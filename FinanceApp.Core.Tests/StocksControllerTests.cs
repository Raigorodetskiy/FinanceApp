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

        var result = await controller.UpdateMetadata(existing.Id, new UpdateStockMetadataRequest
        {
            Name = existing.Name,
            CommonName = existing.CommonName,
            Wkn = existing.Wkn,
            Isin = existing.Isin,
            CurrentPrice = 101.23m,
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

        var result = await controller.UpdateQuote(existing.Id, new UpdateStockQuoteRequest
        {
            CurrentPrice = 195.40m,
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

        // Simulate a manual price edit via metadata endpoint: clears stale snapshot fields
        var result = await controller.UpdateMetadata(existing.Id, new UpdateStockMetadataRequest
        {
            Name = existing.Name,
            CommonName = existing.CommonName,
            CurrentPrice = 400m,
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

    [Fact]
    public async Task UpdateQuote_UpdatesOnlyQuoteFields_DoesNotTouchIdentity()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 101,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 180m,
            Wkn = "865985",
            Isin = "US0378331005",
            FinanzenNetSlug = "apple-aktie",
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var providerTs = new DateTime(2026, 8, 1, 14, 30, 0, DateTimeKind.Utc);
        var controller = CreateController(context);

        var result = await controller.UpdateQuote(existing.Id, new UpdateStockQuoteRequest
        {
            CurrentPrice = 195.50m,
            CurrentPriceChange = 3.25m,
            CurrentPriceChangePercent = 1.69m,
            CurrentPriceAt = providerTs,
        });

        Assert.IsType<NoContentResult>(result);

        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal(195.50m, persisted.CurrentPrice);
        Assert.Equal(3.25m, persisted.CurrentPriceChange);
        Assert.Equal(1.69m, persisted.CurrentPriceChangePercent);
        Assert.Equal(providerTs, persisted.CurrentPriceAt);

        // Identity fields must NOT be modified
        Assert.Equal("AAPL", persisted.Ticker);
        Assert.Equal("Apple Inc.", persisted.Name);
        Assert.Equal("Apple", persisted.CommonName);
        Assert.Equal(StockExchanges.Nyse, persisted.Exchange);
        Assert.Equal("865985", persisted.Wkn);
        Assert.Equal("US0378331005", persisted.Isin);
        Assert.Equal("apple-aktie", persisted.FinanzenNetSlug);
    }

    [Fact]
    public async Task UpdateQuote_MissingStock_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.UpdateQuote(999, new UpdateStockQuoteRequest
        {
            CurrentPrice = 100m,
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateQuote_NullableFields_CanBeNull()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 102,
            Ticker = "MSFT",
            Name = "Microsoft Corporation",
            CommonName = "Microsoft",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 400m,
            CurrentPriceChange = 5m,
            CurrentPriceChangePercent = 1.25m,
            CurrentPriceAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        var result = await controller.UpdateQuote(existing.Id, new UpdateStockQuoteRequest
        {
            CurrentPrice = 410m,
            CurrentPriceChange = null,
            CurrentPriceChangePercent = null,
            CurrentPriceAt = null,
        });

        Assert.IsType<NoContentResult>(result);

        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal(410m, persisted.CurrentPrice);
        Assert.Null(persisted.CurrentPriceChange);
        Assert.Null(persisted.CurrentPriceChangePercent);
        Assert.Null(persisted.CurrentPriceAt);
    }

    [Fact]
    public async Task UpdateQuote_RaceCondition_DoesNotRevertMetadataEditedAfterQuoteFetch()
    {
        // Simulates: 1) quote fetch starts, 2) user edits metadata, 3) old quote response arrives
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 103,
            Ticker = "OLD.F",
            Name = "Old Name",
            CommonName = "Old",
            Exchange = StockExchanges.Frankfurt,
            CurrentPrice = 100m,
            UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Step 2: user edits metadata (name, etc.) — identity is immutable
        var editResult = await controller.UpdateMetadata(existing.Id, new UpdateStockMetadataRequest
        {
            Name = "New Name",
            CommonName = "New",
            CurrentPrice = 100m,
        });
        Assert.IsType<NoContentResult>(editResult);

        // Step 3: stale quote request completes — uses quote-only endpoint
        var quoteResult = await controller.UpdateQuote(existing.Id, new UpdateStockQuoteRequest
        {
            CurrentPrice = 102m,
            CurrentPriceChange = 2m,
            CurrentPriceChangePercent = 2.0m,
            CurrentPriceAt = new DateTime(2026, 8, 1, 14, 30, 0, DateTimeKind.Utc),
        });
        Assert.IsType<NoContentResult>(quoteResult);

        var persisted = await context.Stocks.SingleAsync();
        // Price was updated by the quote
        Assert.Equal(102m, persisted.CurrentPrice);
        // Identity fields are unchanged (immutable)
        Assert.Equal("OLD.F", persisted.Ticker);
        Assert.Equal(StockExchanges.Frankfurt, persisted.Exchange);
        // Name reflects the metadata edit, NOT reverted by the quote
        Assert.Equal("New Name", persisted.Name);
    }

    [Fact]
    public async Task UpdateQuote_StaleProviderTimestamp_DoesNotOverwriteNewerStoredQuote()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 104,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 210m,
            CurrentPriceChange = 3m,
            CurrentPriceChangePercent = 1.45m,
            CurrentPriceAt = new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 1, 15, 0, 5, DateTimeKind.Utc),
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.UpdateQuote(existing.Id, new UpdateStockQuoteRequest
        {
            CurrentPrice = 205m,
            CurrentPriceChange = -2m,
            CurrentPriceChangePercent = -0.97m,
            CurrentPriceAt = new DateTime(2026, 8, 1, 14, 0, 0, DateTimeKind.Utc),
        });

        Assert.IsType<NoContentResult>(result);

        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal(210m, persisted.CurrentPrice);
        Assert.Equal(3m, persisted.CurrentPriceChange);
        Assert.Equal(1.45m, persisted.CurrentPriceChangePercent);
        Assert.Equal(new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc), persisted.CurrentPriceAt);
    }

    // ─── New metadata endpoint tests ───────────────────────────────────────────

    [Fact]
    public async Task UpdateMetadata_ChangesAllAllowedFields_PreservesIdentity()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 200,
            Ticker = "SAP",
            Name = "SAP SE",
            CommonName = "SAP",
            Exchange = StockExchanges.Frankfurt,
            CurrentPrice = 100m,
            Wkn = "716460",
            Isin = "DE0007164600",
            FinanzenNetSlug = "sap-aktie",
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.UpdateMetadata(existing.Id, new UpdateStockMetadataRequest
        {
            Name = "SAP SE Updated",
            CommonName = "SAP Updated",
            Wkn = "716461",
            Isin = "DE0007164601",
            FinanzenNetSlug = "sap-aktie-new",
            CurrentPrice = 120m,
        });

        Assert.IsType<NoContentResult>(result);

        var persisted = await context.Stocks.SingleAsync();
        // Editable fields updated
        Assert.Equal("SAP SE Updated", persisted.Name);
        Assert.Equal("SAP Updated", persisted.CommonName);
        Assert.Equal("716461", persisted.Wkn);
        Assert.Equal("DE0007164601", persisted.Isin);
        Assert.Equal("sap-aktie-new", persisted.FinanzenNetSlug);
        Assert.Equal(120m, persisted.CurrentPrice);
        // Identity fields must NOT change
        Assert.Equal("SAP", persisted.Ticker);
        Assert.Equal(StockExchanges.Frankfurt, persisted.Exchange);
    }

    [Fact]
    public async Task UpdateMetadata_MissingStock_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.UpdateMetadata(999, new UpdateStockMetadataRequest
        {
            Name = "Ghost",
            CurrentPrice = 1m,
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateMetadata_ManualPrice_ClearsStaleSnapshotFields()
    {
        await using var context = CreateContext();
        var providerTs = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var existing = new Stock
        {
            Id = 201,
            Ticker = "MSFT",
            Name = "Microsoft Corporation",
            CommonName = "Microsoft",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 420m,
            CurrentPriceChange = 5m,
            CurrentPriceChangePercent = 1.2m,
            CurrentPriceAt = providerTs,
            UpdatedAt = providerTs,
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.UpdateMetadata(existing.Id, new UpdateStockMetadataRequest
        {
            Name = existing.Name,
            CurrentPrice = 400m,
        });

        Assert.IsType<NoContentResult>(result);

        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal(400m, persisted.CurrentPrice);
        Assert.Null(persisted.CurrentPriceChange);
        Assert.Null(persisted.CurrentPriceChangePercent);
        Assert.Null(persisted.CurrentPriceAt);
    }

    [Fact]
    public async Task UpdateMetadata_InvalidSlug_ReturnsBadRequest()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 202,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.UpdateMetadata(existing.Id, new UpdateStockMetadataRequest
        {
            Name = "Apple Inc.",
            FinanzenNetSlug = "invalid/slug",
            CurrentPrice = 100m,
        });

        Assert.IsType<BadRequestObjectResult>(result);
        // Stock must not be modified
        var persisted = await context.Stocks.SingleAsync();
        Assert.Null(persisted.FinanzenNetSlug);
    }

    [Fact]
    public async Task UpdateMetadata_BlankCommonName_FallsBackToName()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 203,
            Ticker = "IBM",
            Name = "IBM Corp",
            CommonName = "IBM",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.UpdateMetadata(existing.Id, new UpdateStockMetadataRequest
        {
            Name = "International Business Machines",
            CommonName = "   ",
            CurrentPrice = 100m,
        });

        Assert.IsType<NoContentResult>(result);
        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal("International Business Machines", persisted.Name);
        Assert.Equal("International Business Machines", persisted.CommonName);
    }

    [Fact]
    public async Task LegacyPut_ReturnsGone_PerformsNoWrite()
    {
        await using var context = CreateContext();
        var existing = new Stock
        {
            Id = 210,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 180m,
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        context.Stocks.Add(existing);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = controller.Update(existing.Id);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status410Gone, statusResult.StatusCode);

        // The stock must not be modified
        var persisted = await context.Stocks.SingleAsync();
        Assert.Equal("Apple Inc.", persisted.Name);
        Assert.Equal("AAPL", persisted.Ticker);
        Assert.Equal(180m, persisted.CurrentPrice);
    }

    [Fact]
    public async Task LegacyPut_ReturnsGone_MessageMentionsNewEndpoints()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = controller.Update(42);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status410Gone, statusResult.StatusCode);
        var message = Assert.IsType<string>(statusResult.Value);
        Assert.Contains("/metadata", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/quote", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithMarketIndexIds_PersistsJoinRows()
    {
        await using var context = CreateContext();
        context.MarketIndices.AddRange(
            new MarketIndex { Id = 1, Name = "S&P 500", NormalizedName = "S&P 500", Code = "SPX", NormalizedCode = "SPX", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new MarketIndex { Id = 2, Name = "NASDAQ-100", NormalizedName = "NASDAQ-100", Code = "NDX", NormalizedCode = "NDX", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.Create(new Stock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            MarketIndexIds = new List<int> { 1, 2 }
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var stock = Assert.IsType<Stock>(created.Value);
        Assert.Equal(new[] { 1, 2 }, stock.MarketIndexIds);
        Assert.Equal(2, await context.StockMarketIndices.CountAsync());
    }

    [Fact]
    public async Task UpdateMetadata_WithMarketIndexIds_SyncsJoinRows()
    {
        await using var context = CreateContext();
        context.MarketIndices.AddRange(
            new MarketIndex { Id = 1, Name = "S&P 500", NormalizedName = "S&P 500", Code = "SPX", NormalizedCode = "SPX", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new MarketIndex { Id = 2, Name = "NASDAQ-100", NormalizedName = "NASDAQ-100", Code = "NDX", NormalizedCode = "NDX", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new MarketIndex { Id = 3, Name = "MSCI World", NormalizedName = "MSCI WORLD", Code = "MSCIW", NormalizedCode = "MSCIW", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        var stock = new Stock
        {
            Id = 300,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            UpdatedAt = DateTime.UtcNow,
            MarketIndices = new List<StockMarketIndex>
            {
                new() { StockId = 300, MarketIndexId = 1 },
                new() { StockId = 300, MarketIndexId = 2 }
            }
        };
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.UpdateMetadata(stock.Id, new UpdateStockMetadataRequest
        {
            Name = stock.Name,
            CommonName = stock.CommonName,
            CurrentPrice = stock.CurrentPrice,
            MarketIndexIds = new List<int> { 2, 3 }
        });

        Assert.IsType<NoContentResult>(result);
        var joins = await context.StockMarketIndices
            .Where(x => x.StockId == stock.Id)
            .OrderBy(x => x.MarketIndexId)
            .Select(x => x.MarketIndexId)
            .ToListAsync();
        Assert.Equal(new[] { 2, 3 }, joins);
    }

    [Fact]
    public async Task Create_UnknownMarketIndexId_ReturnsBadRequest()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(new Stock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            MarketIndexIds = new List<int> { 999 }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Указан несуществующий мировой индекс.", badRequest.Value);
    }

    [Fact]
    public async Task Create_ArchivedMarketIndexId_ReturnsBadRequest()
    {
        await using var context = CreateContext();
        context.MarketIndices.Add(new MarketIndex
        {
            Id = 5,
            Name = "Archived",
            NormalizedName = "ARCHIVED",
            Code = "ARC",
            NormalizedCode = "ARC",
            IsArchived = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.Create(new Stock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            MarketIndexIds = new List<int> { 5 }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Нельзя привязать акцию к архивному мировому индексу.", badRequest.Value);
    }

    [Fact]
    public async Task UpdateMetadata_WithoutMarketIndexIds_PreservesExistingBindings()
    {
        await using var context = CreateContext();
        context.MarketIndices.Add(new MarketIndex
        {
            Id = 1,
            Name = "S&P 500",
            NormalizedName = "S&P 500",
            Code = "SPX",
            NormalizedCode = "SPX",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        var stock = new Stock
        {
            Id = 301,
            Ticker = "AAPL",
            Name = "Apple Inc.",
            CommonName = "Apple",
            Exchange = StockExchanges.Nyse,
            CurrentPrice = 100m,
            UpdatedAt = DateTime.UtcNow,
            MarketIndices = new List<StockMarketIndex>
            {
                new() { StockId = 301, MarketIndexId = 1 }
            }
        };
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.UpdateMetadata(stock.Id, new UpdateStockMetadataRequest
        {
            Name = "Apple Inc. Updated",
            CommonName = "Apple",
            CurrentPrice = 101m
        });

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(1, await context.StockMarketIndices.CountAsync(x => x.StockId == stock.Id));
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
