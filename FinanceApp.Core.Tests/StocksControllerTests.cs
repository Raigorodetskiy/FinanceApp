using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StocksControllerTests
{
    [Fact]
    public async Task Create_BlankExchange_DefaultsToNyse()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(new Stock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = "   ",
            CurrentPrice = 123.45m
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var stock = Assert.IsType<Stock>(created.Value);

        Assert.Equal(StockExchanges.Nyse, stock.Exchange);
        Assert.Equal(StockExchanges.Nyse, await context.Stocks.Select(x => x.Exchange).SingleAsync());
    }

    [Theory]
    [InlineData("nyse", StockExchanges.Nyse)]
    [InlineData(" Frankfurt ", StockExchanges.Frankfurt)]
    public async Task Create_ValidExchangeValues_AreNormalized(string inputExchange, string expectedExchange)
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(new Stock
        {
            Ticker = "TEST",
            Name = "Test Corp",
            CommonName = "Test Corp",
            Exchange = inputExchange,
            CurrentPrice = 1m
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var stock = Assert.IsType<Stock>(created.Value);

        Assert.Equal(expectedExchange, stock.Exchange);
    }

    [Fact]
    public async Task Create_UnsupportedExchange_IsRejected()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(new Stock
        {
            Ticker = "TEST",
            Name = "Test Corp",
            CommonName = "Test Corp",
            Exchange = "NASDAQ",
            CurrentPrice = 1m
        });

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Contains(nameof(Stock.Exchange), problem.Errors.Keys);
        Assert.Empty(context.Stocks);
    }

    [Fact]
    public async Task Create_BlankCommonName_FallsBackToName()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(new Stock
        {
            Ticker = "AAPL",
            Name = " Apple Inc. ",
            CommonName = "   ",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 123.45m
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var stock = Assert.IsType<Stock>(created.Value);

        Assert.Equal("Apple Inc.", stock.Name);
        Assert.Equal("Apple Inc.", stock.CommonName);
    }

    [Fact]
    public async Task Update_PriceOnlyRefresh_PreservesExistingMetadata()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 7,
            Ticker = "APC.F",
            Name = "Apple Inc. Frankfurt",
            CommonName = "Apple Inc.",
            Exchange = StockExchanges.Frankfurt,
            CurrentPrice = 100m,
            UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Wkn = "865985",
            Isin = "US0378331005"
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        var result = await controller.Update(existing.Id, new Stock
        {
            Id = existing.Id,
            Ticker = existing.Ticker,
            Name = existing.Name,
            CommonName = existing.CommonName,
            Exchange = existing.Exchange,
            CurrentPrice = 101.23m,
            UpdatedAt = DateTime.UtcNow,
            Wkn = existing.Wkn,
            Isin = existing.Isin
        });

        Assert.IsType<NoContentResult>(result);

        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal(101.23m, persisted.CurrentPrice);
        Assert.Equal("Apple Inc.", persisted.CommonName);
        Assert.Equal(StockExchanges.Frankfurt, persisted.Exchange);
        Assert.Equal("865985", persisted.Wkn);
        Assert.Equal("US0378331005", persisted.Isin);
        Assert.Equal("Apple Inc. Frankfurt", persisted.Name);
        Assert.Equal("APC.F", persisted.Ticker);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static StocksController CreateController(AppDbContext context)
    {
        return new StocksController(context, new StubStockHistoryService(), NullLogger<StocksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class StubStockHistoryService : IStockHistoryService
    {
        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());

        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
