using System.Security.Claims;
using System.Text.Json;
using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockTechnicalAnalysisScoringTests
{
    [Fact]
    public void HorizonWeights_AreExact_AndSumToOne()
    {
        var expected = new Dictionary<string, (double Trend, double Momentum, double Returns, double Risk, double Fundamentals)>
        {
            ["ThreeMonths"] = (0.35, 0.35, 0.20, 0.10, 0.00),
            ["SixMonths"] = (0.35, 0.25, 0.20, 0.15, 0.05),
            ["OneYear"] = (0.30, 0.15, 0.20, 0.15, 0.20),
            ["TwoYears"] = (0.15, 0.05, 0.15, 0.20, 0.45)
        };

        foreach (var (horizon, weights) in TechnicalAnalysisScoring.HorizonWeights)
        {
            Assert.True(expected.ContainsKey(horizon));
            var e = expected[horizon];
            Assert.Equal(e.Trend, weights.Trend, 10);
            Assert.Equal(e.Momentum, weights.Momentum, 10);
            Assert.Equal(e.Returns, weights.Returns, 10);
            Assert.Equal(e.Risk, weights.Risk, 10);
            Assert.Equal(e.Fundamentals, weights.Fundamentals, 10);

            var sum = weights.Trend + weights.Momentum + weights.Returns + weights.Risk + weights.Fundamentals;
            Assert.Equal(1.0, sum, 10);
        }
    }

    [Theory]
    [InlineData(0.0, "StrongBearish")]
    [InlineData(29.999999, "StrongBearish")]
    [InlineData(30.0, "ModeratelyBearish")]
    [InlineData(44.999999, "ModeratelyBearish")]
    [InlineData(45.0, "Neutral")]
    [InlineData(64.999999, "Neutral")]
    [InlineData(65.0, "ModeratelyBullish")]
    [InlineData(79.999999, "ModeratelyBullish")]
    [InlineData(80.0, "StrongBullish")]
    [InlineData(100.0, "StrongBullish")]
    public void SignalBoundaries_AreStable(double score, string expected)
    {
        Assert.Equal(expected, TechnicalAnalysisScoring.ToSignal(score));
    }

    [Fact]
    public void Compute_ReturnsAllHorizons_WithClampedScoreAndConfidence()
    {
        var candles = BuildSeries(600, 100m, 0.3m, adjustedCoverage: 1.0);
        var input = new TechnicalAnalysisScoring.Input(candles, BuildFreshFundamentals(), null, Utc(2026, 8, 20));

        var result = TechnicalAnalysisScoring.Compute(input);

        Assert.NotNull(result.ThreeMonths);
        Assert.NotNull(result.SixMonths);
        Assert.NotNull(result.OneYear);
        Assert.NotNull(result.TwoYears);

        foreach (var horizon in new[] { result.ThreeMonths, result.SixMonths, result.OneYear, result.TwoYears })
        {
            Assert.InRange(horizon.Score, 0, 100);
            Assert.InRange(horizon.Confidence, 0, 1);
        }
    }

    [Fact]
    public void MonotonicSeries_BullishScoresHigherThanBearishAndFlat()
    {
        var bullish = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(
            BuildSeries(600, 100m, 0.4m, 1.0), BuildFreshFundamentals(), null, Utc(2026, 8, 20)));
        var bearish = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(
            BuildSeries(600, 100m, -0.3m, 1.0), BuildFreshFundamentals(), null, Utc(2026, 8, 20)));
        var flat = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(
            BuildSeries(600, 100m, 0m, 1.0), BuildFreshFundamentals(), null, Utc(2026, 8, 20)));

        Assert.True(bullish.ThreeMonths.Score > flat.ThreeMonths.Score);
        Assert.True(flat.ThreeMonths.Score > bearish.ThreeMonths.Score);
    }

    [Fact]
    public void AdjustedClose_IsPreferred_WithPerPointFallbackToClose()
    {
        var candles = BuildSeries(260, 100m, 0.2m, adjustedCoverage: 1.0).ToList();

        for (var i = 100; i < 120; i++)
        {
            candles[i] = candles[i] with { AdjustedClose = null };
        }

        var splitIndex = 170;
        candles[splitIndex] = candles[splitIndex] with { Close = candles[splitIndex].Close / 2m, AdjustedClose = candles[splitIndex].AdjustedClose };

        var result = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candles, BuildFreshFundamentals(), null, Utc(2026, 8, 20)));

        Assert.True(result.Metrics.AdjustedCloseCoverage < 1.0);
        Assert.Contains(result.Warnings, w => w.Code == "ADJUSTED_CLOSE_INCOMPLETE");
        Assert.NotNull(result.Metrics.Return3Months);
    }

    [Fact]
    public void Atr14_UsesUnadjustedOhlc()
    {
        var candlesA = BuildSeries(80, 100m, 0.15m, adjustedCoverage: 1.0).ToList();
        var candlesB = candlesA.Select(c => c with { AdjustedClose = c.AdjustedClose.HasValue ? c.AdjustedClose * 1.8m : null }).ToList();

        var a = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candlesA, BuildFreshFundamentals(), null, Utc(2026, 8, 20)));
        var b = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candlesB, BuildFreshFundamentals(), null, Utc(2026, 8, 20)));

        Assert.NotNull(a.Metrics.Atr14);
        Assert.NotNull(b.Metrics.Atr14);
        Assert.Equal(a.Metrics.Atr14!.Value, b.Metrics.Atr14!.Value, 8);
    }

    [Fact]
    public void InsufficientHistory_ProducesPartialWarnings_NotFailure()
    {
        var candles = BuildSeries(90, 100m, 0.1m, adjustedCoverage: 0.5);
        var result = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candles, null, null, Utc(2026, 8, 20)));

        Assert.NotNull(result.ThreeMonths);
        Assert.Null(result.Metrics.Sma200);
        Assert.Contains(result.Warnings, w => w.Code == "SMA200_UNAVAILABLE");
        Assert.Contains(result.SixMonths.Warnings, w => w.Code == "HISTORY_INSUFFICIENT");
    }

    [Fact]
    public void StaleLatestCandle_ReducesConfidence_AndAddsWarning()
    {
        var candles = BuildSeries(300, 100m, 0.2m, adjustedCoverage: 1.0, start: Utc(2025, 1, 1));
        var fresh = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candles, BuildFreshFundamentals(), null, Utc(2025, 10, 30)));
        var stale = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candles, BuildFreshFundamentals(), null, Utc(2026, 8, 20)));

        Assert.True(stale.ThreeMonths.Confidence < fresh.ThreeMonths.Confidence);
        Assert.Contains(stale.Warnings, w => w.Code == "HISTORY_STALE");
    }

    [Fact]
    public void MissingOrStaleFundamentals_ReduceLongHorizonConfidence_AndWarn()
    {
        var candles = BuildSeries(600, 100m, 0.2m, adjustedCoverage: 1.0);
        var noFundamentals = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candles, null, null, Utc(2026, 8, 20)));
        var staleFundamentals = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(
            candles,
            BuildFreshFundamentals() with { FetchedAtUtc = Utc(2026, 1, 1) },
            null,
            Utc(2026, 8, 20)));

        Assert.True(noFundamentals.OneYear.Confidence < noFundamentals.ThreeMonths.Confidence);
        Assert.True(staleFundamentals.TwoYears.Confidence < staleFundamentals.ThreeMonths.Confidence);
        Assert.Contains(noFundamentals.OneYear.Warnings, w => w.Code == "FUNDAMENTALS_MISSING");
        Assert.Contains(staleFundamentals.TwoYears.Warnings, w => w.Code == "FUNDAMENTALS_STALE");
    }

    [Fact]
    public void TwoYear_HasExplicitHistoricalFundamentalDataWarning_WhenUnavailable()
    {
        var candles = BuildSeries(600, 100m, 0.2m, adjustedCoverage: 1.0);
        var result = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candles, BuildFreshFundamentals(), null, Utc(2026, 8, 20)));

        Assert.Contains(result.TwoYears.Warnings, w => w.Code == "FUNDAMENTAL_HISTORY_INSUFFICIENT");
    }

    [Fact]
    public void NullZeroConstantDuplicateOutOfOrderCandles_AreHandledDeterministically()
    {
        var candles = BuildSeries(260, 100m, 0m, adjustedCoverage: 1.0).ToList();
        candles.Add(candles[20]); // duplicate timestamp
        candles.Add(candles[30] with { Id = candles[30].Id + 1000, Close = 0m, AdjustedClose = null });
        candles = candles.OrderByDescending(c => c.Timestamp).ToList();

        var a = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candles, BuildFreshFundamentals(), null, Utc(2026, 8, 20)));
        var b = TechnicalAnalysisScoring.Compute(new TechnicalAnalysisScoring.Input(candles, BuildFreshFundamentals(), null, Utc(2026, 8, 20)));

        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        Assert.Contains(a.Warnings, w => w.Code == "DUPLICATE_CANDLES");
        Assert.Contains(a.Warnings, w => w.Code == "CONSTANT_PRICE_SERIES");
    }

    [Fact]
    public async Task ServiceAndEndpoint_NotFound_AndAuthorizationConventions()
    {
        await using var db = await CreateSqliteContextAsync();
        var service = new StockTechnicalAnalysisService(db, TimeProvider.System);

        var response = await service.GetTechnicalAnalysisAsync(999, CancellationToken.None);
        Assert.Null(response);

        var controller = new StocksController(
            db,
            new StubStockHistoryService(),
            new StockQuoteSnapshotPersistenceService(db, TimeProvider.System, NullLogger<StockQuoteSnapshotPersistenceService>.Instance),
            NullLogger<StocksController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "Test"))
            }
        };

        var endpointResult = await controller.GetTechnicalAnalysis(999, service, CancellationToken.None);
        Assert.IsType<NotFoundResult>(endpointResult.Result);

        Assert.NotNull(typeof(StocksController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).FirstOrDefault());
    }

    [Fact]
    public async Task Service_UsesReadOnlyQueries_WithoutDatabaseWrites()
    {
        await using var db = await CreateSqliteContextAsync();

        var stock = new Stock
        {
            Ticker = "TEST",
            Name = "Test",
            CommonName = "Test",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            UpdatedAt = Utc(2026, 8, 20),
            TrackingStatus = StockTrackingStatus.Tracked
        };

        db.Stocks.Add(stock);
        await db.SaveChangesAsync();

        db.StockHistoricalPrices.AddRange(BuildSeries(900, 100m, 0.1m, 1.0).Select(c => new StockHistoricalPrice
        {
            Id = c.Id,
            StockId = stock.Id,
            Timestamp = c.Timestamp,
            Interval = "1d",
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            AdjustedClose = c.AdjustedClose,
            Volume = c.Volume,
            QuoteUnitMultiplier = 1m
        }));
        await db.SaveChangesAsync();

        var beforeWrites = await db.StockHistoricalPrices.CountAsync();

        var service = new StockTechnicalAnalysisService(db, TimeProvider.System);
        var result = await service.GetTechnicalAnalysisAsync(stock.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Metrics.DailyCandleCount <= 800);
        var afterWrites = await db.StockHistoricalPrices.CountAsync();
        Assert.Equal(beforeWrites, afterWrites);
    }

    private static TechnicalAnalysisScoring.FundamentalsSnapshotInput BuildFreshFundamentals()
        => new(
            FetchedAtUtc: Utc(2026, 8, 15),
            AsOfDate: Utc(2026, 8, 1),
            MarketCap: 100_000_000_000m,
            TotalDebt: 20_000_000_000m,
            CashAndEquivalents: 10_000_000_000m,
            EbitdaTtm: 15_000_000_000m,
            NetIncomeTtm: 5_000_000_000m,
            FreeCashFlowTtm: 4_000_000_000m,
            PeRatio: 22m,
            PbRatio: 4m,
            DividendYield: 1.8m);

    private static List<TechnicalAnalysisScoring.RawDailyCandle> BuildSeries(
        int count,
        decimal startPrice,
        decimal driftPerDay,
        double adjustedCoverage,
        DateTime? start = null)
    {
        var startDate = start ?? Utc(2024, 1, 1);
        var candles = new List<TechnicalAnalysisScoring.RawDailyCandle>(count);
        var price = startPrice;

        for (var i = 0; i < count; i++)
        {
            var date = startDate.AddDays(i);
            var open = price;
            var close = Math.Max(1m, open + driftPerDay);
            var high = Math.Max(open, close) + 1m;
            var low = Math.Min(open, close) - 1m;
            var useAdjusted = i < count * adjustedCoverage;
            decimal? adjustedClose = useAdjusted ? close : null;
            candles.Add(new TechnicalAnalysisScoring.RawDailyCandle(i + 1, date, open, high, low, close, adjustedClose, 1_000_000));
            price = close;
        }

        return candles;
    }

    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<AppDbContext> CreateSqliteContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed class StubStockHistoryService : IStockHistoryService
    {
        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());

        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryRefreshResponse());

        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, StockHistoryRefreshTrigger trigger, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryRefreshResponse());
    }
}
