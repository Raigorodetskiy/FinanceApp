using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockPerformanceParityTests
{
    [Theory]
    [InlineData("today")]
    [InlineData("24h")]
    [InlineData("1w")]
    [InlineData("1m")]
    [InlineData("3m")]
    [InlineData("6m")]
    [InlineData("1y")]
    [InlineData("3y")]
    [InlineData("5y")]
    public async Task CatalogAndIndexPerformance_AreIdentical_ForSameStockAcrossAllRanges(string range)
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        context.MarketIndices.Add(new MarketIndex
        {
            Id = 9001,
            Name = "Parity",
            NormalizedName = "PARITY",
            Code = "PARITY",
            NormalizedCode = "PARITY",
            CreatedAt = now,
            UpdatedAt = now
        });

        context.Stocks.Add(new Stock
        {
            Id = 9002,
            Ticker = "PAR",
            Name = "Parity Stock",
            CommonName = "Parity Stock",
            Exchange = StockExchanges.Frankfurt,
            CurrentPrice = 150m,
            CurrentPriceAt = now.AddMinutes(-5),
            TrackingStatus = StockTrackingStatus.CatalogOnly,
            UpdatedAt = now
        });

        context.StockMarketIndices.Add(new StockMarketIndex
        {
            StockId = 9002,
            MarketIndexId = 9001,
            EffectiveFrom = now
        });

        context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice { StockId = 9002, Interval = "10m", Timestamp = now.AddHours(-30), Close = 90m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "10m", Timestamp = now.AddHours(-2), Close = 100m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "1h", Timestamp = now.AddDays(-8), Close = 80m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "1h", Timestamp = now.AddHours(-3), Close = 120m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "1d", Timestamp = now.AddDays(-45), Close = 70m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "1d", Timestamp = now.AddDays(-2), Close = 110m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "1wk", Timestamp = now.AddDays(-420), Close = 60m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "1wk", Timestamp = now.AddDays(-14), Close = 100m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "1mo", Timestamp = now.AddYears(-6), Close = 50m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
            new StockHistoricalPrice { StockId = 9002, Interval = "1mo", Timestamp = now.AddDays(-40), Close = 95m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" });
        await context.SaveChangesAsync();

        var stocksController = CreateStocksController(context);
        var marketIndicesController = CreateMarketIndicesController(context);

        var catalogResult = await stocksController.GetCatalogPerformance(range);
        var indexResult = await marketIndicesController.GetConstituentPerformance(9001, range);

        var catalogItem = Assert.Single(Assert.IsType<StockCatalogPerformanceResponse>(Assert.IsType<OkObjectResult>(catalogResult.Result).Value).Items);
        var indexItem = Assert.Single(Assert.IsType<IndexConstituentPerformanceResponse>(Assert.IsType<OkObjectResult>(indexResult.Result).Value).Items);

        Assert.Equal(indexItem.StartPrice, catalogItem.StartPrice);
        Assert.Equal(indexItem.EndPrice, catalogItem.EndPrice);
        Assert.Equal(indexItem.ChangePercent, catalogItem.ChangePercent);
        Assert.Equal(indexItem.StartAtUtc, catalogItem.StartAtUtc);
        Assert.Equal(indexItem.EndAtUtc, catalogItem.EndAtUtc);
        Assert.Equal(indexItem.DataStatus, catalogItem.DataStatus);
    }

    [Fact]
    public async Task CatalogAndIndexPerformance_InvalidCurrentQuote_FallBackToHistoryIdentically()
    {
        foreach (var scenario in new[]
                 {
                     new { Id = 9101, Price = 200m, CurrentAt = (DateTime?)DateTime.UtcNow.AddHours(-25) },
                     new { Id = 9102, Price = 200m, CurrentAt = (DateTime?)DateTime.UtcNow.AddHours(1) },
                     new { Id = 9103, Price = 0m, CurrentAt = (DateTime?)DateTime.UtcNow.AddMinutes(-10) },
                     new { Id = 9104, Price = 200m, CurrentAt = (DateTime?)null }
                 })
        {
            await using var context = CreateContext();
            var now = DateTime.UtcNow;
            context.MarketIndices.Add(new MarketIndex
            {
                Id = 9100,
                Name = "Invalid quote",
                NormalizedName = "INVALID QUOTE",
                Code = "INVQ",
                NormalizedCode = "INVQ",
                CreatedAt = now,
                UpdatedAt = now
            });
            context.Stocks.Add(new Stock
            {
                Id = scenario.Id,
                Ticker = $"S{scenario.Id}",
                Name = "Scenario",
                CommonName = "Scenario",
                Exchange = StockExchanges.Frankfurt,
                CurrentPrice = scenario.Price,
                CurrentPriceAt = scenario.CurrentAt,
                TrackingStatus = StockTrackingStatus.CatalogOnly,
                UpdatedAt = now
            });
            context.StockMarketIndices.Add(new StockMarketIndex
            {
                StockId = scenario.Id,
                MarketIndexId = 9100,
                EffectiveFrom = now
            });
            context.StockHistoricalPrices.AddRange(
                new StockHistoricalPrice { StockId = scenario.Id, Interval = "1h", Timestamp = now.AddDays(-8), Close = 100m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" },
                new StockHistoricalPrice { StockId = scenario.Id, Interval = "1h", Timestamp = now.AddHours(-1), Close = 110m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "EUR" });
            await context.SaveChangesAsync();

            var catalogResult = await CreateStocksController(context).GetCatalogPerformance("1w");
            var indexResult = await CreateMarketIndicesController(context).GetConstituentPerformance(9100, "1w");

            var catalogItem = Assert.Single(Assert.IsType<StockCatalogPerformanceResponse>(Assert.IsType<OkObjectResult>(catalogResult.Result).Value).Items);
            var indexItem = Assert.Single(Assert.IsType<IndexConstituentPerformanceResponse>(Assert.IsType<OkObjectResult>(indexResult.Result).Value).Items);

            Assert.Equal(110m, catalogItem.EndPrice);
            Assert.Equal(indexItem.EndPrice, catalogItem.EndPrice);
            Assert.Equal(indexItem.ChangePercent, catalogItem.ChangePercent);
            Assert.Equal(indexItem.DataStatus, catalogItem.DataStatus);
        }
    }

    [Fact]
    public async Task CatalogAndIndexPerformance_IncompatibleCurrency_IgnoreCurrentEndpointIdentically()
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;
        context.MarketIndices.Add(new MarketIndex
        {
            Id = 9200,
            Name = "Currency",
            NormalizedName = "CURRENCY",
            Code = "CURR",
            NormalizedCode = "CURR",
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Stocks.Add(new Stock
        {
            Id = 9201,
            Ticker = "USDX",
            Name = "USD X",
            CommonName = "USD X",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 500m,
            CurrentPriceAt = now.AddMinutes(-5),
            TrackingStatus = StockTrackingStatus.CatalogOnly,
            UpdatedAt = now
        });
        context.StockMarketIndices.Add(new StockMarketIndex
        {
            StockId = 9201,
            MarketIndexId = 9200,
            EffectiveFrom = now
        });
        context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice { StockId = 9201, Interval = "1h", Timestamp = now.AddDays(-8), Close = 100m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "USD" },
            new StockHistoricalPrice { StockId = 9201, Interval = "1h", Timestamp = now.AddHours(-1), Close = 110m, QuoteUnitMultiplier = 1m, NormalizedQuoteCurrency = "USD" });
        await context.SaveChangesAsync();

        var catalogResult = await CreateStocksController(context).GetCatalogPerformance("1w");
        var indexResult = await CreateMarketIndicesController(context).GetConstituentPerformance(9200, "1w");

        var catalogItem = Assert.Single(Assert.IsType<StockCatalogPerformanceResponse>(Assert.IsType<OkObjectResult>(catalogResult.Result).Value).Items);
        var indexItem = Assert.Single(Assert.IsType<IndexConstituentPerformanceResponse>(Assert.IsType<OkObjectResult>(indexResult.Result).Value).Items);

        Assert.Equal(110m, catalogItem.EndPrice);
        Assert.Equal(indexItem.EndPrice, catalogItem.EndPrice);
    }

    [Fact]
    public async Task SharedService_TodayBoundary_UsesBusinessTimezoneMidnight_NotUtcMidnight()
    {
        await using var context = CreateContext();
        var fixedNowUtc = new DateTimeOffset(2026, 8, 21, 0, 30, 0, TimeSpan.Zero);
        var service = new StockPerformanceCalculationService(context, new FixedTimeProvider(fixedNowUtc));

        context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice
            {
                StockId = 9301,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 20, 22, 30, 0, DateTimeKind.Utc),
                Close = 100m,
                QuoteUnitMultiplier = 1m,
                NormalizedQuoteCurrency = "EUR"
            },
            new StockHistoricalPrice
            {
                StockId = 9301,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 20, 23, 30, 0, DateTimeKind.Utc),
                Close = 110m,
                QuoteUnitMultiplier = 1m,
                NormalizedQuoteCurrency = "EUR"
            },
            new StockHistoricalPrice
            {
                StockId = 9301,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 21, 0, 20, 0, DateTimeKind.Utc),
                Close = 120m,
                QuoteUnitMultiplier = 1m,
                NormalizedQuoteCurrency = "EUR"
            });
        await context.SaveChangesAsync();

        var item = Assert.Single(await service.CalculateAsync(
            [new StockPerformanceSubject(9301, "UNKNOWN", 0m, null, null, null)],
            "today"));

        Assert.Equal(new DateTime(2026, 8, 20, 22, 30, 0, DateTimeKind.Utc), item.StartAtUtc);
    }

    [Fact]
    public async Task SharedService_Today_UsesCompatibleCurrentQuoteAsEndpoint_WhenNewerThanHistory()
    {
        await using var context = CreateContext();
        var nowUtc = new DateTimeOffset(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);
        var service = new StockPerformanceCalculationService(context, new FixedTimeProvider(nowUtc));

        context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice
            {
                StockId = 9401,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 21, 13, 20, 0, DateTimeKind.Utc),
                Close = 100m,
                QuoteUnitMultiplier = 1m,
                NormalizedQuoteCurrency = "EUR"
            },
            new StockHistoricalPrice
            {
                StockId = 9401,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 21, 13, 50, 0, DateTimeKind.Utc),
                Close = 99m,
                QuoteUnitMultiplier = 1m,
                NormalizedQuoteCurrency = "EUR"
            });
        await context.SaveChangesAsync();

        var item = Assert.Single(await service.CalculateAsync(
            [new StockPerformanceSubject(9401, StockExchanges.Nyse, 103m, -0.78m, -0.78m, new DateTime(2026, 8, 21, 13, 55, 0, DateTimeKind.Utc))],
            "today"));

        Assert.Equal(ConstituentPerformanceDataStatus.Available, item.DataStatus);
        Assert.Equal(100m, item.StartPrice);
        Assert.Equal(103m, item.EndPrice);
        Assert.Equal(3d, item.ChangePercent!.Value, 6);
        Assert.Equal(new DateTime(2026, 8, 21, 13, 20, 0, DateTimeKind.Utc), item.StartAtUtc);
        Assert.Equal(new DateTime(2026, 8, 21, 13, 55, 0, DateTimeKind.Utc), item.EndAtUtc);
    }

    [Fact]
    public async Task SharedService_Today_SparseHistoryDoesNotUseDailyChangeFallback()
    {
        await using var context = CreateContext();
        var nowUtc = new DateTimeOffset(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);
        var service = new StockPerformanceCalculationService(context, new FixedTimeProvider(nowUtc));

        context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice
            {
                StockId = 9402,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 21, 13, 20, 0, DateTimeKind.Utc),
                Close = 100m,
                QuoteUnitMultiplier = 1m,
                NormalizedQuoteCurrency = "EUR"
            },
            new StockHistoricalPrice
            {
                StockId = 9402,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 21, 13, 40, 0, DateTimeKind.Utc),
                Close = 101m,
                QuoteUnitMultiplier = 1m,
                NormalizedQuoteCurrency = "EUR"
            },
            new StockHistoricalPrice
            {
                StockId = 9403,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 21, 13, 50, 0, DateTimeKind.Utc),
                Close = 200m,
                QuoteUnitMultiplier = 1m,
                NormalizedQuoteCurrency = "EUR"
            });
        await context.SaveChangesAsync();

        var items = await service.CalculateAsync(
            [
                new StockPerformanceSubject(9402, StockExchanges.Nyse, 0m, null, null, null),
                new StockPerformanceSubject(9403, StockExchanges.Nyse, 210m, 10m, 5m, new DateTime(2026, 8, 21, 13, 50, 0, DateTimeKind.Utc))
            ],
            "today");

        var available = Assert.Single(items, x => x.StockId == 9402);
        Assert.Equal(ConstituentPerformanceDataStatus.Available, available.DataStatus);
        Assert.Equal(1d, available.ChangePercent!.Value, 6);

        var sparse = Assert.Single(items, x => x.StockId == 9403);
        Assert.Equal(ConstituentPerformanceDataStatus.InsufficientData, sparse.DataStatus);
        Assert.Null(sparse.ChangePercent);
    }

    [Theory]
    [InlineData(StockExchanges.Frankfurt, 2026, 8, 21, 6, 30, 0, 2026, 8, 20, 15, 20, 0)] // before Frankfurt open -> previous close baseline
    [InlineData(StockExchanges.Nyse, 2026, 8, 21, 12, 0, 0, 2026, 8, 20, 20, 0, 0)] // before NYSE open -> previous close baseline
    [InlineData(StockExchanges.Nasdaq, 2026, 8, 22, 12, 0, 0, 2026, 8, 21, 20, 0, 0)] // weekend -> previous Friday close baseline
    [InlineData(StockExchanges.Frankfurt, 2026, 12, 25, 10, 0, 0, 2026, 12, 24, 16, 20, 0)] // Xetra holiday -> previous trading day close baseline
    public async Task SharedService_TodayBoundary_UsesExchangeSessionsIncludingWeekendsAndHolidays(
        string exchange,
        int nowYear,
        int nowMonth,
        int nowDay,
        int nowHour,
        int nowMinute,
        int nowSecond,
        int expectedStartYear,
        int expectedStartMonth,
        int expectedStartDay,
        int expectedStartHour,
        int expectedStartMinute,
        int expectedStartSecond)
    {
        await using var context = CreateContext();
        var nowUtc = new DateTimeOffset(nowYear, nowMonth, nowDay, nowHour, nowMinute, nowSecond, TimeSpan.Zero);
        var expectedStartAtUtc = new DateTime(expectedStartYear, expectedStartMonth, expectedStartDay, expectedStartHour, expectedStartMinute, expectedStartSecond, DateTimeKind.Utc);
        var service = new StockPerformanceCalculationService(context, new FixedTimeProvider(nowUtc));

        context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = 9501,
            Interval = "10m",
            Timestamp = expectedStartAtUtc,
            Close = 100m,
            QuoteUnitMultiplier = 1m,
            NormalizedQuoteCurrency = "EUR"
        });
        await context.SaveChangesAsync();

        var item = Assert.Single(await service.CalculateAsync(
            [new StockPerformanceSubject(9501, exchange, 0m, null, null, null)],
            "today"));

        Assert.Equal(ConstituentPerformanceDataStatus.InsufficientData, item.DataStatus);
        Assert.Equal(expectedStartAtUtc, item.StartAtUtc);
        Assert.Equal(expectedStartAtUtc, item.EndAtUtc);
    }

    [Fact]
    public async Task SharedService_Today_AllowsLegacyEurMetadataCompatibility_ForCurrentEndpoint()
    {
        await using var context = CreateContext();
        var nowUtc = new DateTimeOffset(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);
        var service = new StockPerformanceCalculationService(context, new FixedTimeProvider(nowUtc));

        context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice
            {
                StockId = 9601,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 21, 13, 20, 0, DateTimeKind.Utc),
                Close = 100m,
                QuoteUnitMultiplier = 1m,
                QuoteCurrency = "EUR",
                FinancialCurrency = null,
                NormalizedQuoteCurrency = null
            },
            new StockHistoricalPrice
            {
                StockId = 9601,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 21, 13, 40, 0, DateTimeKind.Utc),
                Close = 101m,
                QuoteUnitMultiplier = 1m,
                QuoteCurrency = "EUR",
                FinancialCurrency = null,
                NormalizedQuoteCurrency = null
            });
        await context.SaveChangesAsync();

        var item = Assert.Single(await service.CalculateAsync(
            [new StockPerformanceSubject(9601, StockExchanges.Nyse, 102m, null, null, new DateTime(2026, 8, 21, 13, 55, 0, DateTimeKind.Utc))],
            "today"));

        Assert.Equal(ConstituentPerformanceDataStatus.Available, item.DataStatus);
        Assert.Equal(102m, item.EndPrice);
        Assert.Equal(new DateTime(2026, 8, 21, 13, 55, 0, DateTimeKind.Utc), item.EndAtUtc);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static StocksController CreateStocksController(AppDbContext context)
    {
        return new StocksController(
            context,
            new NullStockHistoryService(),
            new StockPerformanceCalculationService(context, TimeProvider.System),
            new StockQuoteSnapshotPersistenceService(context, TimeProvider.System, NullLogger<StockQuoteSnapshotPersistenceService>.Instance),
            NullLogger<StocksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static MarketIndicesController CreateMarketIndicesController(AppDbContext context)
    {
        return new MarketIndicesController(
            context,
            new NullMarketIndexHistoryService(),
            new NullIndexConstituentsProvider(),
            new NullStockHistoryService(),
            new StockPerformanceCalculationService(context, TimeProvider.System),
            new NullIndexConstituentHistoryRefreshJobService(),
            new NullIndexConstituentsBatchQuoteRefreshJobService(),
            NullLogger<MarketIndicesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class NullMarketIndexHistoryService : IMarketIndexHistoryService
    {
        public Task<MarketIndexHistoryResponse> GetHistoryAsync(MarketIndex index, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketIndexHistoryResponse());

        public Task<MarketIndexRefreshResponse> RefreshHistoryAsync(MarketIndex index, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketIndexRefreshResponse());
    }

    private sealed class NullIndexConstituentsProvider : IIndexConstituentsProvider
    {
        public string ProviderName => "Null";
        public Task<IndexConstituentsResult> GetConstituentsAsync(MarketIndex index, CancellationToken cancellationToken = default)
            => Task.FromResult(IndexConstituentsResult.Unsupported(ProviderName));
    }

    private sealed class NullStockHistoryService : IStockHistoryService
    {
        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default) => Task.FromResult(new StockHistoryResponse());
        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default) => Task.FromResult(new StockHistoryRefreshResponse());
        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, StockHistoryRefreshTrigger trigger, CancellationToken cancellationToken = default) => Task.FromResult(new StockHistoryRefreshResponse());
    }

    private sealed class NullIndexConstituentHistoryRefreshJobService : IIndexConstituentHistoryRefreshJobService
    {
        public IndexConstituentHistoryRefreshJobEnqueueResult Enqueue(int marketIndexId, int stockId) => new() { Status = IndexConstituentHistoryRefreshJobEnqueueStatus.QueueFull };
        public bool TryGetJob(int marketIndexId, int stockId, string jobId, out IndexConstituentHistoryRefreshJobResponse? job)
        {
            job = null;
            return false;
        }
    }

    private sealed class NullIndexConstituentsBatchQuoteRefreshJobService : IIndexConstituentsBatchQuoteRefreshJobService
    {
        public IndexConstituentsBatchQuoteRefreshJobEnqueueResult Enqueue(int marketIndexId) => new() { Status = IndexConstituentsBatchQuoteRefreshJobEnqueueStatus.QueueFull };
        public bool TryGetJob(int marketIndexId, string jobId, out IndexConstituentsBatchQuoteRefreshJobResponse? job)
        {
            job = null;
            return false;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
