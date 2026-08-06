using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FinanceApp.API.Services;

public interface IYahooQuoteService
{
    Task<YahooQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed record YahooQuoteResult(
    YahooQuoteData? Quote,
    int StatusCode,
    string? ErrorMessage)
{
    public bool IsSuccess => Quote is not null;

    public static YahooQuoteResult Success(YahooQuoteData quote) => new(quote, StatusCodes.Status200OK, null);

    public static YahooQuoteResult Failure(int statusCode, string errorMessage) => new(null, statusCode, errorMessage);
}

public sealed record YahooQuoteData(
    string Symbol,
    decimal CurrentPrice,
    decimal PreviousClose,
    decimal PercentChange,
    string? Currency,
    string? EstimateCurrency,
    string MarketState,
    /// <summary>
    /// Session that the returned price belongs to.  Yahoo's chart endpoint always returns
    /// <c>regularMarketPrice</c>, so this is always <c>"REGULAR"</c> regardless of the
    /// current market state (which may be PRE or POST).
    /// </summary>
    string PriceSession = "REGULAR",
    /// <summary>
    /// UTC timestamp of the price as reported by the provider.
    /// Null when the provider did not supply the field.
    /// </summary>
    DateTime? PriceTimestampUtc = null,
    /// <summary>
    /// True when the quote was successfully parsed but its provider timestamp is significantly
    /// behind the current time during an active trading session, indicating that the price
    /// may be delayed rather than genuinely current.
    /// </summary>
    bool IsDelayed = false,
    /// <summary>
    /// Human-readable reason why the quote is considered delayed/stale, if applicable.
    /// Null when the quote is considered fresh or when no active session could be determined.
    /// </summary>
    string? DelayReason = null);

public sealed class YahooQuoteService : IYahooQuoteService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(20);

    private readonly IYahooRequestCoordinator _yahooRequestCoordinator;
    private readonly ILogger<YahooQuoteService> _logger;
    private readonly YahooFinanceOptions _options;
    private readonly TimeProvider _timeProvider;

    public YahooQuoteService(
        IYahooRequestCoordinator yahooRequestCoordinator,
        ILogger<YahooQuoteService> logger,
        Microsoft.Extensions.Options.IOptions<YahooFinanceOptions> options,
        TimeProvider? timeProvider = null)
    {
        _yahooRequestCoordinator = yahooRequestCoordinator;
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<YahooQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d";
        var safeSymbol = SanitizeForLog(symbol);
        var requestLabel = $"quote:{safeSymbol}";

        try
        {
            var response = await _yahooRequestCoordinator.GetAsync(
                url,
                requestLabel,
                new YahooRequestExecutionOptions(
                    MaxAttempts,
                    RetryBaseDelay,
                    RetryMaxDelay,
                    _options.QuoteCacheDuration),
                cancellationToken);

            if (response.IsRateLimited)
            {
                _logger.LogWarning(
                    "Yahoo quote request rate limit exceeded for {Symbol}; cooldownUntilUtc={CooldownUntilUtc}.",
                    safeSymbol,
                    response.CooldownUntilUtc);
                return YahooQuoteResult.Failure(StatusCodes.Status429TooManyRequests, "Quote provider rate limit exceeded.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Yahoo quote request failed for {Symbol}: {StatusCode}", safeSymbol, (int)response.StatusCode);
                return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider request failed.");
            }

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning("Yahoo quote returned empty response for {Symbol}.", safeSymbol);
                return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider returned an empty response.");
            }

            return ParseQuote(symbol, response.Content, _timeProvider, _options.IntradayStaleThreshold);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Yahoo quote request timed out for {Symbol}.", safeSymbol);
            return YahooQuoteResult.Failure(StatusCodes.Status504GatewayTimeout, "Quote provider request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Yahoo quote request failed for {Symbol}.", safeSymbol);
            return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider request failed.");
        }
    }

    private static string SanitizeForLog(string value) =>
        value.Replace('\r', '_').Replace('\n', '_');

    private static YahooQuoteResult ParseQuote(string symbol, string payload, TimeProvider timeProvider, TimeSpan intradayStaleThreshold)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (!root.TryGetProperty("chart", out var chart) ||
                !chart.TryGetProperty("result", out var resultArray) ||
                resultArray.GetArrayLength() == 0)
            {
                return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider returned an invalid response.");
            }

            var result = resultArray[0];
            if (!result.TryGetProperty("meta", out var meta))
            {
                return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider returned an invalid response.");
            }

            if (!TryGetDecimal(meta, "regularMarketPrice", out var metaPrice) || metaPrice <= 0)
            {
                return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider returned an invalid current price.");
            }

            var metaTimestamp = TryGetLong(meta, "regularMarketTime", out var rawMetaTs) && rawMetaTs > 0
                ? DateTimeOffset.FromUnixTimeSeconds(rawMetaTs).UtcDateTime
                : (DateTime?)null;

            // Try to find a fresher price/timestamp from the candle data.
            // The candle timestamp[] and close[] arrays must stay aligned; we never combine
            // a price from one index with a timestamp from a different index.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var (candlePrice, candleTimestamp) = TrySelectBestCandle(result, now);

            decimal currentPrice;
            DateTime? priceTimestampUtc;

            if (candleTimestamp.HasValue && candlePrice.HasValue &&
                (metaTimestamp is null || candleTimestamp.Value >= metaTimestamp.Value))
            {
                // Candle data is fresher (or metadata timestamp is absent).
                currentPrice = candlePrice.Value;
                priceTimestampUtc = candleTimestamp;
            }
            else
            {
                // Metadata is at least as fresh as the best candle, or there are no usable candles.
                currentPrice = metaPrice;
                priceTimestampUtc = metaTimestamp;
            }

            var previousClose = SelectPreviousClose(meta, currentPrice);
            var percentChange = CalculatePercentChange(currentPrice, previousClose);

            var currency = GetOptionalString(meta, "currency");
            var estimateCurrency = GetOptionalString(meta, "financialCurrency");
            var marketState = ParseMarketState(meta, timeProvider);

            // Yahoo chart always returns regularMarketPrice regardless of the current session.
            // PriceSession is therefore always "REGULAR"; MarketState reflects the current session.
            const string priceSession = "REGULAR";

            // Detect intraday delay: during an active trading session a quote timestamp that
            // trails the current time by more than the configured threshold is considered delayed.
            bool isDelayed = false;
            string? delayReason = null;
            if (priceTimestampUtc.HasValue &&
                IsActiveTradingSession(marketState) &&
                (now - priceTimestampUtc.Value) > intradayStaleThreshold)
            {
                isDelayed = true;
                var lagMinutes = (int)(now - priceTimestampUtc.Value).TotalMinutes;
                delayReason = $"Котировка устарела на {lagMinutes} мин. во время активной торговой сессии ({marketState}).";
            }

            return YahooQuoteResult.Success(new YahooQuoteData(
                symbol,
                currentPrice,
                previousClose,
                percentChange,
                currency,
                estimateCurrency,
                marketState,
                priceSession,
                priceTimestampUtc,
                isDelayed,
                delayReason));
        }
        catch (JsonException)
        {
            return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider returned an invalid response.");
        }
    }

    /// <summary>
    /// Returns true for Yahoo market states that represent an active trading session during
    /// which a lagging quote timestamp is suspicious (REGULAR, PRE, POST).
    /// </summary>
    private static bool IsActiveTradingSession(string marketState) =>
        marketState is "REGULAR" or "PRE" or "POST";

    /// <summary>
    /// Scans the Yahoo chart's <c>timestamp[]</c> and <c>indicators.quote[0].close[]</c>
    /// arrays and returns the newest aligned price/timestamp pair where the close is a valid
    /// positive finite number and the timestamp is positive and not implausibly in the future
    /// (beyond 1 hour from <paramref name="now"/>).
    /// Returns <c>(null, null)</c> when no usable candle is found.
    /// </summary>
    private static (decimal? price, DateTime? timestamp) TrySelectBestCandle(
        JsonElement result,
        DateTime now)
    {
        if (!result.TryGetProperty("timestamp", out var tsArray) ||
            tsArray.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        if (!result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quoteArray) ||
            quoteArray.ValueKind != JsonValueKind.Array ||
            quoteArray.GetArrayLength() == 0)
        {
            return (null, null);
        }

        var quoteObj = quoteArray[0];
        if (!quoteObj.TryGetProperty("close", out var closeArray) ||
            closeArray.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var tsLen = tsArray.GetArrayLength();
        var closeLen = closeArray.GetArrayLength();

        // The arrays must be the same length; if mismatched only use the aligned prefix.
        var len = Math.Min(tsLen, closeLen);
        if (len == 0)
        {
            return (null, null);
        }

        // Future cutoff: reject timestamps more than 1 hour ahead of server clock.
        var futureCutoff = now.AddHours(1);

        decimal? bestPrice = null;
        DateTime? bestTimestamp = null;

        // Scan from newest to oldest; break on first valid candle.
        for (var i = len - 1; i >= 0; i--)
        {
            if (!TryParseUnixTimestamp(tsArray[i], out var tsUnix) || tsUnix <= 0)
            {
                continue;
            }

            var candleDt = DateTimeOffset.FromUnixTimeSeconds(tsUnix).UtcDateTime;
            if (candleDt > futureCutoff)
            {
                continue; // implausibly in the future
            }

            var closeElement = closeArray[i];
            if (closeElement.ValueKind == JsonValueKind.Null)
            {
                continue; // null close — not yet settled
            }

            if (!closeElement.TryGetDecimal(out var closeVal) || closeVal <= 0)
            {
                continue;
            }

            bestPrice = closeVal;
            bestTimestamp = candleDt;
            break;
        }

        return (bestPrice, bestTimestamp);
    }

    private static bool TryParseUnixTimestamp(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static string ParseMarketState(JsonElement meta, TimeProvider timeProvider)
    {
        var state = GetOptionalString(meta, "marketState");
        if (state is not null)
        {
            var mapped = state.ToUpperInvariant() switch
            {
                "REGULAR" => "REGULAR",
                "PRE" or "PREPRE" => "PRE",
                "POST" or "POSTPOST" => "POST",
                "CLOSED" => "CLOSED",
                _ => null
            };
            if (mapped is not null)
            {
                return mapped;
            }
        }

        // Fall back to currentTradingPeriod when marketState is absent or unrecognised.
        var nowUnix = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (!meta.TryGetProperty("currentTradingPeriod", out var ctp))
        {
            return "UNKNOWN";
        }

        if (TryGetPeriodBounds(ctp, "regular", out var regStart, out var regEnd) &&
            nowUnix >= regStart && nowUnix < regEnd)
        {
            return "REGULAR";
        }

        if (TryGetPeriodBounds(ctp, "pre", out var preStart, out var preEnd) &&
            nowUnix >= preStart && nowUnix < preEnd)
        {
            return "PRE";
        }

        if (TryGetPeriodBounds(ctp, "post", out var postStart, out var postEnd) &&
            nowUnix >= postStart && nowUnix < postEnd)
        {
            return "POST";
        }

        // At least one valid period was present, but the current time falls outside all of them.
        var hasAnyPeriod =
            TryGetPeriodBounds(ctp, "regular", out _, out _) ||
            TryGetPeriodBounds(ctp, "pre", out _, out _) ||
            TryGetPeriodBounds(ctp, "post", out _, out _);

        return hasAnyPeriod ? "CLOSED" : "UNKNOWN";
    }

    /// <summary>
    /// Tries to read valid, non-reversed start/end Unix timestamps from a named period
    /// sub-object inside <paramref name="tradingPeriod"/>.
    /// Returns <c>false</c> when the period is absent, malformed, or has start &gt;= end.
    /// </summary>
    private static bool TryGetPeriodBounds(
        JsonElement tradingPeriod,
        string periodName,
        out long start,
        out long end)
    {
        start = 0;
        end = 0;

        if (!tradingPeriod.TryGetProperty(periodName, out var period))
        {
            return false;
        }

        if (!TryGetLong(period, "start", out start) || !TryGetLong(period, "end", out end))
        {
            return false;
        }

        return start < end;
    }

    private static bool TryGetLong(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static decimal SelectPreviousClose(JsonElement meta, decimal currentPrice)
    {
        if (TryGetDecimal(meta, "chartPreviousClose", out var chartPreviousClose) && chartPreviousClose > 0m)
        {
            return chartPreviousClose;
        }

        if (TryGetDecimal(meta, "previousClose", out var previousClose) && previousClose > 0m)
        {
            return previousClose;
        }

        if (TryGetDecimal(meta, "regularMarketChange", out var regularMarketChange))
        {
            var derivedPreviousClose = currentPrice - regularMarketChange;
            if (derivedPreviousClose > 0m)
            {
                return derivedPreviousClose;
            }
        }

        return currentPrice;
    }

    private static decimal CalculatePercentChange(decimal currentPrice, decimal previousClose) =>
        previousClose > 0m
            ? (currentPrice - previousClose) / previousClose * 100m
            : 0m;

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
