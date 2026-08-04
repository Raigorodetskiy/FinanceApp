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

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
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

    [Fact]
    public async Task Update_WithQuoteSnapshot_PersistsChangeAndTimestampAtomically()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 1,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple Inc.",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 190m,
            UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var providerTs = new DateTime(2026, 8, 1, 14, 30, 0, DateTimeKind.Utc);
        var controller = CreateController(context);

        var result = await controller.Update(existing.Id, new Stock
        {
            Id = existing.Id,
            Ticker = existing.Ticker,
            Name = existing.Name,
            CommonName = existing.CommonName,
            Exchange = existing.Exchange,
            CurrentPrice = 195.40m,
            UpdatedAt = providerTs,
            CurrentPriceChange = 3.15m,
            CurrentPriceChangePercent = 1.30m,
            CurrentPriceAt = providerTs,
        });

        Assert.IsType<NoContentResult>(result);

        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal(195.40m, persisted.CurrentPrice);
        Assert.Equal(3.15m, persisted.CurrentPriceChange);
        Assert.Equal(1.30m, persisted.CurrentPriceChangePercent);
        Assert.Equal(providerTs, persisted.CurrentPriceAt);
    }

    [Fact]
    public async Task Update_ManualPriceEdit_ClearsStaleSnapshotFields()
    {
        await using var context = CreateContext();
        var providerTs = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var existing = new Stock
        {
            Id = 2,
            Ticker = "MSFT",
            Name = "Microsoft Corporation",
            CommonName = "Microsoft",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 420m,
            UpdatedAt = providerTs,
            CurrentPriceChange = 5m,
            CurrentPriceChangePercent = 1.2m,
            CurrentPriceAt = providerTs,
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Simulate a manual price edit: no snapshot fields supplied
        var result = await controller.Update(existing.Id, new Stock
        {
            Id = existing.Id,
            Ticker = existing.Ticker,
            Name = existing.Name,
            CommonName = existing.CommonName,
            Exchange = existing.Exchange,
            CurrentPrice = 400m,
            UpdatedAt = DateTime.UtcNow,
            // CurrentPriceChange / CurrentPriceChangePercent / CurrentPriceAt intentionally omitted
        });

        Assert.IsType<NoContentResult>(result);

        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal(400m, persisted.CurrentPrice);
        // Stale snapshot fields must be cleared to avoid showing outdated change/timestamp
        Assert.Null(persisted.CurrentPriceChange);
        Assert.Null(persisted.CurrentPriceChangePercent);
        Assert.Null(persisted.CurrentPriceAt);
    }

    [Fact]
    public async Task Create_NewStock_HasNullSnapshotFields()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(new Stock
        {
            Ticker = "GOOG",
            Name = "Alphabet Inc.",
            CommonName = "Google",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 185m,
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var stock = Assert.IsType<Stock>(created.Value);

        Assert.Null(stock.CurrentPriceChange);
        Assert.Null(stock.CurrentPriceChangePercent);
        Assert.Null(stock.CurrentPriceAt);
    }

    [Fact]
    public async Task ExistingStockRows_RemainValidAfterModelUpdate()
    {
        // Verifies backward compatibility: rows without snapshot fields are still valid
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 99,
            Ticker = "IBM",
            Name = "IBM Corp",
            CommonName = "IBM",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 130m,
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            // Snapshot fields are null (as they would be for existing rows before migration)
            CurrentPriceChange = null,
            CurrentPriceChangePercent = null,
            CurrentPriceAt = null,
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var loaded = await context.Stocks.FindAsync(99);
        Assert.NotNull(loaded);
        Assert.Equal(130m, loaded.CurrentPrice);
        Assert.Null(loaded.CurrentPriceChange);
        Assert.Null(loaded.CurrentPriceChangePercent);
        Assert.Null(loaded.CurrentPriceAt);
    }

    [Fact]
    public async Task Delete_ReferencedStock_ReturnsConflictAndDoesNotDelete()
    {
        await using var context = CreateContext();
        var user = new User
        {
            Id = 10,
            Username = "user",
            Email = "user@example.com",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow
        };
        var stock = new Stock
        {
            Id = 20,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            UpdatedAt = DateTime.UtcNow
        };
        var portfolio = new Portfolio
        {
            Id = 30,
            Name = "Main",
            UserId = user.Id,
            User = user,
            CreatedAt = DateTime.UtcNow
        };
        var item = new PortfolioItem
        {
            Id = 40,
            PortfolioId = portfolio.Id,
            Portfolio = portfolio,
            StockId = stock.Id,
            Stock = stock,
            Quantity = 1m,
            BuyPrice = 100m,
            BoughtAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Stocks.Add(stock);
        context.Portfolios.Add(portfolio);
        context.PortfolioItems.Add(item);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.Delete(stock.Id);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("Невозможно удалить акцию: она используется как минимум в одном портфеле.", conflict.Value);
        Assert.True(await context.Stocks.AnyAsync(s => s.Id == stock.Id));
    }

    [Fact]
    public async Task Delete_UnreferencedStock_ReturnsNoContentAndDeletes()
    {
        await using var context = CreateContext();
        var stock = new Stock
        {
            Id = 50,
            Ticker = "MSFT",
            Name = "Microsoft Corporation",
            CommonName = "Microsoft",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 200m,
            UpdatedAt = DateTime.UtcNow
        };
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.Delete(stock.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await context.Stocks.AnyAsync(s => s.Id == stock.Id));
    }

    [Fact]
    public async Task Delete_MissingStock_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }


    [Fact]
    public async Task RefreshHistory_MissingStock_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.RefreshHistory(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task RefreshHistory_BlankTicker_ReturnsBadRequest()
    {
        await using var context = CreateContext();
        context.Stocks.Add(new Stock
        {
            Id = 77,
            Ticker = "   ",
            Name = "No Ticker",
            CommonName = "No Ticker",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 1m,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.RefreshHistory(77);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("тикер", Assert.IsType<string>(badRequest.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithUnderscoreSlug_Accepted()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(new Stock
        {
            Ticker = "WDC",
            Name = "Western Digital",
            CommonName = "Western Digital",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 50m,
            FinanzenNetSlug = "western_digital-aktie"
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var stock = Assert.IsType<Stock>(created.Value);
        Assert.Equal("western_digital-aktie", stock.FinanzenNetSlug);
    }

    [Fact]
    public async Task Create_WithSlashInSlug_ReturnsBadRequest()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(new Stock
        {
            Ticker = "WDC",
            Name = "Western Digital",
            CommonName = "Western Digital",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 50m,
            FinanzenNetSlug = "invalid/slug"
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
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

        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryRefreshResponse { StockId = stock.Id });

        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
