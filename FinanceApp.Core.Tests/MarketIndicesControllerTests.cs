using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class MarketIndicesControllerTests
{
    [Fact]
    public async Task Seed_ContainsAll27RequiredIndices()
    {
        await using var context = await CreateSqliteContextAsync();

        var marketIndices = await context.MarketIndices
            .OrderBy(x => x.Id)
            .ToListAsync();

        Assert.Equal(27, marketIndices.Count);
        Assert.Equal(new[] { "DJIA", "SPX", "COMP", "NDX", "RUT" }, marketIndices.Take(5).Select(x => x.Code));
        Assert.Contains(marketIndices, x => x.Code == "MSCIACWI" && x.CountryOrRegion == "Global");
    }

    [Fact]
    public async Task Crud_Archive_And_Restore_Work()
    {
        await using var context = await CreateSqliteContextAsync();
        var controller = CreateController(context);

        var createResult = await controller.CreateMarketIndex(new UpsertMarketIndexRequest
        {
            Name = "Test Index",
            Code = "TIDX",
            CountryOrRegion = "Testland",
            Description = "Описание",
            SortOrder = 999
        });

        var created = Assert.IsType<ObjectResult>(createResult.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var dto = Assert.IsType<MarketIndexDto>(created.Value);
        Assert.Equal("TIDX", dto.Code);

        var updateResult = await controller.UpdateMarketIndex(dto.Id, new UpsertMarketIndexRequest
        {
            Name = "Test Index Updated",
            Code = "TIDX2",
            CountryOrRegion = "Updated",
            Description = "Обновлено",
            SortOrder = 1000
        });
        var updated = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updatedDto = Assert.IsType<MarketIndexDto>(updated.Value);
        Assert.Equal("TIDX2", updatedDto.Code);

        var archiveResult = await controller.ArchiveMarketIndex(dto.Id);
        var archived = Assert.IsType<OkObjectResult>(archiveResult.Result);
        Assert.True(Assert.IsType<MarketIndexDto>(archived.Value).IsArchived);

        var restoreResult = await controller.RestoreMarketIndex(dto.Id);
        var restored = Assert.IsType<OkObjectResult>(restoreResult.Result);
        Assert.False(Assert.IsType<MarketIndexDto>(restored.Value).IsArchived);

        var deleteResult = await controller.DeleteMarketIndex(dto.Id);
        Assert.IsType<NoContentResult>(deleteResult);
        Assert.False(await context.MarketIndices.AnyAsync(x => x.Id == dto.Id));
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        await using var context = await CreateSqliteContextAsync();
        var controller = CreateController(context);

        var result = await controller.CreateMarketIndex(new UpsertMarketIndexRequest
        {
            Name = "Duplicate S&P",
            Code = "SPX",
            SortOrder = 10
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("Индекс с таким кодом уже существует.", conflict.Value);
    }

    [Fact]
    public async Task Delete_UsedMarketIndex_ReturnsConflict()
    {
        await using var context = await CreateSqliteContextAsync();
        var stock = new Stock
        {
            Id = 500,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            UpdatedAt = DateTime.UtcNow
        };
        context.Stocks.Add(stock);
        context.StockMarketIndices.Add(new StockMarketIndex { StockId = 500, MarketIndexId = 1 });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.DeleteMarketIndex(1);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("Нельзя удалить индекс, к которому привязаны акции.", conflict.Value);
    }

    [Fact]
    public async Task GetAll_ExcludeArchivedByDefault()
    {
        await using var context = await CreateSqliteContextAsync();
        context.MarketIndices.Add(new MarketIndex
        {
            Id = 1000,
            Name = "Archived Test",
            NormalizedName = "ARCHIVED TEST",
            Code = "ATST",
            NormalizedCode = "ATST",
            IsArchived = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<MarketIndexDto>>(ok.Value).ToList();

        Assert.DoesNotContain(items, x => x.Code == "ATST");
    }

    [Fact]
    public async Task Create_WithProviderSymbol_SavesAndReturnsIt()
    {
        await using var context = await CreateSqliteContextAsync();
        var controller = CreateController(context);

        var result = await controller.CreateMarketIndex(new UpsertMarketIndexRequest
        {
            Name = "Test Index With Symbol",
            Code = "TIDXS",
            ProviderSymbol = "^TIDX",
            SortOrder = 999
        });

        var created = Assert.IsType<ObjectResult>(result.Result);
        var dto = Assert.IsType<MarketIndexDto>(created.Value);
        Assert.Equal("^TIDX", dto.ProviderSymbol);
    }

    [Fact]
    public async Task Update_ChangingProviderSymbol_ClearsOldHistory()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;

        context.MarketIndices.Add(new MarketIndex
        {
            Id = 900,
            Name = "Symbol Change Test",
            NormalizedName = "SYMBOL CHANGE TEST",
            Code = "SCTST",
            NormalizedCode = "SCTST",
            ProviderSymbol = "^OLD",
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        context.MarketIndexHistoricalPrices.Add(new MarketIndexHistoricalPrice
        {
            MarketIndexId = 900,
            Timestamp = now.AddDays(-1),
            Interval = "1d",
            Open = 100m,
            High = 110m,
            Low = 95m,
            Close = 105m,
            ProviderSymbol = "^OLD"
        });
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.MarketIndexHistoricalPrices.CountAsync(x => x.MarketIndexId == 900));

        var controller = CreateController(context);
        await controller.UpdateMarketIndex(900, new UpsertMarketIndexRequest
        {
            Name = "Symbol Change Test",
            Code = "SCTST",
            ProviderSymbol = "^NEW",
            SortOrder = 0
        });

        Assert.Equal(0, await context.MarketIndexHistoricalPrices.CountAsync(x => x.MarketIndexId == 900));
    }

    [Fact]
    public async Task Update_SameProviderSymbol_PreservesHistory()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;

        context.MarketIndices.Add(new MarketIndex
        {
            Id = 901,
            Name = "Preserve History Test",
            NormalizedName = "PRESERVE HISTORY TEST",
            Code = "PHTST",
            NormalizedCode = "PHTST",
            ProviderSymbol = "^SAME",
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        context.MarketIndexHistoricalPrices.Add(new MarketIndexHistoricalPrice
        {
            MarketIndexId = 901,
            Timestamp = now.AddDays(-1),
            Interval = "1d",
            Open = 100m,
            High = 110m,
            Low = 95m,
            Close = 105m,
            ProviderSymbol = "^SAME"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        await controller.UpdateMarketIndex(901, new UpsertMarketIndexRequest
        {
            Name = "Preserve History Test Updated",
            Code = "PHTST",
            ProviderSymbol = "^SAME",
            SortOrder = 0
        });

        Assert.Equal(1, await context.MarketIndexHistoricalPrices.CountAsync(x => x.MarketIndexId == 901));
    }

    [Fact]
    public async Task GetHistory_NoProviderSymbol_Returns422()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;

        context.MarketIndices.Add(new MarketIndex
        {
            Id = 902,
            Name = "No Symbol Index",
            NormalizedName = "NO SYMBOL INDEX",
            Code = "NSIDX",
            NormalizedCode = "NSIDX",
            ProviderSymbol = null,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.GetHistory(902);

        Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetHistory_InvalidRange_Returns400()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;

        context.MarketIndices.Add(new MarketIndex
        {
            Id = 903,
            Name = "Range Test Index",
            NormalizedName = "RANGE TEST INDEX",
            Code = "RTIDX",
            NormalizedCode = "RTIDX",
            ProviderSymbol = "^TEST",
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.GetHistory(903, range: "invalid_range");

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetHistory_UnknownIndex_Returns404()
    {
        await using var context = await CreateSqliteContextAsync();
        var controller = CreateController(context);
        var result = await controller.GetHistory(99999);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Seed_ProviderSymbols_AreCorrectForKnownIndices()
    {
        await using var context = await CreateSqliteContextAsync();
        var indices = await context.MarketIndices
            .OrderBy(x => x.Id)
            .ToListAsync();

        var byCode = indices.ToDictionary(x => x.Code, x => x.ProviderSymbol);

        Assert.Equal("^DJI",      byCode["DJIA"]);
        Assert.Equal("^GSPC",     byCode["SPX"]);
        Assert.Equal("^IXIC",     byCode["COMP"]);
        Assert.Equal("^NDX",      byCode["NDX"]);
        Assert.Equal("^RUT",      byCode["RUT"]);
        Assert.Equal("^FTSE",     byCode["UKX"]);
        Assert.Equal("^GDAXI",    byCode["DAX"]);
        Assert.Equal("^N225",     byCode["NKY"]);
        Assert.Equal("^HSI",      byCode["HSI"]);
        Assert.Equal("000300.SS", byCode["CSI300"]);
        Assert.Equal("000001.SS", byCode["SHCOMP"]);
        Assert.Equal("FTSEMIB.MI", byCode["FTSEMIB"]);
        Assert.Equal("^BVSP",     byCode["IBOV"]);
        // MSCI indices intentionally have no direct public symbol
        Assert.Null(byCode["MSCIW"]);
        Assert.Null(byCode["MSCIEM"]);
        Assert.Null(byCode["MSCIACWI"]);
    }

    [Fact]
    public async Task Seed_ProviderSymbols_AreUnique_WhereNotNull()
    {
        await using var context = await CreateSqliteContextAsync();
        var symbols = await context.MarketIndices
            .Where(x => x.ProviderSymbol != null)
            .Select(x => x.ProviderSymbol!)
            .ToListAsync();

        Assert.Equal(symbols.Count, symbols.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("5y")]
    [InlineData("3y")]
    [InlineData("1y")]
    [InlineData("6m")]
    [InlineData("3m")]
    [InlineData("1m")]
    [InlineData("1w")]
    [InlineData("24h")]
    [InlineData("today")]
    public void RangeValidation_ValidRanges_DoNotThrow(string range)
    {
        var normalized = MarketIndexHistoryService.NormalizeRange(range);
        Assert.Equal(range, normalized);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("")]
    [InlineData("10y")]
    [InlineData("ALL")]
    public void RangeValidation_InvalidRanges_Throw(string range)
    {
        Assert.Throws<ArgumentException>(() => MarketIndexHistoryService.NormalizeRange(range));
    }

    [Theory]
    [InlineData("^DJI")]
    [InlineData("^GSPC")]
    [InlineData("000300.SS")]
    [InlineData("FTSEMIB.MI")]
    [InlineData("^BVSP")]
    public void SymbolValidation_ValidSymbols_ReturnsTrue(string symbol)
    {
        Assert.True(MarketIndexHistoryService.IsValidProviderSymbol(symbol));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("symbol with spaces")]
    [InlineData("symbol;DROP TABLE")]
    [InlineData("https://evil.com/path")]
    public void SymbolValidation_InvalidSymbols_ReturnsFalse(string? symbol)
    {
        Assert.False(MarketIndexHistoryService.IsValidProviderSymbol(symbol));
    }

    [Fact]
    public async Task RefreshConstituentHistory_CurrentCatalogOnlyMember_WorksWithoutTrackingMutation()
    {
        await using var context = await CreateSqliteContextAsync();
        var stock = new Stock
        {
            Id = 6001,
            Ticker = "AAPL",
            Name = "Apple",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            TrackingStatus = StockTrackingStatus.CatalogOnly,
            UpdatedAt = DateTime.UtcNow
        };
        context.Stocks.Add(stock);
        context.StockMarketIndices.Add(new StockMarketIndex { StockId = stock.Id, MarketIndexId = 1, EffectiveFrom = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var stockHistoryService = new TestStockHistoryService(
            (s, _) => Task.FromResult(new StockHistoryRefreshResponse { StockId = s.Id, DeletedPoints = 2, ImportedPoints = 10 }));
        var controller = CreateController(context, stockHistoryService: stockHistoryService);
        var result = await controller.RefreshConstituentHistory(1, stock.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<StockHistoryRefreshResponse>(ok.Value);
        Assert.Equal(stock.Id, payload.StockId);
        Assert.False(payload.RateLimited);
        Assert.Equal(new[] { stock.Id }, stockHistoryService.CallOrder);

        var persisted = await context.Stocks.AsNoTracking().SingleAsync(x => x.Id == stock.Id);
        Assert.Equal(StockTrackingStatus.CatalogOnly, persisted.TrackingStatus);
    }

    [Fact]
    public async Task RefreshConstituentHistory_TrackedMember_Works()
    {
        await using var context = await CreateSqliteContextAsync();
        var stock = new Stock
        {
            Id = 6002,
            Ticker = "MSFT",
            Name = "Microsoft",
            CommonName = "Microsoft",
            Exchange = StockExchanges.Nyse,
            TrackingStatus = StockTrackingStatus.Tracked,
            UpdatedAt = DateTime.UtcNow
        };
        context.Stocks.Add(stock);
        context.StockMarketIndices.Add(new StockMarketIndex { StockId = stock.Id, MarketIndexId = 1, EffectiveFrom = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            stockHistoryService: new TestStockHistoryService((s, _) => Task.FromResult(new StockHistoryRefreshResponse { StockId = s.Id, DeletedPoints = 1, ImportedPoints = 5 })));
        var result = await controller.RefreshConstituentHistory(1, stock.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<StockHistoryRefreshResponse>(ok.Value);
        Assert.Equal(stock.Id, payload.StockId);
    }

    [Fact]
    public async Task RefreshConstituentHistory_MissingOrFormerMember_ReturnsNotFound()
    {
        await using var context = await CreateSqliteContextAsync();
        var stock = new Stock
        {
            Id = 6003,
            Ticker = "SAP",
            Name = "SAP",
            CommonName = "SAP",
            Exchange = StockExchanges.Frankfurt,
            TrackingStatus = StockTrackingStatus.CatalogOnly,
            UpdatedAt = DateTime.UtcNow
        };
        context.Stocks.Add(stock);
        context.StockMarketIndices.Add(new StockMarketIndex
        {
            StockId = stock.Id,
            MarketIndexId = 1,
            EffectiveFrom = DateTime.UtcNow.AddDays(-10),
            EffectiveTo = DateTime.UtcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var missingIndex = await controller.RefreshConstituentHistory(999999, stock.Id);
        var formerMember = await controller.RefreshConstituentHistory(1, stock.Id);
        var missingStock = await controller.RefreshConstituentHistory(1, 999998);

        Assert.IsType<NotFoundObjectResult>(missingIndex.Result);
        Assert.IsType<NotFoundObjectResult>(formerMember.Result);
        Assert.IsType<NotFoundObjectResult>(missingStock.Result);
    }

    [Fact]
    public async Task RefreshConstituentHistory_ArchivedIndexOrInvalidStockData_ReturnsError()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        context.MarketIndices.Add(new MarketIndex
        {
            Id = 6004,
            Name = "Archived",
            NormalizedName = "ARCHIVED",
            Code = "AR6004",
            NormalizedCode = "AR6004",
            IsArchived = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Stocks.AddRange(
            new Stock
            {
                Id = 60041,
                Ticker = "IBM",
                Name = "IBM",
                CommonName = "IBM",
                Exchange = StockExchanges.Nyse,
                TrackingStatus = StockTrackingStatus.CatalogOnly,
                UpdatedAt = now
            },
            new Stock
            {
                Id = 60042,
                Ticker = "   ",
                Name = "BadTicker",
                CommonName = "BadTicker",
                Exchange = "???",
                TrackingStatus = StockTrackingStatus.CatalogOnly,
                UpdatedAt = now
            });
        context.StockMarketIndices.AddRange(
            new StockMarketIndex { StockId = 60041, MarketIndexId = 6004, EffectiveFrom = now },
            new StockMarketIndex { StockId = 60042, MarketIndexId = 1, EffectiveFrom = now });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var archived = await controller.RefreshConstituentHistory(6004, 60041);
        var invalid = await controller.RefreshConstituentHistory(1, 60042);

        Assert.IsType<ConflictObjectResult>(archived.Result);
        Assert.IsType<BadRequestObjectResult>(invalid.Result);
    }

    [Fact]
    public async Task RefreshConstituentsHistory_BatchIsSequential_ContinuesAfterFailures_AndStopsOnRateLimit()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        context.Stocks.AddRange(
            new Stock { Id = 6010, Ticker = "BBB", Name = "B", CommonName = "B", Exchange = StockExchanges.Nyse, TrackingStatus = StockTrackingStatus.CatalogOnly, UpdatedAt = now },
            new Stock { Id = 6009, Ticker = "AAA", Name = "A", CommonName = "A", Exchange = StockExchanges.Nyse, TrackingStatus = StockTrackingStatus.CatalogOnly, UpdatedAt = now },
            new Stock { Id = 6011, Ticker = "CCC", Name = "C", CommonName = "C", Exchange = StockExchanges.Nyse, TrackingStatus = StockTrackingStatus.CatalogOnly, UpdatedAt = now },
            new Stock { Id = 6012, Ticker = "DDD", Name = "D", CommonName = "D", Exchange = StockExchanges.Nyse, TrackingStatus = StockTrackingStatus.CatalogOnly, UpdatedAt = now });
        context.StockMarketIndices.AddRange(
            new StockMarketIndex { StockId = 6010, MarketIndexId = 1, EffectiveFrom = now },
            new StockMarketIndex { StockId = 6009, MarketIndexId = 1, EffectiveFrom = now },
            new StockMarketIndex { StockId = 6011, MarketIndexId = 1, EffectiveFrom = now },
            new StockMarketIndex { StockId = 6012, MarketIndexId = 1, EffectiveFrom = now });
        await context.SaveChangesAsync();

        var service = new TestStockHistoryService(async (stock, _) =>
        {
            await Task.Delay(5);
            return stock.Id switch
            {
                6010 => throw new InvalidOperationException("bad stock data"),
                6011 => new StockHistoryRefreshResponse { StockId = stock.Id, RateLimited = true },
                _ => new StockHistoryRefreshResponse { StockId = stock.Id, DeletedPoints = 1, ImportedPoints = 2 }
            };
        });
        var controller = CreateController(context, stockHistoryService: service);

        var result = await controller.RefreshConstituentsHistory(1);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<IndexConstituentHistoryRefreshBatchResponse>(ok.Value);

        Assert.Equal(new[] { 6009, 6010, 6011 }, service.CallOrder);
        Assert.Equal(1, service.MaxConcurrentCalls);
        Assert.Equal(4, payload.Total);
        Assert.Equal(3, payload.Attempted);
        Assert.Equal(1, payload.Succeeded);
        Assert.Equal(1, payload.Failed);
        Assert.Equal(1, payload.RateLimited);
        Assert.Equal(1, payload.SkippedRateLimited);
        Assert.True(payload.StoppedDueToRateLimit);
        Assert.Contains(payload.Results, x => x.StockId == 6012 && x.Status == "SkippedRateLimited");
    }

    [Fact]
    public async Task RefreshConstituentsHistory_RejectsDuplicateConcurrentBatch()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        context.Stocks.Add(new Stock
        {
            Id = 6020,
            Ticker = "AAPL",
            Name = "Apple",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            TrackingStatus = StockTrackingStatus.CatalogOnly,
            UpdatedAt = now
        });
        context.StockMarketIndices.Add(new StockMarketIndex { StockId = 6020, MarketIndexId = 1, EffectiveFrom = now });
        await context.SaveChangesAsync();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TestStockHistoryService(async (stock, _) =>
        {
            entered.TrySetResult();
            await gate.Task;
            return new StockHistoryRefreshResponse { StockId = stock.Id };
        });

        var firstController = CreateController(context, stockHistoryService: service);
        var secondController = CreateController(context, stockHistoryService: service);

        var firstBatchTask = firstController.RefreshConstituentsHistory(1);
        await entered.Task;
        var secondBatch = await secondController.RefreshConstituentsHistory(1);

        var conflict = Assert.IsType<ConflictObjectResult>(secondBatch.Result);
        Assert.Equal("Обновление исторических данных для этого индекса уже выполняется.", conflict.Value);

        gate.TrySetResult();
        await firstBatchTask;
    }

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

    private static MarketIndicesController CreateController(
        AppDbContext context,
        IIndexConstituentsProvider? provider = null,
        IStockHistoryService? stockHistoryService = null)
    {
        return new MarketIndicesController(
            context,
            new NullMarketIndexHistoryService(),
            provider ?? new NullIndexConstituentsProvider(),
            stockHistoryService ?? new NullStockHistoryService(),
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
            => Task.FromResult(new MarketIndexHistoryResponse { MarketIndexId = index.Id, Range = range, Interval = "1d" });

        public Task<MarketIndexRefreshResponse> RefreshHistoryAsync(MarketIndex index, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketIndexRefreshResponse { MarketIndexId = index.Id });
    }

    private sealed class NullIndexConstituentsProvider : IIndexConstituentsProvider
    {
        public string ProviderName => "Null";

        public Task<IndexConstituentsResult> GetConstituentsAsync(MarketIndex index, CancellationToken cancellationToken = default)
            => Task.FromResult(IndexConstituentsResult.Unsupported(ProviderName));
    }

    private sealed class NullStockHistoryService : IStockHistoryService
    {
        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());

        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryRefreshResponse { StockId = stock.Id });
    }

    private sealed class TestStockHistoryService(
        Func<Stock, CancellationToken, Task<StockHistoryRefreshResponse>> refreshHandler) : IStockHistoryService
    {
        private int _activeCalls;
        private int _maxConcurrentCalls;
        private readonly List<int> _callOrder = [];
        private readonly object _sync = new();

        public IReadOnlyList<int> CallOrder
        {
            get
            {
                lock (_sync)
                {
                    return _callOrder.ToList();
                }
            }
        }

        public int MaxConcurrentCalls => _maxConcurrentCalls;

        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());

        public async Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _callOrder.Add(stock.Id);
            }

            var current = Interlocked.Increment(ref _activeCalls);
            var max = Volatile.Read(ref _maxConcurrentCalls);
            while (current > max)
            {
                var updated = Interlocked.CompareExchange(ref _maxConcurrentCalls, current, max);
                if (updated == max)
                {
                    break;
                }

                max = updated;
            }

            try
            {
                return await refreshHandler(stock, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }
}
