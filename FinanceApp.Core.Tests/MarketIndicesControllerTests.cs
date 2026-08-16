using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    private static MarketIndicesController CreateController(AppDbContext context)
    {
        return new MarketIndicesController(context, new NullMarketIndexHistoryService())
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
}
