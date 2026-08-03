using System.Net;
using System.Reflection;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using FinanceApp.API.Services;
using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

/// <summary>
/// Fixture-based tests for the experimental FinanzenNet quote provider.
/// All tests use saved HTML fixtures; no live network calls are made.
/// </summary>
public class FinanzenNetQuoteServiceTests
{
    // ── Slug validation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("microsoft-aktie", true)]
    [InlineData("sap-aktie", true)]
    [InlineData("a", true)]
    [InlineData("abc123", true)]
    [InlineData("rheinmetall-ag-aktie", true)]
    [InlineData("western_digital-aktie", true)]        // underscore is now valid
    [InlineData("some_slug_with_underscores", true)]    // multiple underscores valid
    public void IsValidSlug_ValidSlugs_ReturnsTrue(string slug, bool expected)
    {
        Assert.Equal(expected, FinanzenNetQuoteService.IsValidSlug(slug));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("MICROSOFT-AKTIE", false)]           // uppercase not allowed
    [InlineData("microsoft/aktie", false)]            // path injection: slash
    [InlineData("../etc/passwd", false)]              // path traversal
    [InlineData("microsoft aktie", false)]            // space
    [InlineData("microsoft.aktie", false)]            // dot
    [InlineData("-microsoft-aktie", false)]           // starts with hyphen
    [InlineData("_microsoft-aktie", false)]           // starts with underscore
    public void IsValidSlug_InvalidSlugs_ReturnsFalse(string? slug, bool expected)
    {
        Assert.Equal(expected, FinanzenNetQuoteService.IsValidSlug(slug));
    }

    // ── German decimal parsing ───────────────────────────────────────────────────

    [Theory]
    [InlineData("415,25", 415.25)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1.000.000,00", 1000000.00)]
    [InlineData("0,50", 0.50)]
    [InlineData("100", 100.00)]
    [InlineData("440,50", 440.50)]
    [InlineData("250,00", 250.00)]
    public void TryParseGermanDecimal_ValidInputs_ParsesCorrectly(string input, double expected)
    {
        var result = FinanzenNetQuoteService.TryParseGermanDecimal(input, out var value);
        Assert.True(result);
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("--")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    public void TryParseGermanDecimal_InvalidInputs_ReturnsFalse(string? input)
    {
        var result = FinanzenNetQuoteService.TryParseGermanDecimal(input, out _);
        Assert.False(result);
    }

    // ── Disabled service ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreMarketQuoteAsync_WhenDisabled_ReturnsFailureWithoutHttpCall()
    {
        var handler = new RecordingHttpMessageHandler();
        var service = CreateService(handler, enabled: false);

        var result = await service.GetPreMarketQuoteAsync("microsoft-aktie");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(0, handler.TotalCalls);
    }

    [Fact]
    public void IsEnabled_WhenDisabledInOptions_ReturnsFalse()
    {
        var service = CreateService(new RecordingHttpMessageHandler(), enabled: false);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WhenEnabledInOptions_ReturnsTrue()
    {
        var service = CreateService(new RecordingHttpMessageHandler(), enabled: true);
        Assert.True(service.IsEnabled);
    }

    // ── Invalid slug ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreMarketQuoteAsync_InvalidSlug_ReturnsBadRequestWithoutHttpCall()
    {
        var handler = new RecordingHttpMessageHandler();
        var service = CreateService(handler, enabled: true);

        var result = await service.GetPreMarketQuoteAsync("INVALID/SLUG");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal(0, handler.TotalCalls);
    }

    // ── HTML parsing: pre-market fixture ─────────────────────────────────────────

    [Fact]
    public async Task ParseDocument_ExplicitPreMarket_ReturnsPriceWithPRESesssion()
    {
        var html = LoadFixture("pre_market_explicit.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "microsoft-aktie");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote);
        Assert.Equal(417.80m, result.Quote!.Price);
        Assert.Equal("PRE", result.Quote.PriceSession);
        Assert.Equal("USD", result.Quote.Currency);
        Assert.Equal("finanzen.net", result.Quote.Source);
    }

    [Fact]
    public async Task ParseDocument_ExplicitPreMarket_TimestampIsExtracted()
    {
        var html = LoadFixture("pre_market_explicit.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "microsoft-aktie");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote!.ProviderTimestampUtc);
        var expected = new DateTime(2025, 1, 15, 9, 12, 34, DateTimeKind.Utc);
        Assert.Equal(expected, result.Quote.ProviderTimestampUtc!.Value);
    }

    [Fact]
    public async Task ParseDocument_RegularPriceOnly_ReturnsNotFound()
    {
        var html = LoadFixture("regular_price_only.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "sap-aktie");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    [Fact]
    public async Task ParseDocument_RegularPriceOnly_IsNeverLabeledPRE()
    {
        // The regular XETRA price must not be returned with session=PRE
        var html = LoadFixture("regular_price_only.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "sap-aktie");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Quote);
    }

    [Fact]
    public async Task ParseDocument_GermanThousandsFormat_ParsedCorrectly()
    {
        var html = LoadFixture("german_format_thousands.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "apple-aktie");

        Assert.True(result.IsSuccess);
        Assert.Equal(1234.56m, result.Quote!.Price);
        Assert.Equal("EUR", result.Quote.Currency);
        Assert.Equal("PRE", result.Quote.PriceSession);
    }

    [Fact]
    public async Task ParseDocument_MultiplePreMarketSections_ReturnsAmbiguousError()
    {
        var html = LoadFixture("ambiguous_multiple_premarket.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "ambiguous-aktie");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.Contains("Ambiguous", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseDocument_NoData_ReturnsNotFound()
    {
        var html = LoadFixture("no_premarket_data.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "some-aktie");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    [Fact]
    public async Task ParseDocument_MissingTimestamp_TimestampIsNull()
    {
        var html = LoadFixture("pre_market_no_timestamp.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "some-aktie");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote);
        // Timestamp must remain null — never substituted with request time
        Assert.Null(result.Quote!.ProviderTimestampUtc);
    }

    [Fact]
    public async Task ParseDocument_MalformedPrice_ReturnsBadGateway()
    {
        var html = LoadFixture("malformed_price.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "some-aktie");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task ParseDocument_DataAttributes_ExtractsPriceAndCurrency()
    {
        var html = LoadFixture("pre_market_data_attributes.html");
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "test-aktie");

        Assert.True(result.IsSuccess);
        // data-value="440.50" in invariant format
        Assert.Equal(440.50m, result.Quote!.Price);
        Assert.Equal("EUR", result.Quote.Currency);
        Assert.Equal("PRE", result.Quote.PriceSession);
    }

    [Fact]
    public async Task ParseDocument_EmptyHtml_ReturnsNotFound()
    {
        var html = "<html><body></body></html>";
        var document = await ParseHtmlAsync(html);

        var result = FinanzenNetQuoteService.ParseDocument(document, "some-aktie");

        Assert.False(result.IsSuccess);
    }

    // ── Caching ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreMarketQuoteAsync_SecondCall_ServedFromCache()
    {
        var html = LoadFixture("pre_market_explicit.html");
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueHtml(html);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(handler, enabled: true, cache: cache);

        var first = await service.GetPreMarketQuoteAsync("microsoft-aktie");
        var second = await service.GetPreMarketQuoteAsync("microsoft-aktie");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(417.80m, second.Quote!.Price);
        // Only one HTTP call should have been made
        Assert.Equal(1, handler.TotalCalls);
    }

    // ── HTTP error handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreMarketQuoteAsync_Http403_ReturnsFailure()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Forbidden));
        var service = CreateService(handler, enabled: true);

        var result = await service.GetPreMarketQuoteAsync("microsoft-aktie");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task GetPreMarketQuoteAsync_Http429_ReturnsFailure()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage((HttpStatusCode)429));
        var service = CreateService(handler, enabled: true);

        var result = await service.GetPreMarketQuoteAsync("microsoft-aktie");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task GetPreMarketQuoteAsync_Timeout_ReturnsFailure()
    {
        var handler = new RecordingHttpMessageHandler
        {
            ThrowOnSend = new TaskCanceledException("Timeout simulated")
        };
        var service = CreateService(handler, enabled: true);

        var result = await service.GetPreMarketQuoteAsync("microsoft-aktie");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    // ── Controller integration: FinanzenNet failure falls back to Yahoo/Finnhub ─

    [Fact]
    public async Task Controller_FinanzenNetFailure_FallsBackToFinnhub()
    {
        var finnhubResult = FinnhubQuoteResult.Success(new FinnhubQuoteData(
            "AAPL", 150m, 149m, 145m, 3.45m, 1720000000, "USD", "USD", "US", "NASDAQ", "REGULAR"));
        var fnService = new StubFinanzenNetQuoteService(
            FinanzenNetQuoteResult.Failure(StatusCodes.Status502BadGateway, "Simulated failure"),
            isEnabled: true);

        var controller = CreateController(
            finnhubResult: finnhubResult,
            yahooResult: YahooQuoteResult.Failure(502, "not used"),
            finanzenNetService: fnService);

        var actionResult = await controller.GetPrice("AAPL", StockExchanges.Nyse, "microsoft-aktie");

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<StockQuoteResponse>(ok.Value);

        // Should use Finnhub price, not FinanzenNet
        Assert.Equal(150m, response.RawCurrentPrice);
        Assert.Equal("LAST", response.PriceSession);
        Assert.Null(response.PriceSource);
    }

    [Fact]
    public async Task Controller_FinanzenNetDisabled_NeverCalled()
    {
        var finnhubResult = FinnhubQuoteResult.Success(new FinnhubQuoteData(
            "AAPL", 150m, 149m, 145m, 3.45m, 1720000000, "USD", "USD", "US", "NASDAQ", "REGULAR"));
        var fnService = new StubFinanzenNetQuoteService(
            FinanzenNetQuoteResult.Failure(StatusCodes.Status503ServiceUnavailable, "Disabled"),
            isEnabled: false);

        var controller = CreateController(
            finnhubResult: finnhubResult,
            yahooResult: YahooQuoteResult.Failure(502, "not used"),
            finanzenNetService: fnService);

        await controller.GetPrice("AAPL", StockExchanges.Nyse, "microsoft-aktie");

        Assert.Equal(0, fnService.CallCount);
    }

    [Fact]
    public async Task Controller_FinanzenNetPreMarketQuote_ReplacesCurrentPrice()
    {
        var finnhubResult = FinnhubQuoteResult.Success(new FinnhubQuoteData(
            "AAPL", 150m, 149m, 145m, 3.45m, 1720000000, "USD", "USD", "US", "NASDAQ", "REGULAR"));
        var fnQuote = new FinanzenNetQuoteData(
            Price: 155m,
            Currency: "USD",
            ProviderTimestampUtc: new DateTime(2025, 1, 15, 9, 0, 0, DateTimeKind.Utc),
            PriceSession: "PRE",
            Venue: "Nasdaq Pre-Market");
        var fnService = new StubFinanzenNetQuoteService(
            FinanzenNetQuoteResult.Success(fnQuote),
            isEnabled: true);

        var controller = CreateController(
            finnhubResult: finnhubResult,
            yahooResult: YahooQuoteResult.Failure(502, "not used"),
            finanzenNetService: fnService);

        var actionResult = await controller.GetPrice("AAPL", StockExchanges.Nyse, "microsoft-aktie");

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<StockQuoteResponse>(ok.Value);

        // Pre-market price should be used
        Assert.Equal(155m, response.RawCurrentPrice);
        Assert.Equal("PRE", response.PriceSession);
        Assert.Equal("finanzen.net", response.PriceSource);
        Assert.Equal(new DateTime(2025, 1, 15, 9, 0, 0, DateTimeKind.Utc), response.PriceTimestampUtc);
    }

    [Fact]
    public async Task Controller_FinanzenNetLastSession_DoesNotReplacePrice()
    {
        var finnhubResult = FinnhubQuoteResult.Success(new FinnhubQuoteData(
            "AAPL", 150m, 149m, 145m, 3.45m, 1720000000, "USD", "USD", "US", "NASDAQ", "REGULAR"));
        // FinanzenNet returns "LAST" session (not PRE) → should NOT replace
        var fnQuote = new FinanzenNetQuoteData(
            Price: 155m,
            Currency: "USD",
            ProviderTimestampUtc: null,
            PriceSession: "LAST",
            Venue: null);
        var fnService = new StubFinanzenNetQuoteService(
            FinanzenNetQuoteResult.Success(fnQuote),
            isEnabled: true);

        var controller = CreateController(
            finnhubResult: finnhubResult,
            yahooResult: YahooQuoteResult.Failure(502, "not used"),
            finanzenNetService: fnService);

        var actionResult = await controller.GetPrice("AAPL", StockExchanges.Nyse, "microsoft-aktie");

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<StockQuoteResponse>(ok.Value);

        // LAST session must not replace Finnhub price
        Assert.Equal(150m, response.RawCurrentPrice);
        Assert.Equal("LAST", response.PriceSession);
        Assert.Null(response.PriceSource);
    }

    [Fact]
    public async Task Controller_InvalidSlug_ReturnsBadRequest()
    {
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Failure(502, "not used"),
            yahooResult: YahooQuoteResult.Failure(502, "not used"));

        var actionResult = await controller.GetPrice("AAPL", StockExchanges.Nyse, "INVALID/SLUG");

        var bad = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.NotNull(bad.Value);
    }

    [Fact]
    public async Task Controller_UnderscoreSlug_IsAccepted()
    {
        var finnhubResult = FinnhubQuoteResult.Success(new FinnhubQuoteData(
            "WDC", 50m, 49m, 45m, 1.0m, 1720000000, "USD", "USD", "US", "NASDAQ", "REGULAR"));

        var controller = CreateController(
            finnhubResult: finnhubResult,
            yahooResult: YahooQuoteResult.Failure(502, "not used"));

        var actionResult = await controller.GetPrice("WDC", StockExchanges.Nyse, "western_digital-aktie");

        // Should not return BadRequest for a valid underscore slug
        Assert.IsNotType<BadRequestObjectResult>(actionResult);
    }

    [Fact]
    public async Task Controller_NoSlugProvided_FinanzenNetNotCalled()
    {
        var finnhubResult = FinnhubQuoteResult.Success(new FinnhubQuoteData(
            "AAPL", 150m, 149m, 145m, 3.45m, 1720000000, "USD", "USD", "US", "NASDAQ", "REGULAR"));
        var fnService = new StubFinanzenNetQuoteService(
            FinanzenNetQuoteResult.Success(new FinanzenNetQuoteData(155m, "USD", null, "PRE", null)),
            isEnabled: true);

        var controller = CreateController(
            finnhubResult: finnhubResult,
            yahooResult: YahooQuoteResult.Failure(502, "not used"),
            finanzenNetService: fnService);

        // No slug provided
        await controller.GetPrice("AAPL", StockExchanges.Nyse, null);

        Assert.Equal(0, fnService.CallCount);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string LoadFixture(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Fixture '{fileName}' not found in embedded resources.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task<IDocument> ParseHtmlAsync(string html)
    {
        var parser = new HtmlParser();
        return await parser.ParseDocumentAsync(html);
    }

    private static FinanzenNetQuoteService CreateService(
        HttpMessageHandler handler,
        bool enabled = true,
        IMemoryCache? cache = null)
    {
        var options = new FinanzenNetOptions
        {
            Enabled = enabled,
            BaseUrl = "https://www.finanzen.net",
            CacheDuration = TimeSpan.FromMinutes(5),
            MinRequestInterval = TimeSpan.FromMilliseconds(1),
            RequestTimeout = TimeSpan.FromSeconds(10)
        };

        var httpClientFactory = new SingleHandlerHttpClientFactory(handler);
        var memoryCache = cache ?? new MemoryCache(new MemoryCacheOptions());

        return new FinanzenNetQuoteService(
            httpClientFactory,
            memoryCache,
            Options.Create(options),
            NullLogger<FinanzenNetQuoteService>.Instance);
    }

    private static StockPriceController CreateController(
        FinnhubQuoteResult? finnhubResult = null,
        YahooQuoteResult? yahooResult = null,
        IFinanzenNetQuoteService? finanzenNetService = null)
    {
        var exchangeRate = new StubExchangeRateService(("USD", 0.91m));
        return new StockPriceController(
            new StubFinnhubQuoteService(finnhubResult ?? FinnhubQuoteResult.Failure(502, "not configured")),
            new StubYahooQuoteService(yahooResult ?? YahooQuoteResult.Failure(502, "not configured")),
            finanzenNetService ?? new StubFinanzenNetQuoteService(
                FinanzenNetQuoteResult.Failure(StatusCodes.Status503ServiceUnavailable, "Disabled"),
                isEnabled: false),
            exchangeRate,
            new StockQuoteConversionService(exchangeRate))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    // ── Stub / helper implementations ────────────────────────────────────────────

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue = new();
        public Exception? ThrowOnSend { get; init; }
        public int TotalCalls { get; private set; }

        public void EnqueueHtml(string html)
        {
            EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });
        }

        public void EnqueueResponse(HttpResponseMessage response)
        {
            _queue.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            if (_queue.Count > 0)
            {
                return Task.FromResult(_queue.Dequeue());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    private sealed class StubFinanzenNetQuoteService : IFinanzenNetQuoteService
    {
        private readonly FinanzenNetQuoteResult _result;
        public int CallCount { get; private set; }
        public bool IsEnabled { get; }

        public StubFinanzenNetQuoteService(FinanzenNetQuoteResult result, bool isEnabled)
        {
            _result = result;
            IsEnabled = isEnabled;
        }

        public Task<FinanzenNetQuoteResult> GetPreMarketQuoteAsync(
            string slug,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubFinnhubQuoteService : IFinnhubQuoteService
    {
        private readonly FinnhubQuoteResult _result;
        public StubFinnhubQuoteService(FinnhubQuoteResult result) => _result = result;
        public Task<FinnhubQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class StubYahooQuoteService : IYahooQuoteService
    {
        private readonly YahooQuoteResult _result;
        public StubYahooQuoteService(YahooQuoteResult result) => _result = result;
        public Task<YahooQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class StubExchangeRateService : IExchangeRateService
    {
        private readonly Dictionary<string, decimal?> _rates;

        public StubExchangeRateService(params (string Currency, decimal? RateToEur)[] rates)
        {
            _rates = rates.ToDictionary(x => x.Currency, x => x.RateToEur, StringComparer.OrdinalIgnoreCase);
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

            if (_rates.TryGetValue(sourceCurrency, out var rate) && rate.HasValue)
            {
                return Task.FromResult(new ExchangeRateResult(sourceCurrency.ToUpperInvariant(), rate.Value, DateTime.UtcNow, "stub", null));
            }

            return Task.FromResult(new ExchangeRateResult(sourceCurrency.ToUpperInvariant(), null, null, "stub", "rate unavailable"));
        }
    }
}
