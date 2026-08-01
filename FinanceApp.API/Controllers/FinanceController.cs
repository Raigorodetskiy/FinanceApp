using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinanceApp.Data.Data;
using FinanceApp.Core.Models;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/Portfolios/{portfolioId}/finance")]
[Authorize]
public class FinanceController : ControllerBase
{
    private readonly AppDbContext _context;
    public FinanceController(AppDbContext context) { _context = context; }

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> PortfolioBelongsToUser(int portfolioId) =>
        await _context.Portfolios.AnyAsync(p => p.Id == portfolioId && p.UserId == GetUserId());

    [HttpGet("balance")]
    public async Task<ActionResult<PortfolioBalance>> GetBalance(int portfolioId)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();

        var portfolio = await _context.Portfolios
            .Include(p => p.Items)
            .ThenInclude(i => i.Stock)
            .FirstOrDefaultAsync(p => p.Id == portfolioId);
        if (portfolio == null) return NotFound();

        var transactionCashBalance = await GetTransactionCashBalance(portfolioId);
        var cashBalance = transactionCashBalance + portfolio.CashBalanceAdjustment;

        return Ok(BuildBalance(portfolio, cashBalance));
    }

    [HttpPut("balance")]
    public async Task<ActionResult<PortfolioBalance>> UpdateBalance(int portfolioId, UpdatePortfolioBalanceDto dto)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();

        var portfolio = await _context.Portfolios
            .Include(p => p.Items)
            .ThenInclude(i => i.Stock)
            .FirstOrDefaultAsync(p => p.Id == portfolioId);
        if (portfolio == null) return NotFound();

        var transactionCashBalance = await GetTransactionCashBalance(portfolioId);
        var normalizedCashBalance = NormalizeMoney(dto.CashBalance);
        var normalizedBrokerCredit = NormalizeMoney(dto.BrokerCredit);

        portfolio.CashBalanceAdjustment = normalizedCashBalance - transactionCashBalance;
        portfolio.BrokerCredit = normalizedBrokerCredit;

        await _context.SaveChangesAsync();

        return Ok(BuildBalance(portfolio, normalizedCashBalance));
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<IEnumerable<Transaction>>> GetTransactions(int portfolioId)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();
        return await _context.Transactions
            .Where(t => t.PortfolioId == portfolioId)
            .Include(t => t.Stock)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    [HttpPost("transactions")]
    public async Task<ActionResult<Transaction>> CreateTransaction(int portfolioId, CreateTransactionDto dto)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();

        var validationError = await ValidateTransactionAsync(dto.Amount, dto.StockId, dto.CreatedAt);
        if (validationError != null) return validationError;

        // Derive signed amount from type (enforced server-side, positive user amount)
        var signedAmount = TransactionDirection.DeriveSignedAmount(dto.Type, dto.Amount);

        var transaction = new Transaction
        {
            PortfolioId = portfolioId,
            Type = dto.Type,
            Amount = dto.Amount > 0 ? dto.Amount : decimal.Abs(dto.Amount),
            SignedAmount = signedAmount,
            StockId = dto.StockId,
            Description = dto.Description,
            CreatedAt = NormalizeClientDateTime(dto.CreatedAt),
        };
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        if (transaction.StockId.HasValue)
            await _context.Entry(transaction).Reference(t => t.Stock).LoadAsync();

        return Ok(transaction);
    }

    [HttpPut("transactions/{id}")]
    public async Task<ActionResult<Transaction>> UpdateTransaction(int portfolioId, int id, UpdateTransactionDto dto)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();

        var transaction = await _context.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.PortfolioId == portfolioId);
        if (transaction == null) return NotFound();

        // Prevent editing order-linked transactions (they are auto-generated)
        if (transaction.OrderId.HasValue)
            return BadRequest("Order-linked transactions cannot be edited directly.");

        var validationError = await ValidateTransactionAsync(dto.Amount, dto.StockId, dto.CreatedAt);
        if (validationError != null) return validationError;

        var signedAmount = TransactionDirection.DeriveSignedAmount(dto.Type, dto.Amount);

        transaction.Type = dto.Type;
        transaction.Amount = dto.Amount > 0 ? dto.Amount : decimal.Abs(dto.Amount);
        transaction.SignedAmount = signedAmount;
        transaction.StockId = dto.StockId;
        transaction.Description = dto.Description;
        transaction.CreatedAt = NormalizeClientDateTime(dto.CreatedAt);

        await _context.SaveChangesAsync();

        if (transaction.StockId.HasValue)
            await _context.Entry(transaction).Reference(t => t.Stock).LoadAsync();

        return Ok(transaction);
    }

    [HttpDelete("transactions/{id}")]
    public async Task<IActionResult> DeleteTransaction(int portfolioId, int id)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();
        var transaction = await _context.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.PortfolioId == portfolioId);
        if (transaction == null) return NotFound();
        _context.Transactions.Remove(transaction);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<decimal> GetTransactionCashBalance(int portfolioId)
    {
        var transactions = await _context.Transactions
            .Where(t => t.PortfolioId == portfolioId)
            .ToListAsync();

        return transactions.Sum(t => t.GetEffectiveSignedAmount());
    }

    private PortfolioBalance BuildBalance(Portfolio portfolio, decimal cashBalance)
    {
        var stocksValue = portfolio.Items.Sum(i => i.Stock.CurrentPrice * i.Quantity);
        var totalBalance = cashBalance + portfolio.BrokerCredit;

        return new PortfolioBalance
        {
            CashBalance = cashBalance,
            BrokerCredit = portfolio.BrokerCredit,
            TotalBalance = totalBalance,
            StocksValue = stocksValue,
            TotalPortfolioValue = stocksValue + totalBalance,
        };
    }

    private async Task<ActionResult?> ValidateTransactionAsync(
        decimal amount,
        int? stockId,
        DateTime createdAt)
    {
        if (amount <= 0)
            return BadRequest("Amount must be greater than zero.");

        if (createdAt == default)
            return BadRequest("CreatedAt is required.");

        if (stockId.HasValue && !await _context.Stocks.AnyAsync(s => s.Id == stockId.Value))
            return BadRequest("Stock not found.");

        return null;
    }

    private static DateTime NormalizeClientDateTime(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return value;

        if (value.Kind == DateTimeKind.Local)
            return value.ToUniversalTime();

        return DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
    }

    private static decimal NormalizeMoney(decimal value) =>
        Math.Round(value, Portfolio.MonetaryScale, MidpointRounding.AwayFromZero);

    [HttpGet("dividends")]
    public async Task<ActionResult<IEnumerable<Dividend>>> GetDividends(int portfolioId)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();
        return await _context.Dividends
            .Where(d => d.PortfolioId == portfolioId)
            .Include(d => d.Stock)
            .OrderByDescending(d => d.PaidAt)
            .ToListAsync();
    }

    [HttpPost("dividends")]
    public async Task<ActionResult<Dividend>> CreateDividend(int portfolioId, CreateDividendDto dto)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();
        if (!await _context.Stocks.AnyAsync(s => s.Id == dto.StockId))
            return BadRequest("Stock not found");

        var dividend = new Dividend
        {
            PortfolioId = portfolioId,
            StockId = dto.StockId,
            Amount = dto.Amount,
            PaidAt = dto.PaidAt,
            CreatedAt = DateTime.UtcNow,
        };
        _context.Dividends.Add(dividend);
        await _context.SaveChangesAsync();
        await _context.Entry(dividend).Reference(d => d.Stock).LoadAsync();
        return Ok(dividend);
    }

    [HttpDelete("dividends/{id}")]
    public async Task<IActionResult> DeleteDividend(int portfolioId, int id)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();
        var dividend = await _context.Dividends
            .FirstOrDefaultAsync(d => d.Id == id && d.PortfolioId == portfolioId);
        if (dividend == null) return NotFound();
        _context.Dividends.Remove(dividend);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public class CreateTransactionDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? StockId { get; set; }
    public string? Description { get; set; }
}

public class UpdateTransactionDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? StockId { get; set; }
    public string? Description { get; set; }
}

public class UpdatePortfolioBalanceDto
{
    [Range(typeof(decimal), "-9999999999999999.99", "9999999999999999.99")]
    public decimal CashBalance { get; set; }

    [Range(typeof(decimal), "-9999999999999999.99", "9999999999999999.99")]
    public decimal BrokerCredit { get; set; }
}

public class CreateDividendDto
{
    public int StockId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}
