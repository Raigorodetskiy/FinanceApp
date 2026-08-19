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

public class CatalogStockRefreshHostedServiceTests
{
    [Fact]
    public void ScheduleCalculator_BeforeAndAfterTime_ComputesNextRun()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var before = new DateTimeOffset(2026, 8, 18, 18, 0, 0, TimeSpan.Zero); // 20:00 local
        var after = new DateTimeOffset(2026, 8, 18, 21, 0, 0, TimeSpan.Zero); // 23:00 local

        var beforeSnapshot = CatalogStockRefreshScheduleCalculator.Snapshot(before, tz, new TimeSpan(22, 30, 0));
        var afterSnapshot = CatalogStockRefreshScheduleCalculator.Snapshot(after, tz, new TimeSpan(22, 30, 0));

        Assert.Equal(new DateTimeOffset(2026, 8, 18, 20, 30, 0, TimeSpan.Zero), beforeSnapshot.NextScheduledRunUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 20, 30, 0, TimeSpan.Zero), afterSnapshot.NextScheduledRunUtc);
    }

    [Fact]
    public void ScheduleCalculator_EuropeBerlin_DstTransitionsAreHandled()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var springForwardDay = DateOnly.FromDateTime(new DateTime(2026, 3, 29));
        var fallBackDay = DateOnly.FromDateTime(new DateTime(2026, 10, 25));

        var springUtc = CatalogStockRefreshScheduleCalculator.ToUtc(springForwardDay, new TimeSpan(22, 30, 0), tz);
        var fallUtc = CatalogStockRefreshScheduleCalculator.ToUtc(fallBackDay, new TimeSpan(22, 30, 0), tz);

        // CEST (UTC+2) at the end of March, CET (UTC+1) at the end of October.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 20, 30, 0, TimeSpan.Zero), springUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 21, 30, 0, TimeSpan.Zero), fallUtc);
    }

    [Fact]
    public async Task Run_ProcessesTrackedAndCatalogOnly_ExactlyOnce_For600Plus()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 20, 40, 0, TimeSpan.Zero));
        var quote = new RecordingQuoteService();
        var history = new RecordingHistoryService();
        await using var harness = await Harness.CreateAsync(clock, quote, history, options: new CatalogStockRefreshJobOptions
        {
            BatchSize = 37,
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0,
            Enabled = true
        });

        await harness.SeedManyStocksAsync(620);
        var businessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19));
        await harness.Service.TriggerRunAsync(businessDate, clock.GetUtcNow().UtcDateTime, "test");

        await using var scope = harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.CatalogStockRefreshRuns.SingleAsync();
        Assert.Equal(CatalogStockRefreshRunStatus.Completed, run.Status);
        Assert.Equal(620, run.TotalDiscovered);
        Assert.Equal(620, run.Processed);
        Assert.Equal(620, run.QuoteSucceeded);
        Assert.Equal(620, run.HistorySucceeded);
        Assert.Equal(620, quote.ProcessedIds.Count);
        Assert.Equal(620, quote.ProcessedIds.Distinct().Count());
        Assert.Equal(620, history.ProcessedIds.Count);
        Assert.Equal(620, history.ProcessedIds.Distinct().Count());
    }

    [Fact]
    public async Task Run_InvalidMetadata_IsSkipped_AndFollowingStocksContinue()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 20, 40, 0, TimeSpan.Zero));
        var quote = new RecordingQuoteService();
        var history = new RecordingHistoryService();
        await using var harness = await Harness.CreateAsync(clock, quote, history, options: new CatalogStockRefreshJobOptions
        {
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0
        });

        await harness.SeedStockAsync(1, string.Empty, StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.SeedStockAsync(2, "BAD", "UNKNOWN", StockTrackingStatus.CatalogOnly);
        await harness.SeedStockAsync(3, "GOOD", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.Service.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 19)), clock.GetUtcNow().UtcDateTime, "test");

        await using var scope = harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.CatalogStockRefreshRuns.SingleAsync();

        Assert.Equal(3, run.Processed);
        Assert.Equal(2, run.QuoteSkipped);
        Assert.Equal(2, run.HistorySkipped);
        Assert.Equal(1, run.QuoteSucceeded);
        Assert.Equal(1, run.HistorySucceeded);
        Assert.Single(quote.ProcessedIds);
        Assert.Equal(3, quote.ProcessedIds[0]);
    }

    [Fact]
    public async Task Run_DoesNotDuplicate_WhenSameBusinessDateAlreadyCompleted()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 20, 40, 0, TimeSpan.Zero));
        var quote = new RecordingQuoteService();
        var history = new RecordingHistoryService();
        await using var harness = await Harness.CreateAsync(clock, quote, history, options: new CatalogStockRefreshJobOptions
        {
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0
        });
        await harness.SeedStockAsync(1, "AAPL", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        var businessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19));

        await harness.Service.TriggerRunAsync(businessDate, clock.GetUtcNow().UtcDateTime, "first");
        var callsAfterFirst = quote.ProcessedIds.Count;
        await harness.Service.TriggerRunAsync(businessDate, clock.GetUtcNow().UtcDateTime, "second");

        Assert.Equal(callsAfterFirst, quote.ProcessedIds.Count);
    }

    [Fact]
    public async Task Run_RecoversExpiredLease()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 20, 40, 0, TimeSpan.Zero));
        var quote = new RecordingQuoteService();
        var history = new RecordingHistoryService();
        await using var harness = await Harness.CreateAsync(clock, quote, history, options: new CatalogStockRefreshJobOptions
        {
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0
        });
        await harness.SeedStockAsync(10, "SAP", StockExchanges.Frankfurt, StockTrackingStatus.CatalogOnly);

        await using (var scope = harness.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CatalogStockRefreshRuns.Add(new CatalogStockRefreshRun
            {
                RunKey = "Europe/Berlin:2026-08-19",
                BusinessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19)),
                TimeZoneId = "Europe/Berlin",
                ScheduledAtUtc = clock.GetUtcNow().UtcDateTime,
                Status = CatalogStockRefreshRunStatus.Running,
                LeaseOwner = "another-instance",
                LeaseExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-5),
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
                UpdatedAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-10)
            });
            await db.SaveChangesAsync();
        }

        await harness.Service.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 19)), clock.GetUtcNow().UtcDateTime, "resume");

        await using var verify = harness.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await verifyDb.CatalogStockRefreshRuns.SingleAsync();
        Assert.Equal(CatalogStockRefreshRunStatus.Completed, run.Status);
        Assert.Equal(1, run.Processed);
    }

    [Fact]
    public async Task Run_ResumePendingStock_SkipsAlreadyCompletedQuoteStep()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 20, 40, 0, TimeSpan.Zero));
        var quote = new RecordingQuoteService();
        var history = new RecordingHistoryService();
        await using var harness = await Harness.CreateAsync(clock, quote, history, options: new CatalogStockRefreshJobOptions
        {
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0
        });
        await harness.SeedStockAsync(1, "FIRST", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.SeedStockAsync(2, "SECOND", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);

        await using (var scope = harness.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CatalogStockRefreshRuns.Add(new CatalogStockRefreshRun
            {
                RunKey = "Europe/Berlin:2026-08-19",
                BusinessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19)),
                TimeZoneId = "Europe/Berlin",
                ScheduledAtUtc = clock.GetUtcNow().UtcDateTime,
                Status = CatalogStockRefreshRunStatus.Running,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                LastProcessedStockId = 0,
                PendingStockId = 1,
                PendingQuoteCompleted = true,
                PendingHistoryCompleted = false,
                TotalDiscovered = 2,
                Processed = 0,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        await harness.Service.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 19)), clock.GetUtcNow().UtcDateTime, "resume");

        Assert.DoesNotContain(1, quote.ProcessedIds);
        Assert.Contains(2, quote.ProcessedIds);
        Assert.Contains(1, history.ProcessedIds);
        Assert.Contains(2, history.ProcessedIds);
    }

    [Fact]
    public async Task CrossInstance_Lease_PreventsDuplicateExecution()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 20, 40, 0, TimeSpan.Zero));
        var quote = new RecordingQuoteService();
        var history = new RecordingHistoryService();
        await using var harness = await Harness.CreateAsync(clock, quote, history, options: new CatalogStockRefreshJobOptions
        {
            InterRequestDelay = TimeSpan.Zero,
            RetryLimit = 0
        });
        await harness.SeedStockAsync(1, "AAPL", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.SeedStockAsync(2, "MSFT", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);

        var serviceB = harness.CreateSecondService();
        var businessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19));

        await Task.WhenAll(
            harness.Service.TriggerRunAsync(businessDate, clock.GetUtcNow().UtcDateTime, "a"),
            serviceB.TriggerRunAsync(businessDate, clock.GetUtcNow().UtcDateTime, "b"));

        Assert.Equal(2, quote.ProcessedIds.Count);
        Assert.Equal(2, quote.ProcessedIds.Distinct().Count());
    }

    [Fact]
    public async Task GetStatus_ReturnsBoundedUsefulState()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 20, 40, 0, TimeSpan.Zero));
        var quote = new RecordingQuoteService();
        var history = new RecordingHistoryService();
        await using var harness = await Harness.CreateAsync(clock, quote, history);
        await harness.SeedStockAsync(1, "AAPL", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.Service.TriggerRunAsync(DateOnly.FromDateTime(new DateTime(2026, 8, 19)), clock.GetUtcNow().UtcDateTime, "test");

        var status = await harness.Service.GetStatusAsync();
        Assert.NotNull(status.CurrentOrLatestRun);
        Assert.Equal("Europe/Berlin", status.TimeZoneId);
        Assert.True(status.NextScheduledRunUtc > status.GeneratedAtUtc);
        Assert.InRange(status.CurrentOrLatestRun!.FailureSummary?.Length ?? 0, 0, 4000);
    }

    [Fact]
    public void StatusController_UsesAuthorizeConvention()
    {
        var attribute = typeof(CatalogStockRefreshController).GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault();
        Assert.NotNull(attribute);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(ServiceProvider services, CatalogStockRefreshHostedService service, FixedTimeProvider clock, CatalogStockRefreshJobOptions options)
        {
            Services = services;
            Service = service;
            Clock = clock;
            JobOptions = options;
        }

        public ServiceProvider Services { get; }
        public CatalogStockRefreshHostedService Service { get; }
        public FixedTimeProvider Clock { get; }
        public CatalogStockRefreshJobOptions JobOptions { get; }

        public static async Task<Harness> CreateAsync(
            FixedTimeProvider clock,
            RecordingQuoteService quoteService,
            RecordingHistoryService historyService,
            CatalogStockRefreshJobOptions? options = null)
        {
            var opts = options ?? new CatalogStockRefreshJobOptions();
            var services = new ServiceCollection();
            var dbName = $"catalog-refresh-tests-{Guid.NewGuid():N}";
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddDbContext<AppDbContext>(b => b.UseInMemoryDatabase(dbName));
            services.AddScoped<IStockQuoteFetchService>(_ => quoteService);
            services.AddScoped<IStockHistoryService>(_ => historyService);

            var provider = services.BuildServiceProvider();
            await using var setup = provider.CreateAsyncScope();
            await setup.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

            var service = new CatalogStockRefreshHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                clock,
                Options.Create(opts),
                NullLogger<CatalogStockRefreshHostedService>.Instance,
                new CatalogMaintenanceLeaseService(provider.GetRequiredService<IServiceScopeFactory>(), clock),
                (_, _) => Task.CompletedTask);

            return new Harness(provider, service, clock, opts);
        }

        public CatalogStockRefreshHostedService CreateSecondService()
            => new(
                Services.GetRequiredService<IServiceScopeFactory>(),
                Clock,
                Options.Create(JobOptions),
                NullLogger<CatalogStockRefreshHostedService>.Instance,
                new CatalogMaintenanceLeaseService(Services.GetRequiredService<IServiceScopeFactory>(), Clock),
                (_, _) => Task.CompletedTask);

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

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
        }
    }

    private sealed class RecordingQuoteService : IStockQuoteFetchService
    {
        public List<int> ProcessedIds { get; } = [];

        public Task<StockQuoteFetchResult> FetchAsync(
            string ticker,
            string exchange,
            string? finanzenNetSlug,
            CancellationToken cancellationToken = default)
        {
            if (ticker.Length > 1 && ticker.StartsWith('T') && int.TryParse(ticker[1..], out var id))
            {
                ProcessedIds.Add(id);
            }
            else if (ticker == "AAPL")
            {
                ProcessedIds.Add(1);
            }
            else if (ticker == "MSFT")
            {
                ProcessedIds.Add(2);
            }
            else if (ticker == "SECOND")
            {
                ProcessedIds.Add(2);
            }
            else if (ticker == "GOOD")
            {
                ProcessedIds.Add(3);
            }
            else if (ticker == "SAP")
            {
                ProcessedIds.Add(10);
            }

            return Task.FromResult(StockQuoteFetchResult.Success(new StockQuoteResponse
            {
                Symbol = ticker,
                CurrentPriceEur = 10m,
                ChangeEur = 1m,
                PercentChange = 1m,
                MarketState = "REGULAR",
                PriceSession = "REGULAR",
                PriceTimestampUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc),
            }));
        }
    }

    private sealed class RecordingHistoryService : IStockHistoryService
    {
        public List<int> ProcessedIds { get; } = [];

        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());

        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
        {
            ProcessedIds.Add(stock.Id);
            return Task.FromResult(new StockHistoryRefreshResponse
            {
                StockId = stock.Id,
                DeletedPoints = 0,
                ImportedPoints = 0,
                RateLimited = false
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTimeOffset value) => _now = value;
    }
}
