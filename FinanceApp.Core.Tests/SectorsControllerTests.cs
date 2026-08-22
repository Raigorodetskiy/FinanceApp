using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Core.Tests;

public class SectorsControllerTests
{
    [Fact]
    public async Task GetAll_SectorWithDirectStocksAndNoIndustries_HasNonZeroStockCount()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var sector = CreateSector(9001, "Information Technology", now);
        context.Sectors.Add(sector);
        context.Stocks.AddRange(
            CreateStock(9101, "AAPL", now, StockTrackingStatus.Tracked, sectorId: sector.Id),
            CreateStock(9102, "MSFT", now, StockTrackingStatus.CatalogOnly, sectorId: sector.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.GetAll(includeArchived: true);
        var dto = FindSector(result, sector.Id);

        Assert.Equal(0, dto.IndustryCount);
        Assert.Equal(2, dto.StockCount);
    }

    [Fact]
    public async Task GetAll_IndustryClassifiedStocks_CountIndustryAndParentSector()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var sector = CreateSector(9002, "Health Care", now);
        var industry = CreateIndustry(9201, sector.Id, "Pharmaceuticals", now);
        context.Sectors.Add(sector);
        context.Industries.Add(industry);
        context.Stocks.AddRange(
            CreateStock(9301, "MRNA", now, StockTrackingStatus.CatalogOnly, industryId: industry.Id),
            CreateStock(9302, "PFE", now, StockTrackingStatus.Tracked, industryId: industry.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.GetAll(includeArchived: true);
        var dto = FindSector(result, sector.Id);
        var industryDto = Assert.Single(dto.Industries);

        Assert.Equal(2, industryDto.StockCount);
        Assert.Equal(2, dto.StockCount);
    }

    [Fact]
    public async Task GetAll_ConsistentIndustryAndDirectSector_IsCountedOnceForSector()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var sector = CreateSector(9003, "Financials", now);
        var industry = CreateIndustry(9203, sector.Id, "Banks", now);
        context.Sectors.Add(sector);
        context.Industries.Add(industry);
        context.Stocks.AddRange(
            CreateStock(9303, "JPM", now, StockTrackingStatus.CatalogOnly, sectorId: sector.Id, industryId: industry.Id),
            CreateStock(9304, "GS", now, StockTrackingStatus.CatalogOnly, sectorId: sector.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.GetAll(includeArchived: true);
        var dto = FindSector(result, sector.Id);

        Assert.Equal(1, Assert.Single(dto.Industries).StockCount);
        Assert.Equal(2, dto.StockCount);
    }

    [Fact]
    public async Task GetAll_ConflictingDirectSectorAndIndustrySector_CountsOnlyUnderIndustrySector()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var technology = CreateSector(9004, "Information Technology", now);
        var financials = CreateSector(9005, "Financials", now);
        var software = CreateIndustry(9204, technology.Id, "Software", now);
        context.Sectors.AddRange(technology, financials);
        context.Industries.Add(software);
        context.Stocks.Add(CreateStock(9305, "MSFT", now, StockTrackingStatus.CatalogOnly, sectorId: financials.Id, industryId: software.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.GetAll(includeArchived: true);
        var technologyDto = FindSector(result, technology.Id);
        var financialsDto = FindSector(result, financials.Id);

        Assert.Equal(1, technologyDto.StockCount);
        Assert.Equal(0, financialsDto.StockCount);
    }

    [Fact]
    public async Task GetAll_CountsTrackedAndCatalogOnlyStocks()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var sector = CreateSector(9006, "Industrials", now);
        context.Sectors.Add(sector);
        context.Stocks.AddRange(
            CreateStock(9306, "CAT", now, StockTrackingStatus.Tracked, sectorId: sector.Id),
            CreateStock(9307, "DE", now, StockTrackingStatus.CatalogOnly, sectorId: sector.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.GetAll(includeArchived: true);
        var dto = FindSector(result, sector.Id);

        Assert.Equal(2, dto.StockCount);
    }

    [Fact]
    public async Task GetAll_ArchivedFiltering_KeepsDirectSectorCountsAndAvoidsDuplicates()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var sector = CreateSector(9007, "Consumer Discretionary", now);
        var activeIndustry = CreateIndustry(9207, sector.Id, "Retail", now, isArchived: false);
        var archivedIndustry = CreateIndustry(9208, sector.Id, "Legacy Retail", now, isArchived: true);
        context.Sectors.Add(sector);
        context.Industries.AddRange(activeIndustry, archivedIndustry);
        context.Stocks.AddRange(
            CreateStock(9308, "AMZN", now, StockTrackingStatus.CatalogOnly, industryId: activeIndustry.Id),
            CreateStock(9309, "ETSY", now, StockTrackingStatus.CatalogOnly, industryId: archivedIndustry.Id),
            CreateStock(9310, "EBAY", now, StockTrackingStatus.CatalogOnly, sectorId: sector.Id),
            CreateStock(9311, "WMT", now, StockTrackingStatus.CatalogOnly, sectorId: sector.Id, industryId: archivedIndustry.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var visibleResult = await controller.GetAll(includeArchived: false);
        var visibleDto = FindSector(visibleResult, sector.Id);
        Assert.Single(visibleDto.Industries);
        Assert.Equal(activeIndustry.Id, visibleDto.Industries[0].Id);
        Assert.Equal(1, visibleDto.Industries[0].StockCount);
        Assert.Equal(4, visibleDto.StockCount);

        var allResult = await controller.GetAll(includeArchived: true);
        var allDto = FindSector(allResult, sector.Id);
        Assert.Equal(2, allDto.Industries.Count);
        Assert.Equal(4, allDto.StockCount);
    }

    [Fact]
    public async Task UpdateSector_ResponseUsesSameEffectiveSectorCountSemanticsAsGetAll()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var sector = CreateSector(9008, "Utilities", now);
        context.Sectors.Add(sector);
        context.Stocks.Add(CreateStock(9312, "NEE", now, StockTrackingStatus.CatalogOnly, sectorId: sector.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var updateResult = await controller.UpdateSector(sector.Id, new UpsertSectorRequest
        {
            Name = "Utilities Updated",
            SortOrder = 5
        });

        var ok = Assert.IsType<OkObjectResult>(updateResult.Result);
        var dto = Assert.IsType<SectorTreeItemDto>(ok.Value);
        Assert.Equal(1, dto.StockCount);
    }

    [Fact]
    public async Task UpdateIndustry_ResponseKeepsIndustryCountSemantics()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var sector = CreateSector(9009, "Energy", now);
        var industry = CreateIndustry(9209, sector.Id, "Oil & Gas", now);
        context.Sectors.Add(sector);
        context.Industries.Add(industry);
        context.Stocks.AddRange(
            CreateStock(9313, "XOM", now, StockTrackingStatus.CatalogOnly, industryId: industry.Id),
            CreateStock(9314, "CVX", now, StockTrackingStatus.CatalogOnly, sectorId: sector.Id, industryId: industry.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var updateResult = await controller.UpdateIndustry(sector.Id, industry.Id, new UpsertIndustryRequest
        {
            Name = "Oil & Gas Updated",
            SortOrder = 10
        });

        var ok = Assert.IsType<OkObjectResult>(updateResult.Result);
        var dto = Assert.IsType<IndustryTreeItemDto>(ok.Value);
        Assert.Equal(2, dto.StockCount);
    }

    [Fact]
    public async Task DeleteSector_WithDirectlyReferencedStocks_ReturnsConflict()
    {
        await using var context = await CreateSqliteContextAsync();
        var now = DateTime.UtcNow;
        var sector = CreateSector(9010, "Real Estate", now);
        context.Sectors.Add(sector);
        context.Stocks.Add(CreateStock(9315, "O", now, StockTrackingStatus.Tracked, sectorId: sector.Id));
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.DeleteSector(sector.Id);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("Нельзя удалить сектор, к которому напрямую привязаны акции.", conflict.Value);
    }

    private static SectorTreeItemDto FindSector(ActionResult<IEnumerable<SectorTreeItemDto>> result, int sectorId)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<SectorTreeItemDto>>(ok.Value);
        return Assert.Single(items, x => x.Id == sectorId);
    }

    private static Sector CreateSector(int id, string name, DateTime now)
        => new()
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            SortOrder = id,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static Industry CreateIndustry(int id, int sectorId, string name, DateTime now, bool isArchived = false)
        => new()
        {
            Id = id,
            SectorId = sectorId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            SortOrder = id,
            IsArchived = isArchived,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static Stock CreateStock(
        int id,
        string ticker,
        DateTime now,
        StockTrackingStatus trackingStatus,
        int? sectorId = null,
        int? industryId = null)
        => new()
        {
            Id = id,
            Ticker = ticker,
            Name = ticker,
            CommonName = ticker,
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 1m,
            UpdatedAt = now,
            TrackingStatus = trackingStatus,
            SectorId = sectorId,
            IndustryId = industryId
        };

    private static SectorsController CreateController(AppDbContext context)
        => new(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

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
}
