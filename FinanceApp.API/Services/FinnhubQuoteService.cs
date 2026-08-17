using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services;

public interface IFinnhubQuoteService
{
    Task<FinnhubQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class FinnhubOptions
{
    public string? ApiKey { get; set; }
    public TimeSpan MaxAcceptedRetryAfter { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed record FinnhubQuoteResult(
    FinnhubQuoteData? Quote,
    int StatusCode,
    string? ErrorMessage,
    TimeSpan? RetryAfterDelay = null)
{
    public bool IsSuccess => Quote is not null;

    public static FinnhubQuoteResult Success(FinnhubQuoteData quote) => new(quote, StatusCodes.Status200OK, null);

    public static FinnhubQuoteResult Failure(int statusCode, string errorMessage, TimeSpan? retryAfterDelay = null)
        => new(null, statusCode, errorMessage, retryAfterDelay);
}

public sealed record FinnhubQuoteData(
    string Symbol,
    decimal CurrentPrice,
    decimal OpenPrice,
    decimal PreviousClose,
    decimal PercentChange,
    long QuoteTimestampUnix,
    string? Currency,
    string? EstimateCurrency,
    string? Country,
    string? Exchange,
    string MarketState,
    /// <summary>
    /// Session the price belongs to.  Finnhub exposes a generic "current/last" price that
    /// cannot be reliably attributed to a specific session, so this is always <c>"LAST"</c>.
    /// </summary>
    string PriceSession = "LAST",
    /// <summary>Current regular-session day high as returned by Finnhub ("h"). Null when absent or zero.</summary>
    decimal? DayHigh = null,
    /// <summary>Current regular-session day low as returned by Finnhub ("l"). Null when absent or zero.</summary>
    decimal? DayLow = null);

public sealed class FinnhubQuoteService : IFinnhubQuoteService
{
    private static readonly TimeSpan ProfileCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan MarketStatusCacheDuration = TimeSpan.FromMinutes(1);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly FinnhubOptions _options;
    private readonly ILogger<FinnhubQuoteService> _logger;
    private readonly TimeProvider _timeProvider;

    public FinnhubQuoteService(
        HttpClient httpClient,
        IMemoryCache memoryCache,
        IOptions<FinnhubOptions> options,
        ILogger<FinnhubQuoteService> logger,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<FinnhubQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var apiKey = _options.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return FinnhubQuoteResult.Failure(
                StatusCodes.Status500InternalServerError,
                "Finnhub API key is not configured.");
        }

        var quotePayload = await SendRequestAsync(
            $"quote?symbol={Uri.EscapeDataString(symbol)}&token={Uri.EscapeDataString(apiKey)}",
            "quote",
            cancellationToken);
        if (!quotePayload.IsSuccess || quotePayload.Payload is null)
        {
            return FinnhubQuoteResult.Failure(
                quotePayload.StatusCode,
                quotePayload.ErrorMessage ?? "Quote provider request failed.",
                quotePayload.RetryAfterDelay);
        }

        var quoteParseResult = ParseQuote(symbol, quotePayload.Payload);
        if (!quoteParseResult.IsSuccess || quoteParseResult.Quote is null)
        {
            return FinnhubQuoteResult.Failure(
                quoteParseResult.StatusCode,
                quoteParseResult.ErrorMessage ?? "Quote provider returned an invalid response.");
        }

        var profileResult = await GetProfileAsync(symbol, apiKey, cancellationToken);
        if (!profileResult.IsSuccess || profileResult.Profile is null)
        {
            return FinnhubQuoteResult.Failure(
                profileResult.StatusCode,
                profileResult.ErrorMessage ?? "Quote provider request failed.");
        }

        var marketState = await GetMarketStateAsync(profileResult.Profile, apiKey, cancellationToken);
        var quote = quoteParseResult.Quote with
        {
            Currency = profileResult.Profile.Currency,
            EstimateCurrency = profileResult.Profile.EstimateCurrency,
            Country = profileResult.Profile.Country,
            Exchange = profileResult.Profile.Exchange,
            MarketState = marketState
        };

        return FinnhubQuoteResult.Success(quote);
    }

    private async Task<FinnhubProfileResult> GetProfileAsync(string symbol, string apiKey, CancellationToken cancellationToken)
    {
        var cacheKey = $"finnhub:profile:{symbol.ToUpperInvariant()}";
        if (_memoryCache.TryGetValue(cacheKey, out FinnhubProfileData? cachedProfile) && cachedProfile is not null)
        {
            return FinnhubProfileResult.Success(cachedProfile);
        }

        var payloadResult = await SendRequestAsync(
            $"stock/profile2?symbol={Uri.EscapeDataString(symbol)}&token={Uri.EscapeDataString(apiKey)}",
            "profile2",
            cancellationToken);
        if (!payloadResult.IsSuccess || payloadResult.Payload is null)
        {
            return FinnhubProfileResult.Failure(payloadResult.StatusCode, payloadResult.ErrorMessage ?? "Quote provider request failed.");
        }

        var parseResult = ParseProfile(payloadResult.Payload);
        if (!parseResult.IsSuccess || parseResult.Profile is null)
        {
            return parseResult;
        }

        _memoryCache.Set(cacheKey, parseResult.Profile, ProfileCacheDuration);
        return parseResult;
    }

    private async Task<string> GetMarketStateAsync(FinnhubProfileData profile, string apiKey, CancellationToken cancellationToken)
    {
        var exchange = ResolveMarketStatusExchange(profile);
        if (exchange is null)
        {
            return "UNKNOWN";
        }

        var cacheKey = $"finnhub:market-status:{exchange}";
        if (_memoryCache.TryGetValue(cacheKey, out string? cachedMarketState) && !string.IsNullOrWhiteSpace(cachedMarketState))
        {
            return cachedMarketState;
        }

        var payloadResult = await SendRequestAsync(
            $"stock/market-status?exchange={Uri.EscapeDataString(exchange)}&token={Uri.EscapeDataString(apiKey)}",
            "market-status",
            cancellationToken);
        if (!payloadResult.IsSuccess || payloadResult.Payload is null)
        {
            _logger.LogWarning(
                "Falling back to UNKNOWN market state for exchange {Exchange} due to Finnhub market-status failure.",
                exchange);
            return "UNKNOWN";
        }

        var marketState = ParseMarketState(payloadResult.Payload);
        _memoryCache.Set(cacheKey, marketState, MarketStatusCacheDuration);
        return marketState;
    }

    private async Task<FinnhubPayloadResult> SendRequestAsync(string relativeUri, string endpointName, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
            if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests)
            {
                _logger.LogWarning("Finnhub {Endpoint} request hit rate limit.", endpointName);
                return FinnhubPayloadResult.Failure(
                    StatusCodes.Status429TooManyRequests,
                    "Quote provider rate limit exceeded.",
                    ParseRetryAfterDelay(response.Headers.RetryAfter));
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Finnhub {Endpoint} request failed with status {StatusCode}.",
                    endpointName,
                    (int)response.StatusCode);
                return FinnhubPayloadResult.Failure(StatusCodes.Status502BadGateway, "Quote provider request failed.");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                _logger.LogWarning("Finnhub {Endpoint} returned an empty response.", endpointName);
                return FinnhubPayloadResult.Failure(StatusCodes.Status502BadGateway, "Quote provider returned an empty response.");
            }

            return FinnhubPayloadResult.Success(payload);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Finnhub {Endpoint} request timed out.", endpointName);
            return FinnhubPayloadResult.Failure(StatusCodes.Status504GatewayTimeout, "Quote provider request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Finnhub {Endpoint} request failed.", endpointName);
            return FinnhubPayloadResult.Failure(StatusCodes.Status502BadGateway, "Quote provider request failed.");
        }
    }

    private static FinnhubQuoteResult ParseQuote(string symbol, string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (!TryGetDecimal(root, "c", out var currentPrice) || currentPrice <= 0)
            {
                return FinnhubQuoteResult.Failure(
                    StatusCodes.Status502BadGateway,
                    "Quote provider returned an invalid current price.");
            }

            var openPrice = TryGetDecimal(root, "o", out var parsedOpenPrice) ? parsedOpenPrice : 0m;
            var previousClose = TryGetDecimal(root, "pc", out var parsedPreviousClose) ? parsedPreviousClose : currentPrice;
            var percentChange = TryGetDecimal(root, "dp", out var parsedPercentChange)
                ? parsedPercentChange
                : previousClose > 0 ? (currentPrice - previousClose) / previousClose * 100m : 0m;
            var timestamp = TryGetInt64(root, "t", out var parsedTimestamp) ? parsedTimestamp : 0L;

            decimal? dayHigh = TryGetDecimal(root, "h", out var parsedDayHigh) && parsedDayHigh > 0 ? parsedDayHigh : null;
            decimal? dayLow = TryGetDecimal(root, "l", out var parsedDayLow) && parsedDayLow > 0 ? parsedDayLow : null;

            return FinnhubQuoteResult.Success(new FinnhubQuoteData(
                symbol,
                currentPrice,
                openPrice,
                previousClose,
                percentChange,
                timestamp,
                null,
                null,
                null,
                null,
                "UNKNOWN",
                DayHigh: dayHigh,
                DayLow: dayLow));
        }
        catch (JsonException)
        {
            return FinnhubQuoteResult.Failure(
                StatusCodes.Status502BadGateway,
                "Quote provider returned an invalid response.");
        }
    }

    private static FinnhubProfileResult ParseProfile(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            return FinnhubProfileResult.Success(new FinnhubProfileData(
                GetOptionalString(root, "currency"),
                GetOptionalString(root, "estimateCurrency"),
                GetOptionalString(root, "country"),
                GetOptionalString(root, "exchange")));
        }
        catch (JsonException)
        {
            return FinnhubProfileResult.Failure(
                StatusCodes.Status502BadGateway,
                "Quote provider returned an invalid response.");
        }
    }

    private static string ParseMarketState(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var session = GetOptionalString(root, "session");
            if (session is not null)
            {
                return session.ToUpperInvariant() switch
                {
                    "REGULAR" => "REGULAR",
                    "PRE" or "PREMARKET" or "PRE-MARKET" => "PRE",
                    "POST" or "POSTMARKET" or "POST-MARKET" => "POST",
                    "CLOSED" => "CLOSED",
                    _ => GetBoolean(root, "isOpen") == false ? "CLOSED" : "UNKNOWN"
                };
            }

            return GetBoolean(root, "isOpen") == false ? "CLOSED" : "UNKNOWN";
        }
        catch (JsonException)
        {
            return "UNKNOWN";
        }
    }

    private static string? ResolveMarketStatusExchange(FinnhubProfileData profile)
    {
        var country = profile.Country?.Trim();
        if (string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        if (country.Length == 2)
        {
            return country.ToUpperInvariant();
        }

        return country.ToUpperInvariant() switch
        {
            "UNITED STATES" => "US",
            "GERMANY" => "DE",
            "UNITED KINGDOM" => "GB",
            _ => null
        };
    }

    private static bool TryGetDecimal(JsonElement root, string propertyName, out decimal value)
    {
        value = 0m;
        if (!root.TryGetProperty(propertyName, out var property))
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

    private static bool TryGetInt64(JsonElement root, string propertyName, out long value)
    {
        value = 0;
        if (!root.TryGetProperty(propertyName, out var property))
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

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record FinnhubPayloadResult(
        string? Payload,
        int StatusCode,
        string? ErrorMessage,
        TimeSpan? RetryAfterDelay = null)
    {
        public bool IsSuccess => Payload is not null;

        public static FinnhubPayloadResult Success(string payload) => new(payload, StatusCodes.Status200OK, null);

        public static FinnhubPayloadResult Failure(int statusCode, string errorMessage, TimeSpan? retryAfterDelay = null)
            => new(null, statusCode, errorMessage, retryAfterDelay);
    }

    private TimeSpan? ParseRetryAfterDelay(System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return ClampRetryAfter(delta);
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - _timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                return ClampRetryAfter(delay);
            }
        }

        return null;
    }

    private TimeSpan ClampRetryAfter(TimeSpan delay)
    {
        var max = _options.MaxAcceptedRetryAfter > TimeSpan.Zero
            ? _options.MaxAcceptedRetryAfter
            : TimeSpan.FromMinutes(5);
        return delay > max ? max : delay;
    }

    private sealed record FinnhubProfileResult(
        FinnhubProfileData? Profile,
        int StatusCode,
        string? ErrorMessage)
    {
        public bool IsSuccess => Profile is not null;

        public static FinnhubProfileResult Success(FinnhubProfileData profile) => new(profile, StatusCodes.Status200OK, null);

        public static FinnhubProfileResult Failure(int statusCode, string errorMessage) => new(null, statusCode, errorMessage);
    }

    private sealed record FinnhubProfileData(
        string? Currency,
        string? EstimateCurrency,
        string? Country,
        string? Exchange);
}
