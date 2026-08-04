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

public class StockHistoryRefreshTests
{
    [Fact]
    public async Task RefreshHistoryAsync_ReplacesOnlySelectedStockRows_AndUsesCurrentProviderSymbol()
    {
        await using var context = CreateContext();
        var target = new Stock { Id = 1, Ticker = "AMZN", Exchange = StockExchanges.Frankfurt, Name = "Amazon FRA" };
        var other = new Stock { Id = 2, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        context.Stocks.AddRange(target, other);
        context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice { StockId = 1, Interval = "1d", Timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Open = 1, High = 1, Low = 1, Close = 1, QuoteUnitMultiplier = 1m },
            new StockHistoricalPrice { StockId = 2, Interval = "1d", Timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Open = 9, High = 9, Low = 9, Close = 9, QuoteUnitMultiplier = 1m });
        await context.SaveChangesAsync();

        var handler = new SequenceHandler(
            SuccessChartJson(1704067200, 10m),
            SuccessChartJson(1704672000, 20m),
            SuccessChartJson(1705276800, 30m),
            SuccessChartJson(1705881600, 40m),
            SuccessChartJson(1706486400, 50m));
        var service = CreateService(context, handler);

        var result = await service.RefreshHistoryAsync(target);

        Assert.Equal(1, result.StockId);
        Assert.Equal(1, result.DeletedPoints);
        Assert.Equal(5, result.ImportedPoints);
        Assert.All(handler.RequestedUrls, url => Assert.Contains("AMZN.F", url, StringComparison.Ordinal));

        var targetRows = await context.StockHistoricalPrices.Where(x => x.StockId == 1).OrderBy(x => x.Interval).ToListAsync();
        Assert.Equal(5, targetRows.Count);
        Assert.DoesNotContain(targetRows, row => row.Close == 1m);
        Assert.Contains(targetRows, row => row.Interval == "10m");
        Assert.Equal(1, await context.StockHistoricalPrices.CountAsync(x => x.StockId == 2));
        Assert.Equal(9m, await context.StockHistoricalPrices.Where(x => x.StockId == 2).Select(x => x.Close).SingleAsync());
    }

    [Fact]
    public async Task RefreshHistoryAsync_WhenProviderReturnsNoData_PreservesExistingHistory()
    {
        await using var context = CreateContext();
        var stock = new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        context.Stocks.Add(stock);
        context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = 1,
            Interval = "1d",
            Timestamp = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Open = 7m,
            High = 7m,
            Low = 7m,
            Close = 7m,
            QuoteUnitMultiplier = 1m
        });
        await context.SaveChangesAsync();

        var handler = new SequenceHandler(
            SuccessChartJson(1704067200, 10m),
            SuccessChartJson(1704672000, 20m),
            EmptyChartJson(),
            SuccessChartJson(1705881600, 40m),
            SuccessChartJson(1706486400, 50m));
        var service = CreateService(context, handler);

        var result = await service.RefreshHistoryAsync(stock);

        Assert.Equal(1, result.DeletedPoints);
        var rows = await context.StockHistoricalPrices.Where(x => x.StockId == 1).OrderBy(x => x.Interval).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain(rows, row => row.Close == 7m);
        Assert.DoesNotContain(rows, row => row.Interval == "1d");
    }

    [Fact]
    public async Task GetHistoryAsync_RangeSelection_ContinuesToWork()
    {
        await using var context = CreateContext();
        var stock = new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        context.Stocks.Add(stock);
        context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = 1,
            Interval = "10m",
            Timestamp = DateTime.UtcNow.AddMinutes(-20),
            Open = 1m,
            High = 2m,
            Low = 1m,
            Close = 2m,
            QuoteCurrency = "USD",
            FinancialCurrency = "USD",
            NormalizedQuoteCurrency = "USD",
            QuoteUnitMultiplier = 1m,
            Volume = 100
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new SequenceHandler());
        var response = await service.GetHistoryAsync(stock, "today");

        Assert.Equal("today", response.Range);
        Assert.Equal("10m", response.Interval);
        Assert.Single(response.Points);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static StockHistoryService CreateService(AppDbContext context, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        return new StockHistoryService(
            context,
            new FixedHttpClientFactory(httpClient),
            new StubStockQuoteConversionService(),
            NullLogger<StockHistoryService>.Instance);
    }

    private static string SuccessChartJson(long unixTimestamp, decimal close) =>
        $@"{{""chart"":{{""result"":[{{""meta"":{{""currency"":""USD"",""financialCurrency"":""USD""}},""timestamp"":[{unixTimestamp}],""indicators"":{{""quote"":[{{""open"":[{close}],""high"":[{close}],""low"":[{close}],""close"":[{close}],""volume"":[100]}}]}}}}]}}}}";

    private static string EmptyChartJson() => """{"chart":{"result":[]}}""";

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FixedHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        private readonly List<string> _requestedUrls = new();

        public SequenceHandler(params string[] responses) => _responses = new Queue<string>(responses);
        public IReadOnlyList<string> RequestedUrls => _requestedUrls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
            var body = _responses.Count > 0 ? _responses.Dequeue() : EmptyChartJson();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubStockQuoteConversionService : IStockQuoteConversionService
    {
        public Task<CurrencyConversionContext> GetConversionContextAsync(string? quoteCurrency, string? financialCurrency, CancellationToken cancellationToken = default)
        {
            var meta = QuoteCurrencyMetadata.Parse(quoteCurrency, financialCurrency);
            var rate = new ExchangeRateResult(quoteCurrency, 1m, DateTime.UtcNow, "stub", null);
            return Task.FromResult(new CurrencyConversionContext(meta, rate, null));
        }

        public StockQuoteResponse BuildQuoteResponse(string symbol, decimal rawCurrentPrice, decimal rawPreviousClose, decimal percentChange, string marketState, CurrencyConversionContext conversionContext, string priceSession = "REGULAR", DateTime? priceTimestampUtc = null, string? priceSource = null)
            => new() { Symbol = symbol };

        public StockHistoryPointResponse BuildHistoryPointResponse(StockHistoricalPrice historicalPrice, CurrencyConversionContext conversionContext)
            => new()
            {
                Timestamp = historicalPrice.Timestamp,
                Interval = historicalPrice.Interval,
                OpenRaw = historicalPrice.Open,
                HighRaw = historicalPrice.High,
                LowRaw = historicalPrice.Low,
                CloseRaw = historicalPrice.Close,
                OpenNormalized = historicalPrice.Open,
                HighNormalized = historicalPrice.High,
                LowNormalized = historicalPrice.Low,
                CloseNormalized = historicalPrice.Close,
                OpenEur = historicalPrice.Open,
                HighEur = historicalPrice.High,
                LowEur = historicalPrice.Low,
                CloseEur = historicalPrice.Close,
                Volume = historicalPrice.Volume
            };
    }
}
