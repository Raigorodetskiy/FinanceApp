using System.Security.Claims;
using FinanceApp.API.Controllers;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Core.Tests;

public class FinanceControllerTests
{
    [Fact]
    public async Task CreateTransaction_AllowsManualBuyWithoutStock_AndPreservesCreatedAt()
    {
        await using var context = CreateContext();
        context.Portfolios.Add(new Portfolio { Id = 10, Name = "Main", UserId = 1, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var controller = CreateController(context, userId: 1);
        var createdAtUtc = new DateTime(2026, 8, 1, 9, 30, 0, DateTimeKind.Utc);

        var result = await controller.CreateTransaction(10, new CreateTransactionDto
        {
            Type = TransactionType.Buy,
            Amount = 125.50m,
            CreatedAt = createdAtUtc,
            Description = "Ручная покупка",
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var transaction = Assert.IsType<Transaction>(ok.Value);

        Assert.Null(transaction.StockId);
        Assert.Equal(createdAtUtc, transaction.CreatedAt);
        Assert.Equal(createdAtUtc, await context.Transactions.Select(t => t.CreatedAt).SingleAsync());
    }

    [Fact]
    public async Task UpdateTransaction_ConvertsLocalCreatedAtToUtc_AndAllowsClearingStock()
    {
        await using var context = CreateContext();
        var stock = new Stock
        {
            Id = 50,
            Ticker = "GS",
            Name = "Goldman Sachs Group",
            CommonName = "Goldman Sachs Group",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            UpdatedAt = DateTime.UtcNow,
        };
        var portfolio = new Portfolio { Id = 11, Name = "Main", UserId = 1, CreatedAt = DateTime.UtcNow };
        var transaction = new Transaction
        {
            Id = 70,
            PortfolioId = 11,
            Type = TransactionType.Dividend,
            Amount = 10m,
            SignedAmount = 10m,
            StockId = 50,
            CreatedAt = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc),
        };

        context.Stocks.Add(stock);
        context.Portfolios.Add(portfolio);
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        var controller = CreateController(context, userId: 1);
        var localCreatedAt = new DateTime(2026, 8, 1, 14, 45, 0, DateTimeKind.Local);

        var result = await controller.UpdateTransaction(11, 70, new UpdateTransactionDto
        {
            Type = TransactionType.Dividend,
            Amount = 11m,
            CreatedAt = localCreatedAt,
            StockId = null,
            Description = "Ручной дивиденд",
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var updated = Assert.IsType<Transaction>(ok.Value);

        Assert.Null(updated.StockId);
        Assert.Equal(localCreatedAt.ToUniversalTime(), updated.CreatedAt);
        Assert.Equal(localCreatedAt.ToUniversalTime(), await context.Transactions.Select(t => t.CreatedAt).SingleAsync());
    }

    [Fact]
    public async Task CreateTransaction_MissingCreatedAt_ReturnsBadRequest()
    {
        await using var context = CreateContext();
        context.Portfolios.Add(new Portfolio { Id = 12, Name = "Main", UserId = 1, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var controller = CreateController(context, userId: 1);

        var result = await controller.CreateTransaction(12, new CreateTransactionDto
        {
            Type = TransactionType.Deposit,
            Amount = 50m,
            CreatedAt = default,
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("CreatedAt is required.", badRequest.Value);
    }

    [Fact]
    public async Task UpdateBalance_PersistsCashAdjustment_AndReturnsCalculatedTotals()
    {
        await using var context = CreateContext();
        var stock = new Stock
        {
            Id = 60,
            Ticker = "MSFT",
            Name = "Microsoft",
            CommonName = "Microsoft",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 200m,
            UpdatedAt = DateTime.UtcNow,
        };
        var portfolio = new Portfolio
        {
            Id = 13,
            Name = "Main",
            UserId = 1,
            CreatedAt = DateTime.UtcNow,
            BrokerCredit = 5m,
        };

        context.Stocks.Add(stock);
        context.Portfolios.Add(portfolio);
        context.PortfolioItems.Add(new PortfolioItem
        {
            Id = 1,
            PortfolioId = 13,
            StockId = 60,
            Stock = stock,
            Quantity = 2m,
            BuyPrice = 150m,
            BoughtAt = DateTime.UtcNow,
        });
        context.Transactions.Add(new Transaction
        {
            Id = 80,
            PortfolioId = 13,
            Type = TransactionType.Deposit,
            Amount = 40m,
            SignedAmount = 40m,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, userId: 1);

        var updateResult = await controller.UpdateBalance(13, new UpdatePortfolioBalanceDto
        {
            CashBalance = 70.126m,
            BrokerCredit = 15.125m,
        });

        var ok = Assert.IsType<OkObjectResult>(updateResult.Result);
        var balance = Assert.IsType<PortfolioBalance>(ok.Value);

        Assert.Equal(30.13m, await context.Portfolios.Select(p => p.CashBalanceAdjustment).SingleAsync());
        Assert.Equal(15.13m, await context.Portfolios.Select(p => p.BrokerCredit).SingleAsync());
        Assert.Equal(70.13m, balance.CashBalance);
        Assert.Equal(15.13m, balance.BrokerCredit);
        Assert.Equal(85.26m, balance.TotalBalance);
        Assert.Equal(400m, balance.StocksValue);
        Assert.Equal(485.26m, balance.TotalPortfolioValue);
    }

    [Fact]
    public async Task UpdateBalance_ForAnotherUsersPortfolio_ReturnsNotFound()
    {
        await using var context = CreateContext();
        context.Portfolios.Add(new Portfolio { Id = 14, Name = "Other", UserId = 2, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var controller = CreateController(context, userId: 1);

        var result = await controller.UpdateBalance(14, new UpdatePortfolioBalanceDto
        {
            CashBalance = 10m,
            BrokerCredit = 5m,
        });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static FinanceController CreateController(AppDbContext context, int userId)
    {
        return new FinanceController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    ], "TestAuth"))
                }
            }
        };
    }
}
