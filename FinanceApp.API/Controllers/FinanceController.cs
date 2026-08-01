using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinanceApp.Data.Data;
using FinanceApp.Core.Models;
using System.Security.Claims;
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

        var transactions = await _context.Transactions
            .Where(t => t.PortfolioId == portfolioId)
            .ToListAsync();

        var cashBalance = transactions
            .Sum(t => t.GetEffectiveSignedAmount());

        var stocksValue = portfolio.Items
            .Sum(i => i.Stock.CurrentPrice * i.Quantity);

        var balance = new PortfolioBalance
        {
            CashBalance = cashBalance,
            BrokerCredit = portfolio.BrokerCredit,
            TotalBalance = cashBalance + portfolio.BrokerCredit,
            StocksValue = stocksValue,
            TotalPortfolioValue = stocksValue + cashBalance,
        };

        return Ok(balance);
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

        // Validate stock for types that require it
        if (dto.Type is TransactionType.Buy or TransactionType.Sell or TransactionType.Dividend)
        {
            if (dto.StockId == null)
                return BadRequest("StockId is required for Buy, Sell, and Dividend transactions.");
            if (!await _context.Stocks.AnyAsync(s => s.Id == dto.StockId))
                return BadRequest("Stock not found.");
        }

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
            CreatedAt = DateTime.UtcNow,
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

        if (dto.Type is TransactionType.Buy or TransactionType.Sell or TransactionType.Dividend)
        {
            if (dto.StockId == null)
                return BadRequest("StockId is required for Buy, Sell, and Dividend transactions.");
            if (!await _context.Stocks.AnyAsync(s => s.Id == dto.StockId))
                return BadRequest("Stock not found.");
        }

        var signedAmount = TransactionDirection.DeriveSignedAmount(dto.Type, dto.Amount);

        transaction.Type = dto.Type;
        transaction.Amount = dto.Amount > 0 ? dto.Amount : decimal.Abs(dto.Amount);
        transaction.SignedAmount = signedAmount;
        transaction.StockId = dto.StockId;
        transaction.Description = dto.Description;

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
    public int? StockId { get; set; }
    public string? Description { get; set; }
}

public class UpdateTransactionDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public int? StockId { get; set; }
    public string? Description { get; set; }
}

public class CreateDividendDto
{
    public int StockId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}
