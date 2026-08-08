using System.Net;
using System.Text;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockHistoryRefreshTests
{
    [Fact]
    public async Task RefreshHistoryAsync_ReplacesOnlySelectedStockRows_AndUsesCurrentProviderSymbol()
    {
        await using var context = CreateInMemoryContext();
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
        await using var context = CreateInMemoryContext();
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
    public async Task RefreshHistoryAsync_RelationalProvider_ReplacesAtomicallyAndDoesNotRepeatProviderCalls()
    {
        await using var harness = await CreateSqliteHarnessAsync();
        var target = new Stock { Id = 1, Ticker = "AMZN", Exchange = StockExchanges.Frankfurt, Name = "Amazon FRA" };
        var other = new Stock { Id = 2, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        harness.Context.Stocks.AddRange(target, other);
        harness.Context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice { StockId = 1, Interval = "1d", Timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Open = 1, High = 1, Low = 1, Close = 1, QuoteUnitMultiplier = 1m },
            new StockHistoricalPrice { StockId = 2, Interval = "1d", Timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Open = 9, High = 9, Low = 9, Close = 9, QuoteUnitMultiplier = 1m });
        await harness.Context.SaveChangesAsync();

        var handler = new CountingHandler(
            SuccessChartJson(1704067200, 10m),
            SuccessChartJson(1704672000, 20m),
            SuccessChartJson(1705276800, 30m),
            SuccessChartJson(1705881600, 40m),
            SuccessChartJson(1706486400, 50m));
        var service = CreateService(harness.Context, handler);

        var result = await service.RefreshHistoryAsync(target);

        Assert.Equal(5, handler.CallCount);
        Assert.Equal(1, result.DeletedPoints);
        Assert.Equal(5, result.ImportedPoints);

        await using var verificationContext = harness.CreateVerificationContext();
        var targetRows = await verificationContext.StockHistoricalPrices
            .Where(x => x.StockId == 1)
            .OrderBy(x => x.Interval)
            .ThenBy(x => x.Timestamp)
            .ToListAsync();
        Assert.Equal(5, targetRows.Count);
        Assert.Equal(1, await verificationContext.StockHistoricalPrices.CountAsync(x => x.StockId == 2));
    }

    [Fact]
    public async Task RefreshHistoryAsync_ProviderFailurePreservesExistingHistory()
    {
        await using var harness = await CreateSqliteHarnessAsync();
        var stock = new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        harness.Context.Stocks.Add(stock);
        harness.Context.StockHistoricalPrices.Add(new StockHistoricalPrice
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
        await harness.Context.SaveChangesAsync();

        var handler = new FailingHandler(new HttpRequestException("provider failed"));
        var service = CreateService(harness.Context, handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.RefreshHistoryAsync(stock));

        await using var verificationContext = harness.CreateVerificationContext();
        var rows = await verificationContext.StockHistoricalPrices.Where(x => x.StockId == 1).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(7m, rows[0].Close);
    }

    [Fact]
    public async Task RefreshHistoryAsync_RateLimited_PreservesExistingHistoryAndStopsAfterFirstCall()
    {
        await using var context = CreateInMemoryContext();
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

        var handler = new StatusSequenceHandler(HttpStatusCode.TooManyRequests);
        var service = CreateService(context, handler);

        var result = await service.RefreshHistoryAsync(stock);

        Assert.Equal(0, result.DeletedPoints);
        Assert.Equal(0, result.ImportedPoints);
        Assert.Equal(1, handler.CallCount);

        var rows = await context.StockHistoricalPrices.Where(x => x.StockId == 1).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(7m, rows[0].Close);
    }

    [Fact]
    public async Task SyncHistoricalDataForAllStocksAsync_RateLimited_StopsFurtherFanOut()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.AddRange(
            new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" },
            new Stock { Id = 2, Ticker = "MSFT", Exchange = StockExchanges.Nyse, Name = "Microsoft" });
        await context.SaveChangesAsync();

        var handler = new StatusSequenceHandler(HttpStatusCode.TooManyRequests);
        var service = CreateService(context, handler);

        await service.SyncHistoricalDataForAllStocksAsync();

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RefreshHistoryAsync_CancellationAndPerStockDuplicateCallProtectionRemainIntact()
    {
        await using var context = CreateInMemoryContext();
        var stock = new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new BlockingHandler(gate);
        var service = CreateService(context, handler);

        var firstCall = service.RefreshHistoryAsync(stock);
        await handler.WaitForFirstRequestAsync();

        using var cancellationTokenSource = new CancellationTokenSource();
        var secondCall = service.RefreshHistoryAsync(stock, cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => secondCall);

        gate.SetResult();
        await firstCall;
        Assert.Equal(5, handler.CallCount);
    }

    [Fact]
    public async Task GetHistoryAsync_RangeSelection_ContinuesToWork()
    {
        await using var context = CreateInMemoryContext();
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

    [Fact]
    public async Task RefreshHistoryAsync_PersistsYahooVolume_AndReturnsItFromHistory()
    {
        await using var context = CreateInMemoryContext();
        var stock = new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var intradayBase = DateTimeOffset.UtcNow.AddMinutes(-30);
        intradayBase = new DateTimeOffset(
            intradayBase.Year,
            intradayBase.Month,
            intradayBase.Day,
            intradayBase.Hour,
            (intradayBase.Minute / 10) * 10,
            0,
            TimeSpan.Zero);

        var handler = new SequenceHandler(
            SuccessChartJson(1704067200, 10m, 111),
            SuccessChartJson(1704672000, 20m, 222),
            SuccessChartJson(1705276800, 30m, 333),
            SuccessChartJson(1705881600, 40m, 444),
            SuccessChartJson(
                (intradayBase.ToUnixTimeSeconds(), 50m, 40L),
                (intradayBase.AddMinutes(5).ToUnixTimeSeconds(), 55m, 60L)));
        var service = CreateService(context, handler);

        await service.RefreshHistoryAsync(stock);

        var monthlyRow = await context.StockHistoricalPrices.SingleAsync(x => x.StockId == 1 && x.Interval == "1mo");
        var dailyRow = await context.StockHistoricalPrices.SingleAsync(x => x.StockId == 1 && x.Interval == "1d");
        var intradayRow = await context.StockHistoricalPrices.SingleAsync(x => x.StockId == 1 && x.Interval == "10m");

        Assert.Equal(111, monthlyRow.Volume);
        Assert.Equal(333, dailyRow.Volume);
        Assert.Equal(100, intradayRow.Volume);

        var todayHistory = await service.GetHistoryAsync(stock, "today");
        var todayPoint = Assert.Single(todayHistory.Points);
        Assert.Equal(100, todayPoint.Volume);
    }

    [Fact]
    public async Task GetHistoryAsync_ComputesVolumeMetrics_FromSelectedListingHistory()
    {
        await using var context = CreateInMemoryContext();
        var stock = new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        context.Stocks.Add(stock);

        var startDate = DateTime.UtcNow.Date.AddDays(-50);
        for (var i = 0; i < 50; i++)
        {
            context.StockHistoricalPrices.Add(new StockHistoricalPrice
            {
                StockId = 1,
                Interval = "1d",
                Timestamp = startDate.AddDays(i),
                Open = 100m + i,
                High = 100m + i,
                Low = 100m + i,
                Close = 100m + i,
                QuoteCurrency = "USD",
                FinancialCurrency = "USD",
                NormalizedQuoteCurrency = "USD",
                QuoteUnitMultiplier = 1m,
                Volume = i + 1
            });
        }

        await context.SaveChangesAsync();

        var service = CreateService(context, new SequenceHandler());
        var response = await service.GetHistoryAsync(stock, "6m");

        Assert.Equal(40.5m, response.VolumeMetrics.AverageVolume20);
        Assert.Equal(25.5m, response.VolumeMetrics.AverageVolume50);
        Assert.Equal(50m / 40.5m, response.VolumeMetrics.RelativeVolume);
        Assert.Equal(149m * 50m, response.VolumeMetrics.Turnover);
        Assert.Equal("EUR", response.VolumeMetrics.TurnoverCurrency);
        Assert.True(response.VolumeMetrics.UsesCompletedCandle);
        Assert.Equal(startDate.AddDays(49), response.VolumeMetrics.LatestMetricsTimestamp);
    }

    [Fact]
    public async Task GetHistoryAsync_WithInsufficientPeriods_ReturnsNullAverageMetrics()
    {
        await using var context = CreateInMemoryContext();
        var stock = new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        context.Stocks.Add(stock);

        var startDate = DateTime.UtcNow.Date.AddDays(-19);
        for (var i = 0; i < 19; i++)
        {
            context.StockHistoricalPrices.Add(new StockHistoricalPrice
            {
                StockId = 1,
                Interval = "1d",
                Timestamp = startDate.AddDays(i),
                Open = 20m + i,
                High = 20m + i,
                Low = 20m + i,
                Close = 20m + i,
                QuoteCurrency = "USD",
                FinancialCurrency = "USD",
                NormalizedQuoteCurrency = "USD",
                QuoteUnitMultiplier = 1m,
                Volume = 100 + i
            });
        }

        await context.SaveChangesAsync();

        var service = CreateService(context, new SequenceHandler());
        var response = await service.GetHistoryAsync(stock, "6m");

        Assert.Null(response.VolumeMetrics.AverageVolume20);
        Assert.Null(response.VolumeMetrics.AverageVolume50);
        Assert.Null(response.VolumeMetrics.RelativeVolume);
        Assert.Equal(38m * 118m, response.VolumeMetrics.Turnover);
    }

    [Fact]
    public async Task GetHistoryAsync_WithZeroVolumeBaseline_AvoidsZeroAverageAndRelativeVolume()
    {
        await using var context = CreateInMemoryContext();
        var stock = new Stock { Id = 1, Ticker = "AAPL", Exchange = StockExchanges.Nyse, Name = "Apple" };
        context.Stocks.Add(stock);

        var startDate = DateTime.UtcNow.Date.AddDays(-20);
        for (var i = 0; i < 20; i++)
        {
            context.StockHistoricalPrices.Add(new StockHistoricalPrice
            {
                StockId = 1,
                Interval = "1d",
                Timestamp = startDate.AddDays(i),
                Open = 10m,
                High = 10m,
                Low = 10m,
                Close = 10m,
                QuoteCurrency = "USD",
                FinancialCurrency = "USD",
                NormalizedQuoteCurrency = "USD",
                QuoteUnitMultiplier = 1m,
                Volume = 0
            });
        }

        await context.SaveChangesAsync();

        var service = CreateService(context, new SequenceHandler());
        var response = await service.GetHistoryAsync(stock, "6m");

        Assert.Null(response.VolumeMetrics.AverageVolume20);
        Assert.Null(response.VolumeMetrics.AverageVolume50);
        Assert.Null(response.VolumeMetrics.RelativeVolume);
        Assert.Null(response.VolumeMetrics.Turnover);
    }

    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<SqliteHarness> CreateSqliteHarnessAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var context = CreateSqliteContext(connection);
        await context.Database.EnsureCreatedAsync();

        return new SqliteHarness(connection, context);
    }

    private static AppDbContext CreateSqliteContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        return new AppDbContext(options);
    }

    private static StockHistoryService CreateService(AppDbContext context, HttpMessageHandler handler)
    {
        var coordinator = new YahooRequestCoordinator(
            new FixedHttpClientFactory(new HttpClient(handler)),
            NullLogger<YahooRequestCoordinator>.Instance,
            Options.Create(new YahooFinanceOptions
            {
                MinRequestInterval = TimeSpan.Zero,
                CooldownDuration = TimeSpan.FromMinutes(30),
                QuoteCacheDuration = TimeSpan.Zero,
                RequestTimeout = TimeSpan.FromSeconds(10)
            }));
        return new StockHistoryService(
            context,
            coordinator,
            new StubStockQuoteConversionService(),
            NullLogger<StockHistoryService>.Instance);
    }

    private static string SuccessChartJson(long unixTimestamp, decimal close, long volume = 100L) =>
        SuccessChartJson((unixTimestamp, close, volume));

    private static string SuccessChartJson(params (long Timestamp, decimal Close, long Volume)[] candles)
    {
        var timestamps = string.Join(",", candles.Select(x => x.Timestamp));
        var opens = string.Join(",", candles.Select(x => x.Close));
        var highs = string.Join(",", candles.Select(x => x.Close));
        var lows = string.Join(",", candles.Select(x => x.Close));
        var closes = string.Join(",", candles.Select(x => x.Close));
        var volumes = string.Join(",", candles.Select(x => x.Volume));
        return $@"{{""chart"":{{""result"":[{{""meta"":{{""currency"":""USD"",""financialCurrency"":""USD""}},""timestamp"":[{timestamps}],""indicators"":{{""quote"":[{{""open"":[{opens}],""high"":[{highs}],""low"":[{lows}],""close"":[{closes}],""volume"":[{volumes}]}}]}}}}]}}}}";
    }

    private static string EmptyChartJson() => """{"chart":{"result":[]}}""";

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FixedHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private class SequenceHandler : HttpMessageHandler
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

    private sealed class CountingHandler : SequenceHandler
    {
        private int _callCount;

        public CountingHandler(params string[] responses)
            : base(responses)
        {
        }

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class FailingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StatusSequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(EmptyChartJson(), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class BlockingHandler(TaskCompletionSource gate) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => _callCount;

        public Task WaitForFirstRequestAsync() => _firstRequest.Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call == 1)
            {
                _firstRequest.TrySetResult();
                await gate.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessChartJson(1704067200 + call, 10m + call), Encoding.UTF8, "application/json")
            };
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

        public StockQuoteResponse BuildQuoteResponse(string symbol, decimal rawCurrentPrice, decimal rawPreviousClose, decimal percentChange, string marketState, CurrencyConversionContext conversionContext, string priceSession = "REGULAR", DateTime? priceTimestampUtc = null, string? priceSource = null, string? delayWarning = null)
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

    private sealed class SqliteHarness(
        SqliteConnection connection,
        AppDbContext context) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public AppDbContext Context { get; } = context;

        public AppDbContext CreateVerificationContext() =>
            CreateSqliteContext(Connection);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

}
