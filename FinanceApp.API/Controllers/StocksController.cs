using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinanceApp.API.Services;
using FinanceApp.Data.Data;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StocksController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IStockHistoryService _stockHistoryService;
    private readonly ILogger<StocksController> _logger;
    public StocksController(
        AppDbContext context,
        IStockHistoryService stockHistoryService,
        ILogger<StocksController> logger)
    {
        _context = context;
        _stockHistoryService = stockHistoryService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Stock>>> GetAll()
        => await _context.Stocks.ToListAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<Stock>> GetById(int id)
    {
        var stock = await _context.Stocks.FindAsync(id);
        if (stock == null) return NotFound();
        return stock;
    }

    [HttpGet("{id}/history")]
    public async Task<ActionResult<IEnumerable<object>>> GetHistory(int id, [FromQuery] string range = "5y", CancellationToken cancellationToken = default)
    {
        var stock = await _context.Stocks.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stock == null)
        {
            return NotFound();
        }

        var normalizedRange = (range ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedRange is not ("5y" or "3y" or "1y" or "1w" or "24h" or "today"))
        {
            return BadRequest("Invalid range. Allowed values: 5y, 3y, 1y, 1w, 24h, today");
        }

        var data = await _stockHistoryService.GetHistoryAsync(id, normalizedRange, cancellationToken);
        if (data.Count == 0)
        {
            try
            {
                await _stockHistoryService.SyncHistoricalDataForStockAsync(stock, cancellationToken);
                data = await _stockHistoryService.GetHistoryAsync(id, normalizedRange, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "On-demand stock history sync failed for stock {StockId} ({Ticker})", stock.Id, stock.Ticker);
            }
        }

        return Ok(data.Select(x => new
        {
            x.Timestamp,
            x.Interval,
            x.Open,
            x.High,
            x.Low,
            x.Close,
            x.Volume
        }));
    }

    [HttpPost]
    public async Task<ActionResult<Stock>> Create(Stock stock)
    {
        stock.UpdatedAt = DateTime.UtcNow;
        _context.Stocks.Add(stock);
        await _context.SaveChangesAsync();

        try
        {
            await _stockHistoryService.SyncHistoricalDataForStockAsync(stock, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stock created but history sync failed for stock {StockId} ({Ticker})", stock.Id, stock.Ticker);
        }

        return CreatedAtAction(nameof(GetById), new { id = stock.Id }, stock);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Stock stock)
    {
        if (id != stock.Id) return BadRequest();
        stock.UpdatedAt = DateTime.UtcNow;
        _context.Entry(stock).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var stock = await _context.Stocks.FindAsync(id);
        if (stock == null) return NotFound();
        _context.Stocks.Remove(stock);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
