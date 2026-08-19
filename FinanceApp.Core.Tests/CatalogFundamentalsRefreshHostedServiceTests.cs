using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class CatalogFundamentalsRefreshHostedServiceTests
{
    [Fact]
    public void WeeklyScheduleCalculator_BeforeAndAfterSunday_ComputesNextRun()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var before = new DateTimeOffset(2026, 8, 22, 23, 0, 0, TimeSpan.Zero); // Sunday 01:00 local
        var after = new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero);   // Sunday 03:00 local

        var beforeSnapshot = CatalogStockRefreshScheduleCalculator.WeeklySnapshot(before, tz, DayOfWeek.Sunday, new TimeSpan(2, 30, 0));
        var afterSnapshot = CatalogStockRefreshScheduleCalculator.WeeklySnapshot(after, tz, DayOfWeek.Sunday, new TimeSpan(2, 30, 0));

        Assert.Equal(new DateTimeOffset(2026, 8, 23, 0, 30, 0, TimeSpan.Zero), beforeSnapshot.CurrentWeekScheduledRunUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 0, 30, 0, TimeSpan.Zero), afterSnapshot.NextScheduledRunUtc);
    }

    [Fact]
    public void WeeklyScheduleCalculator_EuropeBerlin_DstInvalidAndAmbiguous_AreDeterministic()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var springForwardDay = DateOnly.FromDateTime(new DateTime(2026, 3, 29));
        var fallBackDay = DateOnly.FromDateTime(new DateTime(2026, 10, 25));

        var springUtc = CatalogStockRefreshScheduleCalculator.ToUtc(springForwardDay, new TimeSpan(2, 30, 0), tz);
        var fallUtc = CatalogStockRefreshScheduleCalculator.ToUtc(fallBackDay, new TimeSpan(2, 30, 0), tz);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), springUtc);   // 03:00 local
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), fallUtc);    // first 02:30 local occurrence
    }

    [Fact]
    public async Task Run_ProcessesTrackedAndCatalogOnly_ExactlyOnce_For600Plus()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero));
        var fundamentals = new RecordingFundamentalsService();
        await using var harness = await Harness.CreateAsync(clock, fundamentals, new CatalogFundamentalsRefreshJobOptions
        {
            BatchSize = 37,
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0,
            FreshnessThreshold = TimeSpan.FromDays(7)
        });

        await harness.SeedManyStocksAsync(620);
        var businessWeek = DateOnly.FromDateTime(new DateTime(2026, 8, 23));
        await harness.FundamentalsService.TriggerRunAsync(businessWeek, clock.GetUtcNow().UtcDateTime, "test");

        await using var scope = harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.CatalogFundamentalsRefreshRuns.SingleAsync();
        Assert.Equal(CatalogFundamentalsRefreshRunStatus.Completed, run.Status);
        Assert.Equal(620, run.TotalDiscovered);
        Assert.Equal(620, run.Processed);
        Assert.Equal(620, run.Succeeded);
        Assert.Equal(620, fundamentals.Calls.Count);
        Assert.Equal(620, fundamentals.Calls.Distinct().Count());
    }

    [Fact]
    public async Task Run_DoesNotDuplicate_WhenSameBusinessWeekAlreadyCompleted()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero));
        var fundamentals = new RecordingFundamentalsService();
        await using var harness = await Harness.CreateAsync(clock, fundamentals);
        await harness.SeedStockAsync(1, "AAPL", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        var businessWeek = DateOnly.FromDateTime(new DateTime(2026, 8, 23));

        await harness.FundamentalsService.TriggerRunAsync(businessWeek, clock.GetUtcNow().UtcDateTime, "first");
        var callsAfterFirst = fundamentals.Calls.Count;
        await harness.FundamentalsService.TriggerRunAsync(businessWeek, clock.GetUtcNow().UtcDateTime, "second");

        Assert.Equal(callsAfterFirst, fundamentals.Calls.Count);
    }

    [Fact]
    public async Task Run_FreshFundamentals_AreSkippedWithoutProviderCall()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero));
        var fundamentals = new RecordingFundamentalsService();
        await using var harness = await Harness.CreateAsync(clock, fundamentals, new CatalogFundamentalsRefreshJobOptions
        {
            FreshnessThreshold = TimeSpan.FromDays(7),
            RetryLimit = 0,
            InterRequestDelay = TimeSpan.Zero
        });

        await harness.SeedStockAsync(1, "AAPL", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.SeedSnapshotAsync(1, clock.GetUtcNow().UtcDateTime.AddDays(-1));
        await harness.FundamentalsService.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 23)), clock.GetUtcNow().UtcDateTime, "test");

        await using var scope = harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.CatalogFundamentalsRefreshRuns.SingleAsync();
        Assert.Equal(1, run.Skipped);
        Assert.Empty(fundamentals.Calls);
    }

    [Fact]
    public async Task Run_InvalidMetadata_IsSkipped_AndFollowingStocksContinue()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero));
        var fundamentals = new RecordingFundamentalsService();
        await using var harness = await Harness.CreateAsync(clock, fundamentals, new CatalogFundamentalsRefreshJobOptions
        {
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0
        });

        await harness.SeedStockAsync(1, string.Empty, StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.SeedStockAsync(2, "BAD", "UNKNOWN", StockTrackingStatus.CatalogOnly);
        await harness.SeedStockAsync(3, "GOOD", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.FundamentalsService.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 23)), clock.GetUtcNow().UtcDateTime, "test");

        await using var scope = harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.CatalogFundamentalsRefreshRuns.SingleAsync();

        Assert.Equal(3, run.Processed);
        Assert.Equal(2, run.Skipped);
        Assert.Equal(1, run.Succeeded);
        Assert.Single(fundamentals.Calls);
        Assert.Equal(3, fundamentals.Calls[0]);
    }

    [Fact]
    public async Task Run_RateLimited_PausesAndKeepsSnapshotData()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero));
        var fundamentals = new RecordingFundamentalsService
        {
            Behavior = id => id == 1
                ? FundamentalsResult.FromSnapshot(
                    new CompanyFundamentalsSnapshot
                    {
                        StockId = 1,
                        SourceSymbol = "AAPL",
                        MarketCap = 100m,
                        Currency = "USD",
                        Source = "Yahoo Finance",
                        AsOfDate = clock.GetUtcNow().UtcDateTime.Date,
                        FetchedAtUtc = clock.GetUtcNow().UtcDateTime.AddDays(-2)
                    },
                    FundamentalsState.Stale,
                    "rate limit")
                with { FailureCategory = FundamentalsRefreshFailureCategory.ProviderRateLimited }
                : FundamentalsResult.FromSnapshot(
                    new CompanyFundamentalsSnapshot
                    {
                        StockId = id,
                        SourceSymbol = $"S{id}",
                        MarketCap = 200m,
                        Currency = "USD",
                        Source = "Yahoo Finance",
                        AsOfDate = clock.GetUtcNow().UtcDateTime.Date,
                        FetchedAtUtc = clock.GetUtcNow().UtcDateTime
                    },
                    FundamentalsState.Fresh)
        };

        await using var harness = await Harness.CreateAsync(clock, fundamentals, new CatalogFundamentalsRefreshJobOptions
        {
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0,
            ProviderRateLimitCooldown = TimeSpan.FromMilliseconds(1)
        });
        await harness.SeedStockAsync(1, "AAPL", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.SeedSnapshotAsync(1, clock.GetUtcNow().UtcDateTime.AddDays(-10), marketCap: 123m);
        await harness.FundamentalsService.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 23)), clock.GetUtcNow().UtcDateTime, "test");

        await using var scope = harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.CatalogFundamentalsRefreshRuns.SingleAsync();
        var snapshot = await db.FundamentalsSnapshots.SingleAsync(x => x.StockId == 1);
        Assert.Equal(1, run.RateLimited);
        Assert.Equal(1, run.Skipped);
        Assert.Equal(123m, snapshot.MarketCap);
    }

    [Fact]
    public async Task Run_RecoversExpiredLease()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero));
        var fundamentals = new RecordingFundamentalsService();
        await using var harness = await Harness.CreateAsync(clock, fundamentals, new CatalogFundamentalsRefreshJobOptions
        {
            RetryLimit = 0,
            InterRequestDelay = TimeSpan.Zero
        });
        await harness.SeedStockAsync(1, "SAP", StockExchanges.Frankfurt, StockTrackingStatus.CatalogOnly);

        await using (var scope = harness.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CatalogFundamentalsRefreshRuns.Add(new CatalogFundamentalsRefreshRun
            {
                RunKey = "Europe/Berlin:2026-08-23",
                BusinessWeek = DateOnly.FromDateTime(new DateTime(2026, 8, 23)),
                TimeZoneId = "Europe/Berlin",
                ScheduledAtUtc = clock.GetUtcNow().UtcDateTime,
                Status = CatalogFundamentalsRefreshRunStatus.Running,
                LeaseOwner = "other",
                LeaseExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-5),
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
                UpdatedAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-10)
            });
            await db.SaveChangesAsync();
        }

        await harness.FundamentalsService.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 23)), clock.GetUtcNow().UtcDateTime, "resume");

        await using var verify = harness.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await verifyDb.CatalogFundamentalsRefreshRuns.SingleAsync();
        Assert.Equal(CatalogFundamentalsRefreshRunStatus.Completed, run.Status);
        Assert.Equal(1, run.Processed);
    }

    [Fact]
    public async Task SharedLease_WithNightlyRefresh_PreventsOverlap()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero));
        var quoteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var quote = new BlockingQuoteService(quoteGate.Task);
        var history = new RecordingHistoryService();
        var fundamentals = new RecordingFundamentalsService();
        await using var harness = await Harness.CreateSharedAsync(clock, quote, history, fundamentals);
        await harness.SeedStockAsync(1, "AAPL", StockExchanges.Nyse, StockTrackingStatus.Tracked);

        var nightlyTask = harness.CatalogService!.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 23)), clock.GetUtcNow().UtcDateTime, "nightly");
        await Task.Delay(25);

        var weeklyTask = harness.FundamentalsService.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 23)), clock.GetUtcNow().UtcDateTime, "weekly");
        await Task.Delay(25);
        Assert.Empty(fundamentals.Calls);

        quoteGate.SetResult();
        await nightlyTask;
        await weeklyTask;
        Assert.Single(fundamentals.Calls);
    }

    [Fact]
    public void StatusController_UsesAuthorizeConvention()
    {
        var attribute = typeof(CatalogFundamentalsRefreshController).GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault();
        Assert.NotNull(attribute);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            ServiceProvider services,
            CatalogFundamentalsRefreshHostedService service,
            FixedTimeProvider clock,
            CatalogFundamentalsRefreshJobOptions options,
            CatalogStockRefreshHostedService? catalogService = null)
        {
            Services = services;
            FundamentalsService = service;
            Clock = clock;
            JobOptions = options;
            CatalogService = catalogService;
        }

        public ServiceProvider Services { get; }
        public CatalogFundamentalsRefreshHostedService FundamentalsService { get; }
        public CatalogStockRefreshHostedService? CatalogService { get; }
        public FixedTimeProvider Clock { get; }
        public CatalogFundamentalsRefreshJobOptions JobOptions { get; }

        public static async Task<Harness> CreateAsync(
            FixedTimeProvider clock,
            RecordingFundamentalsService fundamentalsService,
            CatalogFundamentalsRefreshJobOptions? options = null)
        {
            var opts = options ?? new CatalogFundamentalsRefreshJobOptions();
            var services = new ServiceCollection();
            var dbName = $"catalog-fundamentals-tests-{Guid.NewGuid():N}";
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddDbContext<AppDbContext>(b => b.UseInMemoryDatabase(dbName));
            services.AddScoped<IFundamentalsService>(_ => fundamentalsService);

            var provider = services.BuildServiceProvider();
            await using var setup = provider.CreateAsyncScope();
            await setup.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

            var leaseService = new CatalogMaintenanceLeaseService(provider.GetRequiredService<IServiceScopeFactory>(), clock);
            var service = new CatalogFundamentalsRefreshHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                clock,
                Options.Create(opts),
                NullLogger<CatalogFundamentalsRefreshHostedService>.Instance,
                leaseService,
                (_, _) => Task.CompletedTask);

            return new Harness(provider, service, clock, opts);
        }

        public static async Task<Harness> CreateSharedAsync(
            FixedTimeProvider clock,
            IStockQuoteFetchService quoteService,
            IStockHistoryService historyService,
            RecordingFundamentalsService fundamentalsService)
        {
            var services = new ServiceCollection();
            var dbName = $"catalog-shared-lease-tests-{Guid.NewGuid():N}";
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddDbContext<AppDbContext>(b => b.UseInMemoryDatabase(dbName));
            services.AddScoped<IStockQuoteFetchService>(_ => quoteService);
            services.AddScoped<IStockHistoryService>(_ => historyService);
            services.AddScoped<IFundamentalsService>(_ => fundamentalsService);

            var provider = services.BuildServiceProvider();
            await using var setup = provider.CreateAsyncScope();
            await setup.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            var leaseService = new CatalogMaintenanceLeaseService(scopeFactory, clock);
            var catalog = new CatalogStockRefreshHostedService(
                scopeFactory,
                clock,
                Options.Create(new CatalogStockRefreshJobOptions
                {
                    RetryLimit = 0,
                    InterRequestDelay = TimeSpan.Zero,
                    RateLimitCooldown = TimeSpan.FromMilliseconds(1)
                }),
                NullLogger<CatalogStockRefreshHostedService>.Instance,
                leaseService,
                (_, _) => Task.CompletedTask);
            var fundamentals = new CatalogFundamentalsRefreshHostedService(
                scopeFactory,
                clock,
                Options.Create(new CatalogFundamentalsRefreshJobOptions
                {
                    RetryLimit = 0,
                    InterRequestDelay = TimeSpan.Zero,
                    ProviderRateLimitCooldown = TimeSpan.FromMilliseconds(1)
                }),
                NullLogger<CatalogFundamentalsRefreshHostedService>.Instance,
                leaseService,
                (_, _) => Task.Delay(1));
            return new Harness(provider, fundamentals, clock, new CatalogFundamentalsRefreshJobOptions(), catalog);
        }

        public async Task SeedManyStocksAsync(int count)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = Clock.GetUtcNow().UtcDateTime;
            for (var i = 1; i <= count; i++)
            {
                db.Stocks.Add(new Stock
                {
                    Id = i,
                    Ticker = $"T{i}",
                    Name = $"Stock {i}",
                    CommonName = $"Stock {i}",
                    Exchange = StockExchanges.Nyse,
                    TrackingStatus = i % 2 == 0 ? StockTrackingStatus.Tracked : StockTrackingStatus.CatalogOnly,
                    UpdatedAt = now
                });
            }

            await db.SaveChangesAsync();
        }

        public async Task SeedStockAsync(int stockId, string ticker, string exchange, StockTrackingStatus trackingStatus)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Stocks.Add(new Stock
            {
                Id = stockId,
                Ticker = ticker,
                Name = ticker == string.Empty ? $"Stock {stockId}" : ticker,
                CommonName = ticker == string.Empty ? $"Stock {stockId}" : ticker,
                Exchange = exchange,
                TrackingStatus = trackingStatus,
                UpdatedAt = Clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        public async Task SeedSnapshotAsync(int stockId, DateTime fetchedAtUtc, decimal marketCap = 111m)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FundamentalsSnapshots.Add(new CompanyFundamentalsSnapshot
            {
                StockId = stockId,
                SourceSymbol = $"S{stockId}",
                MarketCap = marketCap,
                Currency = "USD",
                Source = "Yahoo Finance",
                AsOfDate = fetchedAtUtc.Date,
                FetchedAtUtc = fetchedAtUtc
            });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
        }
    }

    private sealed class RecordingFundamentalsService : IFundamentalsService
    {
        public List<int> Calls { get; } = [];
        public Func<int, FundamentalsResult>? Behavior { get; set; }

        public Task<FundamentalsResult> GetFundamentalsAsync(int stockId, CancellationToken ct = default)
            => RefreshFundamentalsAsync(stockId, ct);

        public Task<FundamentalsResult> RefreshFundamentalsAsync(int stockId, CancellationToken ct = default)
        {
            Calls.Add(stockId);
            if (Behavior is not null)
            {
                return Task.FromResult(Behavior(stockId));
            }

            return Task.FromResult(FundamentalsResult.FromSnapshot(
                new CompanyFundamentalsSnapshot
                {
                    StockId = stockId,
                    SourceSymbol = $"S{stockId}",
                    MarketCap = 100m + stockId,
                    Currency = "USD",
                    Source = "Yahoo Finance",
                    AsOfDate = DateTime.UtcNow.Date,
                    FetchedAtUtc = DateTime.UtcNow
                },
                FundamentalsState.Fresh));
        }
    }

    private sealed class BlockingQuoteService(Task gate) : IStockQuoteFetchService
    {
        public async Task<StockQuoteFetchResult> FetchAsync(string ticker, string exchange, string? finanzenNetSlug, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            return StockQuoteFetchResult.Success(new StockQuoteResponse
            {
                Symbol = ticker,
                CurrentPriceEur = 10m,
                ChangeEur = 1m,
                PercentChange = 1m,
                MarketState = "REGULAR",
                PriceSession = "REGULAR",
                PriceTimestampUtc = new DateTime(2026, 8, 23, 0, 30, 0, DateTimeKind.Utc),
            });
        }
    }

    private sealed class RecordingHistoryService : IStockHistoryService
    {
        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());
        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryRefreshResponse { StockId = stock.Id });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTimeOffset value) => _now = value;
    }
}
