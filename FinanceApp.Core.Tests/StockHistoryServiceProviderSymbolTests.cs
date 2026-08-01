using System.Net;
using System.Text;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

/// <summary>
/// Tests that verify <see cref="StockHistoryService.SyncHistoricalDataForStockAsync"/>
/// uses the same resolved provider symbol (<c>AMZN.F</c>) as the current-quote path
/// when the stock's exchange is Frankfurt.
///
/// Requirement 7: Frankfurt history invokes Yahoo with the resolved .F symbol for
/// all supported ranges/refresh flows.
/// </summary>
public class StockHistoryServiceProviderSymbolTests
{
    // Minimal valid Yahoo chart response that causes no retries.
    private const string EmptyChartJson = """{"chart":{"result":[]}}""";

    // ── Main regression ─────────────────────────────────────────────────────────

    /// <summary>
    /// Requirement 7 + 9: SyncHistoricalDataForStockAsync for a Frankfurt stock stored
    /// with bare ticker "AMZN" must request Yahoo URLs containing "AMZN.F", never "AMZN".
    /// </summary>
    [Fact]
    public async Task SyncHistoricalData_FrankfurtBareAmzn_AllUrlsContainDotFSymbol()
    {
        var capturer = new UrlCapturingHttpMessageHandler(EmptyChartJson);
        await RunSyncAsync(new Stock
        {
            Id = 1,
            Ticker = "AMZN",
            Exchange = StockExchanges.Frankfurt,
            Name = "Amazon Frankfurt",
        }, capturer);

        Assert.NotEmpty(capturer.RequestedUrls);

        foreach (var url in capturer.RequestedUrls)
        {
            // Every Yahoo URL must contain the resolved symbol, not the bare one.
            Assert.Contains("AMZN.F", url, StringComparison.Ordinal);
            Assert.DoesNotContain("/AMZN?", url, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// When the ticker already ends with ".F" (e.g. stored as "AMZN.F"), the symbol
    /// must not be doubled to "AMZN.F.F".
    /// </summary>
    [Fact]
    public async Task SyncHistoricalData_FrankfurtAlreadyDotF_UrlsContainExactlyDotF()
    {
        var capturer = new UrlCapturingHttpMessageHandler(EmptyChartJson);
        await RunSyncAsync(new Stock
        {
            Id = 2,
            Ticker = "AMZN.F",
            Exchange = StockExchanges.Frankfurt,
            Name = "Amazon Frankfurt",
        }, capturer);

        Assert.NotEmpty(capturer.RequestedUrls);

        foreach (var url in capturer.RequestedUrls)
        {
            Assert.Contains("AMZN.F", url, StringComparison.Ordinal);
            Assert.DoesNotContain("AMZN.F.F", url, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// NYSE stocks must NOT receive the ".F" suffix appended to their Yahoo URLs.
    /// </summary>
    [Fact]
    public async Task SyncHistoricalData_NyseStock_UrlsContainOriginalTickerNoF()
    {
        var capturer = new UrlCapturingHttpMessageHandler(EmptyChartJson);
        await RunSyncAsync(new Stock
        {
            Id = 3,
            Ticker = "AAPL",
            Exchange = StockExchanges.Nyse,
            Name = "Apple Inc.",
        }, capturer);

        Assert.NotEmpty(capturer.RequestedUrls);

        foreach (var url in capturer.RequestedUrls)
        {
            Assert.Contains("AAPL", url, StringComparison.Ordinal);
            Assert.DoesNotContain("AAPL.F", url, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Requirement 8: Quote and history paths use identical provider symbols.
    /// Both call StockExchanges.ResolveProviderSymbol; this verifies they agree.
    /// </summary>
    [Fact]
    public async Task SyncHistoricalData_FrankfurtAmzn_ProviderSymbolMatchesQuotePath()
    {
        var capturer = new UrlCapturingHttpMessageHandler(EmptyChartJson);
        await RunSyncAsync(new Stock
        {
            Id = 4,
            Ticker = "AMZN",
            Exchange = StockExchanges.Frankfurt,
            Name = "Amazon Frankfurt",
        }, capturer);

        // The provider symbol that StockPriceController would resolve
        var expectedQuoteSymbol = StockExchanges.ResolveProviderSymbol("AMZN", StockExchanges.Frankfurt);

        Assert.NotEmpty(capturer.RequestedUrls);
        foreach (var url in capturer.RequestedUrls)
        {
            Assert.Contains(expectedQuoteSymbol, url, StringComparison.Ordinal);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task RunSyncAsync(Stock stock, HttpMessageHandler handler)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new AppDbContext(options);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new FixedHttpClientFactory(httpClient);

        var service = new StockHistoryService(
            context,
            factory,
            new StubStockQuoteConversionService(),
            NullLogger<StockHistoryService>.Instance);

        await service.SyncHistoricalDataForStockAsync(stock);
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FixedHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    /// <summary>
    /// HTTP handler that captures every requested URL and responds with a
    /// configurable JSON body and HTTP 200, ensuring no retries are triggered.
    /// </summary>
    private sealed class UrlCapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly List<string> _urls = new();

        public UrlCapturingHttpMessageHandler(string responseJson) => _responseJson = responseJson;

        public IReadOnlyList<string> RequestedUrls => _urls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _urls.Add(request.RequestUri?.ToString() ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubStockQuoteConversionService : IStockQuoteConversionService
    {
        public Task<CurrencyConversionContext> GetConversionContextAsync(
            string? quoteCurrency,
            string? financialCurrency,
            CancellationToken cancellationToken = default)
        {
            var meta = QuoteCurrencyMetadata.Parse(quoteCurrency, financialCurrency);
            var rate = new ExchangeRateResult(quoteCurrency, 1m, DateTime.UtcNow, "stub", null);
            return Task.FromResult(new CurrencyConversionContext(meta, rate, null));
        }

        public StockQuoteResponse BuildQuoteResponse(
            string symbol,
            decimal rawCurrentPrice,
            decimal rawPreviousClose,
            decimal percentChange,
            string marketState,
            CurrencyConversionContext conversionContext,
            string priceSession = "REGULAR",
            DateTime? priceTimestampUtc = null,
            string? priceSource = null)
            => new() { Symbol = symbol };

        public StockHistoryPointResponse BuildHistoryPointResponse(
            StockHistoricalPrice historicalPrice,
            CurrencyConversionContext conversionContext)
            => new();
    }
}
