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
    /// UTC timestamp of the price from the provider (<c>regularMarketTime</c>).
    /// Null when the provider did not supply the field.
    /// </summary>
    DateTime? PriceTimestampUtc = null);

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

            return ParseQuote(symbol, response.Content, _timeProvider);
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

    private static YahooQuoteResult ParseQuote(string symbol, string payload, TimeProvider timeProvider)
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

            if (!TryGetDecimal(meta, "regularMarketPrice", out var currentPrice) || currentPrice <= 0)
            {
                return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider returned an invalid current price.");
            }

            var previousClose = SelectPreviousClose(meta, currentPrice);
            var percentChange = CalculatePercentChange(currentPrice, previousClose);

            var currency = GetOptionalString(meta, "currency");
            var estimateCurrency = GetOptionalString(meta, "financialCurrency");
            var marketState = ParseMarketState(meta, timeProvider);

            // Yahoo chart always returns regularMarketPrice regardless of the current session.
            // PriceSession is therefore always "REGULAR"; MarketState reflects the current session.
            const string priceSession = "REGULAR";
            var priceTimestampUtc = TryGetLong(meta, "regularMarketTime", out var rawPriceTimestamp) && rawPriceTimestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(rawPriceTimestamp).UtcDateTime
                : (DateTime?)null;

            return YahooQuoteResult.Success(new YahooQuoteData(
                symbol,
                currentPrice,
                previousClose,
                percentChange,
                currency,
                estimateCurrency,
                marketState,
                priceSession,
                priceTimestampUtc));
        }
        catch (JsonException)
        {
            return YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider returned an invalid response.");
        }
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
