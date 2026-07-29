using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace FinanceApp.API.Services;

public sealed class FrankfurterExchangeRateService : IExchangeRateService
{
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<FrankfurterExchangeRateService> _logger;

    public FrankfurterExchangeRateService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILogger<FrankfurterExchangeRateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<ExchangeRateResult> GetRateToEurAsync(string? sourceCurrency, CancellationToken cancellationToken = default)
    {
        var normalizedCurrency = NormalizeCurrency(sourceCurrency);
        if (normalizedCurrency is null)
        {
            return new ExchangeRateResult(null, null, null, "quote", "Quote currency is missing.");
        }

        if (normalizedCurrency == "EUR")
        {
            return new ExchangeRateResult("EUR", 1m, DateTime.UtcNow, "identity", null);
        }

        var cacheKey = $"fx:{normalizedCurrency}:EUR";
        if (_memoryCache.TryGetValue(cacheKey, out ExchangeRateResult? cachedResult) && cachedResult is not null)
        {
            return cachedResult;
        }

        var result = await FetchRateAsync(normalizedCurrency, cancellationToken);
        _memoryCache.Set(
            cacheKey,
            result,
            result.IsAvailable ? SuccessCacheDuration : FailureCacheDuration);

        return result;
    }

    private async Task<ExchangeRateResult> FetchRateAsync(string sourceCurrency, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

            var url = $"https://api.frankfurter.app/latest?from={Uri.EscapeDataString(sourceCurrency)}&to=EUR";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"Frankfurter returned {(int)response.StatusCode} for {sourceCurrency}/EUR.";
                _logger.LogWarning("Failed to fetch FX rate for {SourceCurrency}: {StatusCode}", sourceCurrency, (int)response.StatusCode);
                return new ExchangeRateResult(sourceCurrency, null, null, "frankfurter.app", error);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            if (!root.TryGetProperty("rates", out var rates) ||
                !rates.TryGetProperty("EUR", out var eurProp) ||
                !TryGetDecimal(eurProp, out var rateToEur))
            {
                const string error = "Unexpected response from frankfurter.app.";
                _logger.LogWarning("Unexpected FX payload for {SourceCurrency}", sourceCurrency);
                return new ExchangeRateResult(sourceCurrency, null, null, "frankfurter.app", error);
            }

            DateTime? rateTimestampUtc = null;
            if (root.TryGetProperty("date", out var dateProp) &&
                dateProp.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(
                    dateProp.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedDate))
            {
                rateTimestampUtc = parsedDate;
            }

            return new ExchangeRateResult(sourceCurrency, rateToEur, rateTimestampUtc ?? DateTime.UtcNow, "frankfurter.app", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to fetch FX rate for {SourceCurrency}", sourceCurrency);
            return new ExchangeRateResult(sourceCurrency, null, null, "frankfurter.app", ex.Message);
        }
    }

    private static string? NormalizeCurrency(string? sourceCurrency)
    {
        var value = sourceCurrency?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.ToUpperInvariant();
    }

    private static bool TryGetDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            value = Convert.ToDecimal(element.GetDouble(), CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }
}
