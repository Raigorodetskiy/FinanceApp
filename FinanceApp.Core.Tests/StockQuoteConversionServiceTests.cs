using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockQuoteConversionServiceTests
{
    [Fact]
    public async Task EurQuote_PassesThroughWithoutExternalRate()
    {
        var service = CreateService();
        var context = await service.GetConversionContextAsync("EUR", "USD");

        var quote = service.BuildQuoteResponse("BMW.DE", 101.25m, 100m, 1.25m, "REGULAR", context);

        Assert.Equal("EUR", quote.Currency);
        Assert.Equal("USD", quote.FinancialCurrency);
        Assert.Equal("EUR", quote.NormalizedQuoteCurrency);
        Assert.Equal(1m, quote.QuoteUnitMultiplier);
        Assert.Equal(101.25m, quote.NormalizedCurrentPrice);
        Assert.Equal(101.25m, quote.CurrentPriceEur);
        Assert.Null(quote.ConversionWarning);
    }

    [Theory]
    [InlineData("USD", 100, 91)]
    [InlineData("GBP", 10, 11.70)]
    [InlineData("CHF", 10, 10.40)]
    public async Task SupportedMajorCurrencies_ConvertToEur(string currency, decimal rawPrice, decimal expectedEur)
    {
        var service = CreateService(
            ("USD", 0.91m),
            ("GBP", 1.17m),
            ("CHF", 1.04m));

        var context = await service.GetConversionContextAsync(currency, "USD");
        var quote = service.BuildQuoteResponse("TEST", rawPrice, rawPrice - 1m, 1m, "REGULAR", context);

        Assert.Equal(currency, quote.Currency);
        Assert.Equal(currency, quote.NormalizedQuoteCurrency);
        Assert.Equal(1m, quote.QuoteUnitMultiplier);
        Assert.Equal(expectedEur, quote.CurrentPriceEur);
        Assert.Null(quote.ConversionWarning);
    }

    [Theory]
    [InlineData("GBp")]
    [InlineData("GBX")]
    public async Task PenceQuotes_AreNormalizedBeforeGbpToEurConversion(string quoteCurrency)
    {
        var service = CreateService(("GBP", 1.17m));
        var context = await service.GetConversionContextAsync(quoteCurrency, "USD");

        var quote = service.BuildQuoteResponse("BARC.L", 525.40m, 500m, 5.08m, "REGULAR", context);

        Assert.Equal(quoteCurrency, quote.Currency);
        Assert.Equal("USD", quote.FinancialCurrency);
        Assert.Equal("GBP", quote.NormalizedQuoteCurrency);
        Assert.Equal(0.01m, quote.QuoteUnitMultiplier);
        Assert.Equal(5.2540m, quote.NormalizedCurrentPrice);
        Assert.Equal(6.14718m, quote.CurrentPriceEur);
        Assert.Equal(0.2540m, quote.NormalizedChange);
        Assert.Equal(0.29718m, quote.ChangeEur);
    }

    [Fact]
    public async Task GbpAndGbpenceStayDistinctDuringNormalization()
    {
        var service = CreateService(("GBP", 1.17m));

        var poundsContext = await service.GetConversionContextAsync("GBP", "USD");
        var penceContext = await service.GetConversionContextAsync("GBp", "USD");

        Assert.Equal(1m, poundsContext.Metadata.QuoteUnitMultiplier);
        Assert.Equal(0.01m, penceContext.Metadata.QuoteUnitMultiplier);
        Assert.Equal("GBP", poundsContext.Metadata.NormalizedQuoteCurrency);
        Assert.Equal("GBP", penceContext.Metadata.NormalizedQuoteCurrency);
    }

    [Fact]
    public async Task MissingOrUnknownCurrency_DoesNotAssumeUsd()
    {
        var service = CreateService();

        var missingContext = await service.GetConversionContextAsync(null, "USD");
        var missingQuote = service.BuildQuoteResponse("TEST", 12m, 10m, 20m, "REGULAR", missingContext);
        Assert.Null(missingQuote.CurrentPriceEur);
        Assert.Null(missingQuote.NormalizedQuoteCurrency);
        Assert.Equal("Источник котировки не указал валюту, поэтому EUR-конвертация недоступна.", missingQuote.ConversionWarning);

        var unknownContext = await service.GetConversionContextAsync("SEK", "USD");
        var unknownQuote = service.BuildQuoteResponse("TEST", 12m, 10m, 20m, "REGULAR", unknownContext);
        Assert.Equal("SEK", unknownQuote.Currency);
        Assert.Equal("SEK", unknownQuote.NormalizedQuoteCurrency);
        Assert.Equal(12m, unknownQuote.NormalizedCurrentPrice);
        Assert.Null(unknownQuote.CurrentPriceEur);
        Assert.NotNull(unknownQuote.ConversionWarning);
    }

    [Fact]
    public async Task RateFailureLeavesConvertedPriceUnavailable()
    {
        var service = CreateService(("USD", null));
        var context = await service.GetConversionContextAsync("USD", "USD");

        var quote = service.BuildQuoteResponse("TEST", 50m, 49m, 2.0408m, "REGULAR", context);

        Assert.Equal(50m, quote.RawCurrentPrice);
        Assert.Equal(50m, quote.NormalizedCurrentPrice);
        Assert.Null(quote.CurrentPriceEur);
        Assert.Null(quote.ChangeEur);
        Assert.NotNull(quote.ConversionWarning);
    }

    [Fact]
    public async Task HistoricalCandlesUseSameNormalizationAndConversionRules()
    {
        var service = CreateService(("GBP", 1.17m));
        var context = await service.GetConversionContextAsync("GBX", "USD");
        var point = service.BuildHistoryPointResponse(
            new StockHistoricalPrice
            {
                Timestamp = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Utc),
                Interval = "1d",
                Open = 500m,
                High = 540m,
                Low = 495m,
                Close = 525.40m,
                QuoteCurrency = "GBX",
                FinancialCurrency = "USD",
                NormalizedQuoteCurrency = "GBP",
                QuoteUnitMultiplier = 0.01m,
                Volume = 1234
            },
            context);

        Assert.Equal(5m, point.OpenNormalized);
        Assert.Equal(5.40m, point.HighNormalized);
        Assert.Equal(4.95m, point.LowNormalized);
        Assert.Equal(5.2540m, point.CloseNormalized);
        Assert.Equal(5.85m, point.OpenEur);
        Assert.Equal(6.318m, point.HighEur);
        Assert.Equal(5.7915m, point.LowEur);
        Assert.Equal(6.14718m, point.CloseEur);
    }

    // ── Change coherence tests ────────────────────────────────────────────────

    [Fact]
    public async Task EurQuote_ResponseFieldsRemainConsistentWithSelectedBaseline()
    {
        var service = CreateService();
        var context = await service.GetConversionContextAsync("EUR", "USD");

        const decimal currentPrice = 236.30m;
        const decimal previousClose = 230.60m;
        var percentChange = (currentPrice - previousClose) / previousClose * 100m;

        var quote = service.BuildQuoteResponse("AMZ.F", currentPrice, previousClose, percentChange, "CLOSED", context);

        Assert.Equal(currentPrice - previousClose, quote.RawChange);
        Assert.Equal(currentPrice, quote.NormalizedCurrentPrice);
        Assert.Equal(previousClose, quote.NormalizedPreviousClose);
        Assert.Equal(currentPrice - previousClose, quote.NormalizedChange);
        Assert.Equal(currentPrice, quote.CurrentPriceEur);
        Assert.Equal(currentPrice - previousClose, quote.ChangeEur);
        Assert.Equal(percentChange, quote.PercentChange);
    }

    [Fact]
    public async Task EurQuote_PercentChangeMatchesSelectedPreviousClose()
    {
        var service = CreateService();
        var context = await service.GetConversionContextAsync("EUR", "USD");

        const decimal currentPrice = 236.30m;
        const decimal previousClose = 230.60m;
        var percentChange = (currentPrice - previousClose) / previousClose * 100m;

        var quote = service.BuildQuoteResponse("AMZ.F", currentPrice, previousClose, percentChange, "CLOSED", context);

        var impliedPercent = quote.RawPreviousClose > 0m
            ? quote.RawChange / quote.RawPreviousClose * 100m
            : 0m;

        Assert.Equal(impliedPercent, quote.PercentChange);
    }

    [Fact]
    public async Task NullChangeEur_WhenRateUnavailable()
    {
        // Missing baseline (no EUR conversion) must yield null ChangeEur, not a fabricated value.
        var service = CreateService(("USD", null));
        var context = await service.GetConversionContextAsync("USD", "USD");

        var quote = service.BuildQuoteResponse("TEST", 100m, 98m, 2m, "REGULAR", context);

        Assert.Null(quote.ChangeEur);
        Assert.Null(quote.CurrentPriceEur);
    }

    // ── PriceSession / IsStale / PriceTimestampUtc tests ─────────────────────

    [Fact]
    public async Task PriceSession_IsPropagatedToResponse()
    {
        var service = CreateService();
        var context = await service.GetConversionContextAsync("EUR", null);

        var quote = service.BuildQuoteResponse("TEST", 100m, 98m, 2m, "PRE", context, priceSession: "REGULAR");

        Assert.Equal("REGULAR", quote.PriceSession);
        Assert.Equal("PRE", quote.MarketState);
    }

    [Fact]
    public async Task PriceTimestampUtc_IsPropagatedToResponse()
    {
        var ts = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        var fakeNow = new DateTimeOffset(2026, 7, 1, 11, 0, 0, TimeSpan.Zero); // 1 hour later → not stale
        var service = CreateService(timeProvider: new FakeTimeProvider(fakeNow));
        var context = await service.GetConversionContextAsync("EUR", null);

        var quote = service.BuildQuoteResponse("TEST", 100m, 98m, 2m, "REGULAR", context, priceTimestampUtc: ts);

        Assert.Equal(ts, quote.PriceTimestampUtc);
        Assert.False(quote.IsStale);
    }

    [Fact]
    public async Task IsStale_False_WhenTimestampIsNull()
    {
        var service = CreateService();
        var context = await service.GetConversionContextAsync("EUR", null);

        var quote = service.BuildQuoteResponse("TEST", 100m, 98m, 2m, "REGULAR", context);

        Assert.Null(quote.PriceTimestampUtc);
        Assert.False(quote.IsStale);
    }

    [Fact]
    public async Task IsStale_False_WhenTimestampIsRecent()
    {
        var ts = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        var fakeNow = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero); // 8 hours later
        var service = CreateService(timeProvider: new FakeTimeProvider(fakeNow));
        var context = await service.GetConversionContextAsync("EUR", null);

        var quote = service.BuildQuoteResponse("TEST", 100m, 98m, 2m, "REGULAR", context, priceTimestampUtc: ts);

        Assert.False(quote.IsStale);
    }

    [Fact]
    public async Task IsStale_True_WhenTimestampIsOlderThan24Hours()
    {
        // Simulates a Friday close visible on Monday morning (>24 h old)
        var ts = new DateTime(2026, 7, 3, 17, 30, 0, DateTimeKind.Utc); // Friday close
        var fakeNow = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);  // Monday pre-market (>24 h later)
        var service = CreateService(timeProvider: new FakeTimeProvider(fakeNow));
        var context = await service.GetConversionContextAsync("EUR", null);

        var quote = service.BuildQuoteResponse("TEST", 100m, 98m, 2m, "PRE", context, priceTimestampUtc: ts);

        Assert.True(quote.IsStale);
    }

    [Fact]
    public async Task IsStale_False_WhenTimestampIsExactly24HoursOld()
    {
        // Exactly at the boundary – not yet stale
        var ts = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        var fakeNow = new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero); // exactly 24 h
        var service = CreateService(timeProvider: new FakeTimeProvider(fakeNow));
        var context = await service.GetConversionContextAsync("EUR", null);

        var quote = service.BuildQuoteResponse("TEST", 100m, 98m, 2m, "REGULAR", context, priceTimestampUtc: ts);

        Assert.False(quote.IsStale);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private static StockQuoteConversionService CreateService(
        params (string Currency, decimal? RateToEur)[] configuredRates) =>
        new StockQuoteConversionService(new StubExchangeRateService(configuredRates));

    private static StockQuoteConversionService CreateService(
        TimeProvider timeProvider,
        params (string Currency, decimal? RateToEur)[] configuredRates) =>
        new StockQuoteConversionService(new StubExchangeRateService(configuredRates), timeProvider);

    private sealed class StubExchangeRateService : IExchangeRateService
    {
        private readonly Dictionary<string, decimal?> _rates;

        public StubExchangeRateService(IEnumerable<(string Currency, decimal? RateToEur)> configuredRates)
        {
            _rates = configuredRates.ToDictionary(x => x.Currency, x => x.RateToEur, StringComparer.OrdinalIgnoreCase);
        }

        public Task<ExchangeRateResult> GetRateToEurAsync(string? sourceCurrency, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceCurrency))
            {
                return Task.FromResult(new ExchangeRateResult(null, null, null, "stub", "missing currency"));
            }

            if (string.Equals(sourceCurrency, "EUR", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ExchangeRateResult("EUR", 1m, DateTime.UtcNow, "stub", null));
            }

            if (_rates.TryGetValue(sourceCurrency, out var rateToEur) && rateToEur.HasValue)
            {
                return Task.FromResult(new ExchangeRateResult(sourceCurrency.ToUpperInvariant(), rateToEur.Value, DateTime.UtcNow, "stub", null));
            }

            return Task.FromResult(new ExchangeRateResult(sourceCurrency.ToUpperInvariant(), null, null, "stub", "rate unavailable"));
        }
    }
}
