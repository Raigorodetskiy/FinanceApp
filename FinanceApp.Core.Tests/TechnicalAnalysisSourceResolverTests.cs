using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace FinanceApp.Core.Tests;

public class TechnicalAnalysisSourceResolverTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;
    private readonly CommandCountingInterceptor _commandCounter = new();

    public TechnicalAnalysisSourceResolverTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commandCounter)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning))
            .Options;
        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private TechnicalAnalysisSourceResolver CreateResolver() => new(_dbContext);

    private async Task<Stock> AddStockAsync(
        int id,
        string ticker,
        string? isin = null,
        string exchange = StockExchanges.Frankfurt,
        string? providerSymbol = null)
    {
        var stock = new Stock
        {
            Id = id,
            Ticker = ticker,
            Name = ticker,
            CommonName = ticker,
            Exchange = exchange,
            ProviderSymbol = providerSymbol,
            Isin = isin
        };
        _dbContext.Stocks.Add(stock);
        await _dbContext.SaveChangesAsync();
        return stock;
    }

    private async Task AddDailyPricesAsync(int stockId, int count, DateTime? latestDate = null)
    {
        var baseDate = (latestDate ?? new DateTime(2024, 1, 1)).AddDays(-(count - 1));
        for (int i = 0; i < count; i++)
        {
            _dbContext.StockHistoricalPrices.Add(new StockHistoricalPrice
            {
                StockId = stockId,
                Interval = "1d",
                Timestamp = baseDate.AddDays(i),
                Open = 100, High = 101, Low = 99, Close = 100,
                QuoteUnitMultiplier = 1
            });
        }
        await _dbContext.SaveChangesAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_StockNotFound_ReturnsNotFound()
    {
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(999);
        Assert.Equal("NotFound", result.Resolution);
        Assert.Null(result.AnalysisStockId);
    }

    [Fact]
    public async Task ResolveAsync_SufficientOwnHistory_ReturnsOwnHistory()
    {
        await AddStockAsync(1, "ABC", "DE0001234567");
        await AddDailyPricesAsync(1, 252);
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        Assert.Equal("OwnHistory", result.Resolution);
        Assert.Equal(1, result.AnalysisStockId);
        Assert.False(result.IsInherited);
    }

    [Fact]
    public async Task ResolveAsync_InsufficientOwnHistory_FallsBackToSameIsin()
    {
        await AddStockAsync(1, "ABC", "DE0001234567", StockExchanges.Frankfurt, "ABC.DE");
        await AddStockAsync(2, "ABC", "DE0001234567", StockExchanges.Nyse, "ABC");
        await AddDailyPricesAsync(1, 100); // insufficient
        await AddDailyPricesAsync(2, 252); // sufficient
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        Assert.Equal("SameIsin", result.Resolution);
        Assert.Equal(2, result.AnalysisStockId);
        Assert.True(result.IsInherited);
        Assert.Equal(1, result.RequestedStockId);
    }

    [Fact]
    public async Task ResolveAsync_CandidateWithMoreHistory_WinsOverLess()
    {
        await AddStockAsync(1, "A", "DE0001234567");
        await AddStockAsync(2, "B", "DE0001234567");
        await AddStockAsync(3, "C", "DE0001234567");
        await AddDailyPricesAsync(1, 50);  // requested: insufficient
        await AddDailyPricesAsync(2, 300); // candidate 2: sufficient with more obs
        await AddDailyPricesAsync(3, 252); // candidate 3: sufficient with exactly threshold
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        // Both 2 and 3 are sufficient; 2 has more observations
        Assert.Equal(2, result.AnalysisStockId);
    }

    [Fact]
    public async Task ResolveAsync_FreshnessTieBreaker()
    {
        await AddStockAsync(1, "A", "DE0001234567");
        await AddStockAsync(2, "B", "DE0001234567");
        await AddStockAsync(3, "C", "DE0001234567");
        await AddDailyPricesAsync(1, 50); // requested: insufficient
        var older = new DateTime(2023, 6, 1);
        var newer = new DateTime(2024, 1, 1);
        await AddDailyPricesAsync(2, 252, older); // same count, older
        await AddDailyPricesAsync(3, 252, newer); // same count, newer
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        Assert.Equal(3, result.AnalysisStockId); // fresher wins
    }

    [Fact]
    public async Task ResolveAsync_StableIdTieBreaker()
    {
        await AddStockAsync(1, "A", "DE0001234567");
        await AddStockAsync(3, "B", "DE0001234567");
        await AddStockAsync(5, "C", "DE0001234567");
        var sameDate = new DateTime(2024, 1, 1);
        await AddDailyPricesAsync(1, 50);          // requested: insufficient
        await AddDailyPricesAsync(3, 252, sameDate);
        await AddDailyPricesAsync(5, 252, sameDate);
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        // Both 3 and 5 are equally good; smallest stockId wins
        Assert.Equal(3, result.AnalysisStockId);
    }

    [Fact]
    public async Task ResolveAsync_MissingIsin_NoFallback()
    {
        await AddStockAsync(1, "A", null); // no ISIN
        await AddStockAsync(2, "A", null);
        await AddDailyPricesAsync(1, 50);
        await AddDailyPricesAsync(2, 252);
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        // No ISIN → no fallback; since some data exists, returns InsufficientHistory
        Assert.NotEqual("SameIsin", result.Resolution);
        Assert.False(result.IsInherited);
    }

    [Fact]
    public async Task ResolveAsync_DifferentIsin_DoesNotMatch()
    {
        await AddStockAsync(1, "A", "DE0001111111");
        await AddStockAsync(2, "B", "DE0002222222"); // different ISIN
        await AddDailyPricesAsync(1, 50);
        await AddDailyPricesAsync(2, 252);
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        // Different ISIN → no fallback candidate, so own insufficient history is retained
        Assert.Equal(1, result.AnalysisStockId);
        Assert.Equal("InsufficientHistory", result.Resolution);
    }

    [Fact]
    public async Task ResolveAsync_NoUsableHistory_ReturnsNoSuitableHistory()
    {
        await AddStockAsync(1, "A", "DE0001234567");
        // No prices at all
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        Assert.Equal("NoSuitableHistory", result.Resolution);
        Assert.Null(result.AnalysisStockId);
    }

    [Fact]
    public async Task ResolveAsync_BothRequestedAndSelectedIds_AreCorrect()
    {
        await AddStockAsync(10, "X", "US1234567890");
        await AddStockAsync(20, "Y", "US1234567890");
        await AddDailyPricesAsync(10, 100);
        await AddDailyPricesAsync(20, 252);
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(10);
        Assert.Equal(10, result.RequestedStockId);
        Assert.Equal(20, result.AnalysisStockId);
        Assert.True(result.IsInherited);
    }

    [Fact]
    public async Task ResolveAsync_IsinNormalization_CaseInsensitive()
    {
        await AddStockAsync(1, "A", "de0001234567"); // lowercase
        await AddStockAsync(2, "B", "DE0001234567"); // uppercase
        await AddDailyPricesAsync(1, 50);
        await AddDailyPricesAsync(2, 252);
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        Assert.Equal(2, result.AnalysisStockId);
    }

    [Fact]
    public async Task ResolveAsync_InvalidIsin_DoesNotEnableFallback()
    {
        await AddStockAsync(1, "A", "INVALID");
        await AddStockAsync(2, "B", "INVALID");
        await AddDailyPricesAsync(1, 50);
        await AddDailyPricesAsync(2, 252);

        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);

        Assert.Equal(1, result.AnalysisStockId);
        Assert.Equal("InsufficientHistory", result.Resolution);
        Assert.Null(result.Isin);
    }

    [Fact]
    public async Task ResolveAsync_RealSchema_AllowsSameNormalizedIsinAcrossListings()
    {
        await AddStockAsync(1, "SAP", "de0007164600", StockExchanges.Frankfurt, "SAP.DE");
        await AddStockAsync(2, "SAP", "DE0007164600", StockExchanges.Nyse, "SAP");

        var stocks = await _dbContext.Stocks
            .OrderBy(x => x.Id)
            .ToListAsync();

        Assert.Equal(2, stocks.Count);
        Assert.Equal("de0007164600", stocks[0].Isin);
        Assert.Equal("DE0007164600", stocks[1].Isin);
    }

    [Fact]
    public async Task ResolveAsync_UsesBoundedQueryCount_ForSameIsinRanking()
    {
        await AddStockAsync(1, "A", "DE0001234567");
        await AddStockAsync(2, "B", "DE0001234567");
        await AddStockAsync(3, "C", "DE0001234567");
        await AddStockAsync(4, "D", "DE0001234567");
        await AddDailyPricesAsync(1, 10);
        await AddDailyPricesAsync(2, 300);
        await AddDailyPricesAsync(3, 280);
        await AddDailyPricesAsync(4, 260);

        _commandCounter.Reset();

        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);

        Assert.Equal(2, result.AnalysisStockId);
        Assert.InRange(_commandCounter.CommandCount, 1, 3);
    }

    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        public int CommandCount { get; private set; }

        public void Reset() => CommandCount = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CommandCount++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task ResolveAsync_SameTicker_DifferentIsin_DoesNotMatch()
    {
        await AddStockAsync(1, "BMW", "DE0005190003"); // BMW common
        await AddStockAsync(2, "BMW", "DE0005190037"); // BMW preference - different ISIN
        await AddDailyPricesAsync(1, 50);
        await AddDailyPricesAsync(2, 252);
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        Assert.Equal(1, result.AnalysisStockId);
        Assert.Equal("InsufficientHistory", result.Resolution);
    }

    [Fact]
    public async Task ResolveAsync_NoCandidatesWithUsableHistory_ReturnsNoSuitable()
    {
        await AddStockAsync(1, "A", "DE0001234567");
        await AddStockAsync(2, "B", "DE0001234567");
        await AddDailyPricesAsync(1, 50);
        // Stock 2 has NO usable history at all
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(1);
        // Only candidate with usable history is stock 1 itself (insufficient)
        // Stock 1 has 50 obs but no ISIN-matching candidate with obs
        // Stock 1 is included as fallback candidate → returns InsufficientHistory
        Assert.Equal(1, result.AnalysisStockId);
        Assert.False(result.IsInherited);
    }
}
