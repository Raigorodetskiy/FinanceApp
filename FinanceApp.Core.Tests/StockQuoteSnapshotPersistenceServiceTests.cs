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
    public async Task ApplyAsync_EqualTimestampEquivalentSnapshot_DoesNotRewrite_WhenProviderBucketAlreadyExists()
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
        context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = 4,
            Interval = "10m",
            Timestamp = new DateTime(2026, 8, 19, 8, 0, 0, DateTimeKind.Utc),
            Open = 210m,
            High = 210m,
            Low = 210m,
            Close = 210m,
            QuoteCurrency = "USD",
            FinancialCurrency = "USD",
            NormalizedQuoteCurrency = "USD",
            QuoteUnitMultiplier = 1m,
            Volume = 1000,
            IsQuoteDerived = false,
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
        Assert.False(result.SnapshotApplied);
        Assert.False(result.HistoryApplied);
        Assert.Equal(originalUpdatedAt, (await context.Stocks.SingleAsync()).UpdatedAt);
    }

    [Fact]
    public async Task ApplyAsync_EqualTimestamp_RepairsMissingCurrentSessionIntradayBucket_WithoutChangingSnapshot()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 41,
            Ticker = "SAP",
            Name = "SAP",
            CommonName = "SAP",
            Exchange = StockExchanges.Frankfurt,
            CurrentPrice = 404.40m,
            CurrentPriceChange = 1.25m,
            CurrentPriceChangePercent = 0.31m,
            CurrentPriceAt = new DateTime(2026, 8, 20, 14, 47, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 20, 14, 47, 2, DateTimeKind.Utc),
        });
        context.StockHistoricalPrices.AddRange(
            new StockHistoricalPrice
            {
                StockId = 41,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 19, 7, 0, 0, DateTimeKind.Utc),
                Open = 399m,
                High = 400m,
                Low = 398m,
                Close = 399.5m,
                QuoteCurrency = "EUR",
                FinancialCurrency = "EUR",
                NormalizedQuoteCurrency = "EUR",
                QuoteUnitMultiplier = 1m,
                Volume = 1000,
                IsQuoteDerived = false,
            },
            new StockHistoricalPrice
            {
                StockId = 41,
                Interval = "10m",
                Timestamp = new DateTime(2026, 8, 19, 15, 20, 0, DateTimeKind.Utc),
                Open = 401m,
                High = 402m,
                Low = 400m,
                Close = 401.5m,
                QuoteCurrency = "EUR",
                FinancialCurrency = "EUR",
                NormalizedQuoteCurrency = "EUR",
                QuoteUnitMultiplier = 1m,
                Volume = 1200,
                IsQuoteDerived = false,
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc)));
        var result = await service.ApplyAsync(41, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 404.40m,
            CurrentPriceChange = 1.25m,
            CurrentPriceChangePercent = 0.31m,
            CurrentPriceAt = new DateTime(2026, 8, 20, 14, 47, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = false,
            QuoteCurrency = "EUR",
            FinancialCurrency = "EUR",
            NormalizedQuoteCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
        });

        Assert.True(result.Applied);
        Assert.False(result.SnapshotApplied);
        Assert.True(result.HistoryApplied);

        var stock = await context.Stocks.SingleAsync(x => x.Id == 41);
        Assert.Equal(new DateTime(2026, 8, 20, 14, 47, 0, DateTimeKind.Utc), stock.CurrentPriceAt);
        Assert.Equal(404.40m, stock.CurrentPrice);

        var intradayRows = await context.StockHistoricalPrices
            .Where(x => x.StockId == 41 && x.Interval == "10m")
            .OrderBy(x => x.Timestamp)
            .ToListAsync();
        Assert.Equal(3, intradayRows.Count);
        var repaired = intradayRows[^1];
        Assert.Equal(new DateTime(2026, 8, 20, 14, 40, 0, DateTimeKind.Utc), repaired.Timestamp);
        Assert.True(repaired.IsQuoteDerived);
        Assert.Equal(404.40m, repaired.Open);
        Assert.Equal(404.40m, repaired.Close);
    }

    [Fact]
    public async Task ApplyAsync_EqualTimestamp_HistoryRepairIsIdempotent_AndNoDuplicatesCreated()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.Add(new Stock
        {
            Id = 42,
            Ticker = "AMD",
            Name = "AMD",
            CommonName = "AMD",
            Exchange = StockExchanges.Nasdaq,
            CurrentPrice = 500m,
            CurrentPriceAt = new DateTime(2026, 8, 24, 15, 35, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 24, 15, 35, 1, DateTimeKind.Utc),
        });
        context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = 42,
            Interval = "10m",
            Timestamp = new DateTime(2026, 8, 24, 15, 20, 0, DateTimeKind.Utc),
            Open = 498m,
            High = 499m,
            Low = 497m,
            Close = 498.5m,
            QuoteCurrency = "EUR",
            FinancialCurrency = "EUR",
            NormalizedQuoteCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
            Volume = 0,
            IsQuoteDerived = true,
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(new DateTime(2026, 8, 24, 16, 0, 0, DateTimeKind.Utc)));
        var request = new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 500m,
            CurrentPriceChange = 1.5m,
            CurrentPriceChangePercent = 0.30m,
            CurrentPriceAt = new DateTime(2026, 8, 24, 15, 35, 0, DateTimeKind.Utc),
            CurrentPriceIsDelayed = false,
            QuoteCurrency = "EUR",
            FinancialCurrency = "EUR",
            NormalizedQuoteCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
        };

        var first = await service.ApplyAsync(42, request);
        var second = await service.ApplyAsync(42, request);

        Assert.True(first.HistoryApplied);
        Assert.True(second.HistoryApplied);

        var rows = await context.StockHistoricalPrices
            .Where(x => x.StockId == 42 && x.Interval == "10m")
            .OrderBy(x => x.Timestamp)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateTime(2026, 8, 24, 15, 30, 0, DateTimeKind.Utc), rows[^1].Timestamp);
    }

    [Fact]
    public async Task ApplyAsync_HistoryUpsert_OnlyAffectsTargetListing()
    {
        await using var context = CreateInMemoryContext();
        context.Stocks.AddRange(
            new Stock
            {
                Id = 43,
                Ticker = "SAP",
                Name = "SAP Frankfurt",
                CommonName = "SAP",
                Exchange = StockExchanges.Frankfurt,
                CurrentPrice = 300m,
                CurrentPriceAt = new DateTime(2026, 8, 20, 14, 44, 0, DateTimeKind.Utc),
            },
            new Stock
            {
                Id = 44,
                Ticker = "SAP",
                Name = "SAP NYSE",
                CommonName = "SAP",
                Exchange = StockExchanges.Nyse,
                CurrentPrice = 200m,
                CurrentPriceAt = new DateTime(2026, 8, 20, 14, 44, 0, DateTimeKind.Utc),
            });
        context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = 44,
            Interval = "10m",
            Timestamp = new DateTime(2026, 8, 20, 14, 40, 0, DateTimeKind.Utc),
            Open = 200m,
            High = 200m,
            Low = 200m,
            Close = 200m,
            QuoteCurrency = "USD",
            FinancialCurrency = "USD",
            NormalizedQuoteCurrency = "USD",
            QuoteUnitMultiplier = 1m,
            Volume = 100,
            IsQuoteDerived = false,
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc)));
        await service.ApplyAsync(43, new PersistStockQuoteSnapshotRequest
        {
            CurrentPrice = 301m,
            CurrentPriceChange = 1m,
            CurrentPriceChangePercent = 0.33m,
            CurrentPriceAt = new DateTime(2026, 8, 20, 14, 44, 0, DateTimeKind.Utc),
            QuoteCurrency = "EUR",
            FinancialCurrency = "EUR",
            NormalizedQuoteCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
        });

        var stock43Rows = await context.StockHistoricalPrices.Where(x => x.StockId == 43 && x.Interval == "10m").ToListAsync();
        var stock44Rows = await context.StockHistoricalPrices.Where(x => x.StockId == 44 && x.Interval == "10m").ToListAsync();
        Assert.Single(stock43Rows);
        Assert.Single(stock44Rows);
        Assert.True(stock43Rows[0].IsQuoteDerived);
        Assert.False(stock44Rows[0].IsQuoteDerived);
        Assert.Equal(200m, stock44Rows[0].Close);
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
    public async Task ApplyAsync_OlderTimestamp_DoesNotRegress_AndEqualTimestampUpdatesSameQuoteDerivedBucketDeterministically()
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
        Assert.True(equalResult.Applied);
        Assert.False(equalResult.SnapshotApplied);
        Assert.True(equalResult.HistoryApplied);
        var intradayRows = await context.StockHistoricalPrices
            .Where(x => x.StockId == 8 && x.Interval == "10m")
            .OrderBy(x => x.Timestamp)
            .ToListAsync();
        Assert.Single(intradayRows);
        Assert.Equal(203m, intradayRows[0].Close);
        Assert.Equal(203m, intradayRows[0].High);
        Assert.Equal(202m, intradayRows[0].Low);
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
