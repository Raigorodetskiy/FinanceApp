using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class CatalogStockRefreshRunSchemaConstraintTests
{
    [Fact]
    public async Task Sqlite_AllowsSameBusinessDateAndTimeZone_ForDistinctOccurrenceRunKeys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        var businessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19));
        context.CatalogStockRefreshRuns.Add(new CatalogStockRefreshRun
        {
            RunKey = "Europe/Berlin:2026-08-19",
            BusinessDate = businessDate,
            TimeZoneId = "Europe/Berlin",
            ScheduledAtUtc = new DateTime(2026, 8, 18, 22, 0, 0, DateTimeKind.Utc),
            Status = CatalogStockRefreshRunStatus.CompletedWithErrors,
            CreatedAtUtc = new DateTime(2026, 8, 19, 3, 18, 36, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 8, 19, 5, 0, 46, DateTimeKind.Utc)
        });
        context.CatalogStockRefreshRuns.Add(new CatalogStockRefreshRun
        {
            RunKey = "Europe/Berlin:2026-08-19T20:30:00Z",
            BusinessDate = businessDate,
            TimeZoneId = "Europe/Berlin",
            ScheduledAtUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc),
            Status = CatalogStockRefreshRunStatus.Pending,
            CreatedAtUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc)
        });

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.CatalogStockRefreshRuns.CountAsync());
    }

    [Fact]
    public async Task Sqlite_RejectsDuplicateRunKey()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        context.CatalogStockRefreshRuns.Add(new CatalogStockRefreshRun
        {
            RunKey = "Europe/Berlin:2026-08-19T20:30:00Z",
            BusinessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19)),
            TimeZoneId = "Europe/Berlin",
            ScheduledAtUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc),
            Status = CatalogStockRefreshRunStatus.Pending,
            CreatedAtUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();

        context.CatalogStockRefreshRuns.Add(new CatalogStockRefreshRun
        {
            RunKey = "Europe/Berlin:2026-08-19T20:30:00Z",
            BusinessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 20)),
            TimeZoneId = "Europe/Berlin",
            ScheduledAtUtc = new DateTime(2026, 8, 20, 20, 30, 0, DateTimeKind.Utc),
            Status = CatalogStockRefreshRunStatus.Pending,
            CreatedAtUtc = new DateTime(2026, 8, 20, 20, 30, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 8, 20, 20, 30, 0, DateTimeKind.Utc)
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task IncidentPath_CorrectOccurrenceCoexistsWithLegacyRow_AndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 20, 30, 0, TimeSpan.Zero));
        var quote = new RecordingQuoteService();
        var history = new RecordingHistoryService();
        await using var services = BuildServices(connection, clock, quote, history);

        await using (var setup = services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Stocks.Add(new Stock
            {
                Id = 1,
                Ticker = "AAPL",
                Name = "Apple",
                CommonName = "Apple",
                Exchange = StockExchanges.Nyse,
                TrackingStatus = StockTrackingStatus.Tracked,
                UpdatedAt = clock.GetUtcNow().UtcDateTime
            });
            db.CatalogStockRefreshRuns.Add(new CatalogStockRefreshRun
            {
                RunKey = "Europe/Berlin:2026-08-19",
                BusinessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19)),
                TimeZoneId = "Europe/Berlin",
                ScheduledAtUtc = new DateTime(2026, 8, 18, 22, 0, 0, DateTimeKind.Utc),
                Status = CatalogStockRefreshRunStatus.CompletedWithErrors,
                CreatedAtUtc = new DateTime(2026, 8, 19, 3, 18, 36, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 8, 19, 5, 0, 46, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        var service = new CatalogStockRefreshHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            Options.Create(new CatalogStockRefreshJobOptions
            {
                Enabled = true,
                InterRequestDelay = TimeSpan.Zero,
                RetryLimit = 0
            }),
            NullLogger<CatalogStockRefreshHostedService>.Instance,
            new CatalogMaintenanceLeaseService(services.GetRequiredService<IServiceScopeFactory>(), clock),
            (_, _) => Task.CompletedTask);

        var businessDate = DateOnly.FromDateTime(new DateTime(2026, 8, 19));
        var occurrenceUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc);
        await service.TriggerRunAsync(businessDate, occurrenceUtc, "scheduled");
        var callsAfterFirstRun = quote.ProcessedIds.Count;
        await service.TriggerRunAsync(businessDate, occurrenceUtc, "scheduled");

        await using var verify = services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var runs = await verifyDb.CatalogStockRefreshRuns.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.Contains(runs, x => x.RunKey == "Europe/Berlin:2026-08-19");
        Assert.Contains(runs, x => x.RunKey == "Europe/Berlin:2026-08-19T20:30:00Z");
        Assert.Equal(callsAfterFirstRun, quote.ProcessedIds.Count);
        Assert.Equal(1, callsAfterFirstRun);
    }

    private static AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new AppDbContext(options);
    }

    private static ServiceProvider BuildServices(
        SqliteConnection connection,
        FixedTimeProvider clock,
        RecordingQuoteService quoteService,
        RecordingHistoryService historyService)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<AppDbContext>(b => b.UseSqlite(connection));
        services.AddScoped<IStockQuoteFetchService>(_ => quoteService);
        services.AddScoped<StockQuoteSnapshotPersistenceService>();
        services.AddScoped<IStockHistoryService>(_ => historyService);
        return services.BuildServiceProvider();
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
            if (ticker == "AAPL")
            {
                ProcessedIds.Add(1);
            }

            return Task.FromResult(StockQuoteFetchResult.Success(new StockQuoteResponse
            {
                Symbol = ticker,
                CurrentPriceEur = 10m,
                ChangeEur = 1m,
                PercentChange = 1m,
                MarketState = "REGULAR",
                PriceSession = "REGULAR",
                PriceTimestampUtc = new DateTime(2026, 8, 19, 20, 30, 0, DateTimeKind.Utc)
            }));
        }
    }

    private sealed class RecordingHistoryService : IStockHistoryService
    {
        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());
        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryRefreshResponse
            {
                StockId = stock.Id
            });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
