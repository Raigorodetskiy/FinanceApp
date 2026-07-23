using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public class StockHistoryService : IStockHistoryService
{
    private const int MaxYahooRequestAttempts = 5;
    private static readonly TimeSpan YahooRequestThrottleDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan YahooRetryBaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan YahooRetryMaxDelay = TimeSpan.FromSeconds(20);
    private static readonly SemaphoreSlim YahooRequestGate = new(1, 1);
    private static DateTime _nextYahooRequestUtc = DateTime.MinValue;

    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StockHistoryService> _logger;

    public StockHistoryService(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<StockHistoryService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
    {
        var stocks = await _dbContext.Stocks
            .Where(s => s.Ticker != null && s.Ticker != string.Empty)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SyncHistoricalDataForStockAsync(stock, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed syncing history for stock {StockId}", stock.Id);
            }
        }
    }

    public async Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stock.Ticker))
        {
            return;
        }

        var monthly = await FetchCandlesAsync(stock.Ticker, "1mo", "5y", cancellationToken);
        await UpsertCandlesAsync(stock.Id, "1mo", monthly, cancellationToken);

        var weekly = await FetchCandlesAsync(stock.Ticker, "1wk", "1y", cancellationToken);
        await UpsertCandlesAsync(stock.Id, "1wk", weekly, cancellationToken);

        var daily = await FetchCandlesAsync(stock.Ticker, "1d", "1y", cancellationToken);
        await UpsertCandlesAsync(stock.Id, "1d", daily, cancellationToken);

        var hourly = await FetchCandlesAsync(stock.Ticker, "1h", "7d", cancellationToken);
        await UpsertCandlesAsync(stock.Id, "1h", hourly, cancellationToken);

        var fiveMinute = await FetchCandlesAsync(stock.Ticker, "5m", "1d", cancellationToken);
        var tenMinute = AggregateToTenMinute(fiveMinute);
        await UpsertCandlesAsync(stock.Id, "10m", tenMinute, cancellationToken);
    }

    public async Task<IReadOnlyList<StockHistoricalPrice>> GetHistoryAsync(int stockId, string range, CancellationToken cancellationToken = default)
    {
        var normalizedRange = NormalizeRange(range);

        var now = DateTime.UtcNow;
        var from = normalizedRange switch
        {
            "5y" => now.AddYears(-5),
            "3y" => now.AddYears(-3),
            "1y" => now.AddYears(-1),
            "6m" => now.AddMonths(-6),
            "3m" => now.AddMonths(-3),
            "1m" => now.AddMonths(-1),
            "1w" => now.AddDays(-7),
            "24h" => now.AddHours(-24),
            "today" => now.Date,
            _ => now.AddYears(-5)
        };

        var interval = normalizedRange switch
        {
            "5y" or "3y" => "1mo",
            "1y" => "1wk",
            "6m" or "3m" or "1m" => "1d",
            "1w" => "1h",
            "24h" or "today" => "10m",
            _ => "1mo"
        };

        return await _dbContext.StockHistoricalPrices
            .AsNoTracking()
            .Where(x => x.StockId == stockId && x.Interval == interval && x.Timestamp >= from)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(cancellationToken);
    }

    private static string NormalizeRange(string range)
    {
        var value = (range ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "5y" => "5y",
            "3y" => "3y",
            "1y" => "1y",
            "6m" => "6m",
            "3m" => "3m",
            "1m" => "1m",
            "1w" => "1w",
            "24h" => "24h",
            "today" => "today",
            _ => "5y"
        };
    }

    private async Task UpsertCandlesAsync(int stockId, string interval, IReadOnlyList<CandleData> candles, CancellationToken cancellationToken)
    {
        if (candles.Count == 0)
        {
            return;
        }

        var minTimestamp = candles.Min(x => x.Timestamp);
        var maxTimestamp = candles.Max(x => x.Timestamp);

        var existing = await _dbContext.StockHistoricalPrices
            .Where(x =>
                x.StockId == stockId &&
                x.Interval == interval &&
                x.Timestamp >= minTimestamp &&
                x.Timestamp <= maxTimestamp)
            .ToListAsync(cancellationToken);

        var existingByTimestamp = existing.ToDictionary(x => x.Timestamp, x => x);

        foreach (var candle in candles)
        {
            if (existingByTimestamp.TryGetValue(candle.Timestamp, out var row))
            {
                row.Open = candle.Open;
                row.High = candle.High;
                row.Low = candle.Low;
                row.Close = candle.Close;
                row.Volume = candle.Volume;
            }
            else
            {
                _dbContext.StockHistoricalPrices.Add(new StockHistoricalPrice
                {
                    StockId = stockId,
                    Timestamp = candle.Timestamp,
                    Interval = interval,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    Volume = candle.Volume
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CandleData>> FetchCandlesAsync(string symbol, string interval, string range, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval={interval}&range={range}";
        for (var attempt = 1; attempt <= MaxYahooRequestAttempts; attempt++)
        {
            try
            {
                using var response = await SendYahooRequestAsync(client, url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    return ParseCandles(doc.RootElement);
                }

                if (IsTransientStatusCode(response.StatusCode) && attempt < MaxYahooRequestAttempts)
                {
                    var delay = GetRetryDelay(attempt, response.Headers.RetryAfter);
                    _logger.LogWarning(
                        "Yahoo history request transient failure for symbol={Symbol} interval={Interval} range={Range} status={StatusCode}; retry {Attempt}/{MaxAttempts} in {DelayMs}ms",
                        symbol,
                        interval,
                        range,
                        (int)response.StatusCode,
                        attempt,
                        MaxYahooRequestAttempts,
                        (int)delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                _logger.LogWarning(
                    "Yahoo history request failed for symbol={Symbol} interval={Interval} range={Range}: {StatusCode}",
                    symbol,
                    interval,
                    range,
                    (int)response.StatusCode);
                return Array.Empty<CandleData>();
            }
            catch (HttpRequestException ex) when (attempt < MaxYahooRequestAttempts)
            {
                var delay = GetRetryDelay(attempt, null);
                _logger.LogWarning(
                    ex,
                    "Yahoo history request network error for symbol={Symbol} interval={Interval} range={Range}; retry {Attempt}/{MaxAttempts} in {DelayMs}ms",
                    symbol,
                    interval,
                    range,
                    attempt,
                    MaxYahooRequestAttempts,
                    (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxYahooRequestAttempts)
            {
                var delay = GetRetryDelay(attempt, null);
                _logger.LogWarning(
                    ex,
                    "Yahoo history request timed out for symbol={Symbol} interval={Interval} range={Range}; retry {Attempt}/{MaxAttempts} in {DelayMs}ms",
                    symbol,
                    interval,
                    range,
                    attempt,
                    MaxYahooRequestAttempts,
                    (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        _logger.LogWarning(
            "Yahoo history request failed after retries for symbol={Symbol} interval={Interval} range={Range}",
            symbol,
            interval,
            range);
        return Array.Empty<CandleData>();
    }

    private static IReadOnlyList<CandleData> ParseCandles(JsonElement root)
    {
        if (!root.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var resultArray) ||
            resultArray.GetArrayLength() == 0)
        {
            return Array.Empty<CandleData>();
        }

        var result = resultArray[0];
        if (!result.TryGetProperty("timestamp", out var timestamps) ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quoteArray) ||
            quoteArray.GetArrayLength() == 0)
        {
            return Array.Empty<CandleData>();
        }

        var quote = quoteArray[0];
        if (!quote.TryGetProperty("close", out var closeArray))
        {
            return Array.Empty<CandleData>();
        }

        var openArray = quote.TryGetProperty("open", out var openElement) ? openElement : default;
        var highArray = quote.TryGetProperty("high", out var highElement) ? highElement : default;
        var lowArray = quote.TryGetProperty("low", out var lowElement) ? lowElement : default;
        var volumeArray = quote.TryGetProperty("volume", out var volumeElement) ? volumeElement : default;

        var candles = new List<CandleData>();
        var pointsCount = timestamps.GetArrayLength();
        for (var i = 0; i < pointsCount; i++)
        {
            if (!TryGetInt64(timestamps, i, out var unixTimestamp))
            {
                continue;
            }

            if (!TryGetDecimal(closeArray, i, out var close))
            {
                continue;
            }

            var open = TryGetDecimal(openArray, i, out var parsedOpen) ? parsedOpen : close;
            var high = TryGetDecimal(highArray, i, out var parsedHigh) ? parsedHigh : close;
            var low = TryGetDecimal(lowArray, i, out var parsedLow) ? parsedLow : close;
            var volume = TryGetInt64(volumeArray, i, out var parsedVolume) ? parsedVolume : 0L;

            candles.Add(new CandleData(
                DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime,
                open,
                high,
                low,
                close,
                volume));
        }

        return candles
            .OrderBy(x => x.Timestamp)
            .ToList();
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        var retryAfterDelay = GetRetryAfterDelay(retryAfter);
        if (retryAfterDelay.HasValue)
        {
            return retryAfterDelay.Value;
        }

        var exponentialMs = Math.Min(
            YahooRetryBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Max(attempt - 1, 0)),
            YahooRetryMaxDelay.TotalMilliseconds);
        var jitterMs = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(exponentialMs + jitterMs);
    }

    private static TimeSpan? GetRetryAfterDelay(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta > YahooRetryMaxDelay ? YahooRetryMaxDelay : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay > YahooRetryMaxDelay ? YahooRetryMaxDelay : delay;
            }
        }

        return null;
    }

    private static async Task<HttpResponseMessage> SendYahooRequestAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        await YahooRequestGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            if (_nextYahooRequestUtc > now)
            {
                await Task.Delay(_nextYahooRequestUtc - now, cancellationToken);
            }

            var response = await client.GetAsync(url, cancellationToken);
            _nextYahooRequestUtc = DateTime.UtcNow.Add(YahooRequestThrottleDelay);
            return response;
        }
        finally
        {
            YahooRequestGate.Release();
        }
    }

    private static IReadOnlyList<CandleData> AggregateToTenMinute(IReadOnlyList<CandleData> fiveMinuteCandles)
    {
        return fiveMinuteCandles
            .GroupBy(x => new DateTime(
                x.Timestamp.Year,
                x.Timestamp.Month,
                x.Timestamp.Day,
                x.Timestamp.Hour,
                (x.Timestamp.Minute / 10) * 10,
                0,
                DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.Timestamp).ToList();
                return new CandleData(
                    g.Key,
                    ordered.First().Open,
                    ordered.Max(x => x.High),
                    ordered.Min(x => x.Low),
                    ordered.Last().Close,
                    ordered.Sum(x => x.Volume));
            })
            .ToList();
    }

    private static bool TryGetDecimal(JsonElement arrayElement, int index, out decimal value)
    {
        value = 0m;
        if (arrayElement.ValueKind != JsonValueKind.Array || index < 0 || index >= arrayElement.GetArrayLength())
        {
            return false;
        }

        var element = arrayElement[index];
        if (element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        try
        {
            if (element.TryGetDecimal(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                value = Convert.ToDecimal(element.GetDouble(), CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryGetInt64(JsonElement arrayElement, int index, out long value)
    {
        value = 0L;
        if (arrayElement.ValueKind != JsonValueKind.Array || index < 0 || index >= arrayElement.GetArrayLength())
        {
            return false;
        }

        var element = arrayElement[index];
        if (element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        try
        {
            if (element.TryGetInt64(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                value = Convert.ToInt64(element.GetDouble());
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private sealed record CandleData(
        DateTime Timestamp,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume);
}
