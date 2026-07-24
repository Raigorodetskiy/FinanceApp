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
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    [HttpPost("transactions")]
    public async Task<ActionResult<Transaction>> CreateTransaction(int portfolioId, CreateTransactionDto dto)
    {
        if (!await PortfolioBelongsToUser(portfolioId)) return NotFound();

        var signedAmount = TransactionDirection.ResolveSignedAmount(dto.Type, dto.Amount, dto.SignedAmount);
        var transaction = new Transaction
        {
            PortfolioId = portfolioId,
            Description = dto.Description,
            CreatedAt = DateTime.UtcNow,
        };
        transaction.ApplySignedAmount(signedAmount, dto.Type);
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();
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
    public decimal? SignedAmount { get; set; }
    public string? Description { get; set; }
}

public class CreateDividendDto
{
    public int StockId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}
