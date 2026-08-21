using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockQuoteSnapshotPersistenceServiceTests
{
    [Fact]
    public async Task ApplyAsync_NewerDelayedSnapshot_ReplacesOlderNonDelayedSnapshot()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 1,
            Ticker = "MTE.F",
            Name = "Seagate",
            CommonName = "Seagate",
            Exchange = StockExchanges.Frankfurt,
            CurrentPrice = 804m,
            CurrentPriceChange = -44m,
            CurrentPriceChangePercent = -5.19m,
            CurrentPriceAt = new DateTime(2026, 8, 18, 12, 17, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 18, 12, 17, 5, DateTimeKind.Utc),
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc)));
        var result = await service.ApplyAsync(1, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 752m,
            CurrentPriceChange = -52m,
            CurrentPriceChangePercent = -6.47m,
            CurrentPriceAt = new DateTime(2026, 8, 19, 8, 1, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = true,
            CurrentPriceDelayWarning = new string('Ж', 350),
        });

        Assert.True(result.Applied);
        Assert.True(result.CurrentPriceIsDelayed);
        Assert.Equal(StockQuoteSnapshotPersistenceService.DelayWarningMaxLength, result.CurrentPriceDelayWarning!.Length);

        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal(752m, persisted.CurrentPrice);
        Assert.Equal(-52m, persisted.CurrentPriceChange);
        Assert.Equal(-6.47m, persisted.CurrentPriceChangePercent);
        Assert.Equal(new DateTime(2026, 8, 19, 8, 1, 0, DateTimeKind.Utc), persisted.CurrentPriceAt);
        Assert.True(persisted.CurrentPriceIsDelayed);
        Assert.Equal(StockQuoteSnapshotPersistenceService.DelayWarningMaxLength, persisted.CurrentPriceDelayWarning!.Length);
    }

    [Fact]
    public async Task ApplyAsync_OlderDelayedSnapshot_DoesNotOverwriteNewerStoredSnapshot()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 2,
            Ticker = "SAP",
            Name = "SAP",
            CommonName = "SAP",
            Exchange = StockExchanges.Frankfurt,
            CurrentPrice = 250m,
            CurrentPriceChange = 2m,
            CurrentPriceChangePercent = 0.8m,
            CurrentPriceAt = new DateTime(2026, 8, 19, 8, 1, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 19, 8, 1, 5, DateTimeKind.Utc),
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.ApplyAsync(2, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 245m,
            CurrentPriceChange = -3m,
            CurrentPriceChangePercent = -1.2m,
            CurrentPriceAt = new DateTime(2026, 8, 19, 8, 0, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = true,
            CurrentPriceDelayWarning = "Котировка задержана",
        });

        Assert.False(result.Applied);
        Assert.Equal(250m, result.CurrentPrice);
        Assert.False(result.CurrentPriceIsDelayed);
    }

    [Fact]
    public async Task ApplyAsync_NewerNonDelayedSnapshot_ReplacesOlderDelayedSnapshotAndClearsMetadata()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 3,
            Ticker = "MTE.F",
            Name = "Seagate",
            CommonName = "Seagate",
            Exchange = StockExchanges.Frankfurt,
            CurrentPrice = 752m,
            CurrentPriceChange = -52m,
            CurrentPriceChangePercent = -6.47m,
            CurrentPriceAt = new DateTime(2026, 8, 19, 8, 1, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = true,
            CurrentPriceDelayWarning = "Котировка задержана",
            UpdatedAt = new DateTime(2026, 8, 19, 8, 1, 5, DateTimeKind.Utc),
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.ApplyAsync(3, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 760m,
            CurrentPriceChange = -44m,
            CurrentPriceChangePercent = -5.47m,
            CurrentPriceAt = new DateTime(2026, 8, 19, 8, 15, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = false,
            CurrentPriceDelayWarning = null,
        });

        Assert.True(result.Applied);
        Assert.False(result.CurrentPriceIsDelayed);
        Assert.Null(result.CurrentPriceDelayWarning);
    }

    [Fact]
    public async Task ApplyAsync_EqualTimestampEquivalentSnapshot_DoesNotRewrite()
    {
        var originalUpdatedAt = new DateTime(2026, 8, 19, 8, 2, 0, DateTimeKind.Utc);
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 4,
            Ticker = "AAPL",
            Name = "Apple",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 210m,
            CurrentPriceChange = 3m,
            CurrentPriceChangePercent = 1.45m,
            CurrentPriceAt = new DateTime(2026, 8, 19, 8, 1, 0, DateTimeKind.Utc),
            UpdatedAt = originalUpdatedAt,
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc)));
        var result = await service.ApplyAsync(4, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 210m,
            CurrentPriceChange = 3m,
            CurrentPriceChangePercent = 1.45m,
            CurrentPriceAt = new DateTime(2026, 8, 19, 8, 1, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = false,
        });

        Assert.False(result.Applied);
        Assert.Equal(originalUpdatedAt, (await context.Stocks.SingleAsync()).UpdatedAt);
    }

    [Fact]
    public async Task ApplyAsync_InvalidIncomingTimestamp_DoesNotOverwriteValidStoredSnapshot()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 5,
            Ticker = "AAPL",
            Name = "Apple",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 210m,
            CurrentPriceChange = 3m,
            CurrentPriceChangePercent = 1.45m,
            CurrentPriceAt = new DateTime(2026, 8, 19, 8, 1, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 19, 8, 2, 0, DateTimeKind.Utc),
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc)));
        var result = await service.ApplyAsync(5, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 150m,
            CurrentPriceChange = -1m,
            CurrentPriceChangePercent = -0.5m,
            CurrentPriceAt = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = true,
            CurrentPriceDelayWarning = "future",
        });

        Assert.False(result.Applied);
        Assert.Equal(210m, (await context.Stocks.SingleAsync()).CurrentPrice);
    }

    [Fact]
    public async Task ApplyAsync_ConcurrentOlderAndNewerUpdates_LeaveNewestSnapshotPersisted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc)));
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<StockQuoteSnapshotPersistenceService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.Stocks.Add(new Stock
            {
                Id = 6,
                Ticker = "MTE.F",
                Name = "Seagate",
                CommonName = "Seagate",
                Exchange = StockExchanges.Frankfurt,
                CurrentPrice = 804m,
                UpdatedAt = new DateTime(2026, 8, 18, 12, 17, 5, DateTimeKind.Utc),
            });
            await context.SaveChangesAsync();
        }

        Task<PersistStockQuoteSnapshotResult> RunAsync(decimal price, DateTime timestamp, bool delayed)
            => Task.Run(async () =>
            {
                await using var scope = provider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<StockQuoteSnapshotPersistenceService>();
                return await service.ApplyAsync(6, new PersistStockQuoteSnapshotRequest
                {
                    CurrentPrice = price,
                    CurrentPriceChange = price - 804m,
                    CurrentPriceChangePercent = 0m,
                    CurrentPriceAt = timestamp,
                    CurrentPriceIsDelayed = delayed,
                    CurrentPriceDelayWarning = delayed ? "Котировка задержана" : null,
                });
            });

        await Task.WhenAll(
            RunAsync(752m, new DateTime(2026, 8, 19, 8, 1, 0, DateTimeKind.Utc), true),
            RunAsync(760m, new DateTime(2026, 8, 19, 8, 15, 0, DateTimeKind.Utc), false));

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await verifyContext.Stocks.SingleAsync(x => x.Id == 6);
        Assert.Equal(760m, persisted.CurrentPrice);
        Assert.Equal(new DateTime(2026, 8, 19, 8, 15, 0, DateTimeKind.Utc), persisted.CurrentPriceAt);
        Assert.False(persisted.CurrentPriceIsDelayed);
    }

    [Fact]
    public async Task ApplyAsync_NewerTimestamp_AppendsQuoteDerivedIntradayPoint()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 7,
            Ticker = "AMD",
            Name = "AMD",
            CommonName = "AMD",
            Exchange = StockExchanges.Nasdaq,
            CurrentPrice = 200m,
            CurrentPriceAt = new DateTime(2026, 8, 24, 15, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 24, 15, 0, 0, DateTimeKind.Utc),
        });
        context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = 7,
            Interval = "10m",
            Timestamp = new DateTime(2026, 8, 24, 15, 0, 0, DateTimeKind.Utc),
            Open = 200m,
            High = 200m,
            Low = 200m,
            Close = 200m,
            QuoteCurrency = "USD",
            FinancialCurrency = "USD",
            NormalizedQuoteCurrency = "USD",
            QuoteUnitMultiplier = 1m,
            Volume = 1200,
            IsQuoteDerived = false,
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(new DateTime(2026, 8, 24, 16, 0, 0, DateTimeKind.Utc)));
        var result = await service.ApplyAsync(7, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 201m,
            CurrentPriceChange = 1m,
            CurrentPriceChangePercent = 0.5m,
            CurrentPriceAt = new DateTime(2026, 8, 24, 15, 21, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = false,
        });

        Assert.True(result.Applied);
        var intradayRows = await context.StockHistoricalPrices
            .Where(x => x.StockId == 7 && x.Interval == "10m")
            .OrderBy(x => x.Timestamp)
            .ToListAsync();
        Assert.Equal(2, intradayRows.Count);
        var newest = intradayRows[^1];
        Assert.Equal(new DateTime(2026, 8, 24, 15, 20, 0, DateTimeKind.Utc), newest.Timestamp);
        Assert.Equal(201m, newest.Close);
        Assert.True(newest.IsQuoteDerived);
        Assert.Equal("EUR", newest.QuoteCurrency);
    }

    [Fact]
    public async Task ApplyAsync_OlderOrEqualTimestamp_DoesNotRegressOrDuplicateIntradayHistory()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 8,
            Ticker = "AMD",
            Name = "AMD",
            CommonName = "AMD",
            Exchange = StockExchanges.Nasdaq,
            CurrentPrice = 202m,
            CurrentPriceAt = new DateTime(2026, 8, 24, 15, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 24, 15, 30, 0, DateTimeKind.Utc),
        });
        context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = 8,
            Interval = "10m",
            Timestamp = new DateTime(2026, 8, 24, 15, 30, 0, DateTimeKind.Utc),
            Open = 202m,
            High = 202m,
            Low = 202m,
            Close = 202m,
            QuoteCurrency = "EUR",
            FinancialCurrency = "EUR",
            NormalizedQuoteCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
            Volume = 0,
            IsQuoteDerived = true,
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(new DateTime(2026, 8, 24, 16, 0, 0, DateTimeKind.Utc)));
        var olderResult = await service.ApplyAsync(8, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 201m,
            CurrentPriceChange = -1m,
            CurrentPriceChangePercent = -0.49m,
            CurrentPriceAt = new DateTime(2026, 8, 24, 15, 29, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = false,
        });
        var equalResult = await service.ApplyAsync(8, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 203m,
            CurrentPriceChange = 1m,
            CurrentPriceChangePercent = 0.49m,
            CurrentPriceAt = new DateTime(2026, 8, 24, 15, 30, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = false,
        });

        Assert.False(olderResult.Applied);
        Assert.False(equalResult.Applied);
        var intradayRows = await context.StockHistoricalPrices
            .Where(x => x.StockId == 8 && x.Interval == "10m")
            .OrderBy(x => x.Timestamp)
            .ToListAsync();
        Assert.Single(intradayRows);
        Assert.Equal(202m, intradayRows[0].Close);
    }

    private static AppDbContext CreateInMemoryContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static StockQuoteSnapshotPersistenceService CreateService(
        AppDbContext context,
        TimeProvider? timeProvider = null)
        => new(
            context,
            timeProvider ?? new FixedTimeProvider(new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc)),
            NullLogger<StockQuoteSnapshotPersistenceService>.Instance);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTime utcNow)
        {
            _now = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
