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

    /// <summary>Normalizes WKN/ISIN: trim whitespace, uppercase; blank becomes null.</summary>
    private static string? NormalizeIdentifier(string? value) => StockIdentifiers.Normalize(value);

    /// <summary>Validates WKN and ISIN formats. Returns a 400 result when invalid, otherwise null.</summary>
    private ActionResult? ValidateIdentifiers(string? wkn, string? isin)
    {
        if (wkn != null && !StockIdentifiers.IsValidWkn(wkn))
            return BadRequest("WKN должен содержать ровно 6 буквенно-цифровых символов (A–Z, 0–9).");
        if (isin != null && !StockIdentifiers.IsValidIsin(isin))
            return BadRequest("ISIN должен содержать ровно 12 символов: 2 буквы страны и 10 буквенно-цифровых символов (A–Z, 0–9).");
        return null;
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
    public async Task<ActionResult> GetHistory(int id, [FromQuery] string range = "5y", CancellationToken cancellationToken = default)
    {
        var stock = await _context.Stocks.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stock == null)
        {
            return NotFound();
        }

        var normalizedRange = (range ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedRange is not ("5y" or "3y" or "1y" or "6m" or "3m" or "1m" or "1w" or "24h" or "today"))
        {
            return BadRequest("Invalid range. Allowed values: 5y, 3y, 1y, 6m, 3m, 1m, 1w, 24h, today");
        }

        return Ok(await _stockHistoryService.GetHistoryAsync(stock, normalizedRange, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<Stock>> Create(Stock stock)
    {
        stock.Wkn = NormalizeIdentifier(stock.Wkn);
        stock.Isin = NormalizeIdentifier(stock.Isin);

        var validationError = ValidateIdentifiers(stock.Wkn, stock.Isin);
        if (validationError != null) return validationError;

        stock.UpdatedAt = DateTime.UtcNow;
        _context.Stocks.Add(stock);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return BadRequest(BuildDuplicateMessage(stock.Wkn, stock.Isin));
        }

        try
        {
            await _stockHistoryService.SyncHistoricalDataForStockAsync(stock, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stock created but history sync failed for stock {StockId}", stock.Id);
        }

        return CreatedAtAction(nameof(GetById), new { id = stock.Id }, stock);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Stock stock)
    {
        if (id != stock.Id) return BadRequest();

        stock.Wkn = NormalizeIdentifier(stock.Wkn);
        stock.Isin = NormalizeIdentifier(stock.Isin);

        var validationError = ValidateIdentifiers(stock.Wkn, stock.Isin);
        if (validationError != null) return validationError;

        var existing = await _context.Stocks.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Ticker = stock.Ticker;
        existing.Name = stock.Name;
        existing.Exchange = stock.Exchange;
        existing.CurrentPrice = stock.CurrentPrice;
        existing.Wkn = stock.Wkn;
        existing.Isin = stock.Isin;
        existing.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return BadRequest(BuildDuplicateMessage(stock.Wkn, stock.Isin));
        }

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

    private static bool IsDuplicateKeyException(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;

    private static string BuildDuplicateMessage(string? wkn, string? isin)
    {
        if (wkn != null && isin != null)
            return $"Акция с WKN «{wkn}» или ISIN «{isin}» уже существует.";
        if (wkn != null)
            return $"Акция с WKN «{wkn}» уже существует.";
        if (isin != null)
            return $"Акция с ISIN «{isin}» уже существует.";
        return "Акция с указанными идентификаторами уже существует.";
    }
}
