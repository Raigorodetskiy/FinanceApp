using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinanceApp.API.Models;
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

    /// <summary>Validates a finanzen.net slug. Returns a 400 result when invalid, otherwise null.</summary>
    private ActionResult? ValidateFinanzenNetSlug(string? slug)
    {
        if (slug is null)
        {
            return null;
        }

        if (!FinanzenNetQuoteService.IsValidSlug(slug))
        {
            return BadRequest("FinanzenNetSlug darf nur Kleinbuchstaben, Ziffern, Bindestriche und Unterstriche enthalten und muss mit einem Buchstaben oder einer Ziffer beginnen.");
        }

        return null;
    }

    private ActionResult? NormalizeAndValidateStock(Stock stock)
    {
        stock.Wkn = NormalizeIdentifier(stock.Wkn);
        stock.Isin = NormalizeIdentifier(stock.Isin);
        stock.FinanzenNetSlug = string.IsNullOrWhiteSpace(stock.FinanzenNetSlug)
            ? null
            : stock.FinanzenNetSlug.Trim();
        stock.Name = (stock.Name ?? string.Empty).Trim();
        stock.CommonName = string.IsNullOrWhiteSpace(stock.CommonName)
            ? stock.Name
            : stock.CommonName.Trim();

        if (!StockExchanges.TryNormalize(stock.Exchange, out var normalizedExchange))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(stock.Exchange)] = [$"Exchange must be one of: {string.Join(", ", StockExchanges.Supported)}."]
            }));
        }

        stock.Exchange = normalizedExchange;

        var slugError = ValidateFinanzenNetSlug(stock.FinanzenNetSlug);
        if (slugError is not null)
        {
            return slugError;
        }

        return ValidateIdentifiers(stock.Wkn, stock.Isin);
    }

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
        => await _context.Stocks
            .Include(s => s.Industry)
            .ThenInclude(i => i!.Sector)
            .ToListAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<Stock>> GetById(int id)
    {
        var stock = await _context.Stocks
            .Include(s => s.Industry)
            .ThenInclude(i => i!.Sector)
            .FirstOrDefaultAsync(s => s.Id == id);
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


    [HttpPost("{id}/history/refresh")]
    public async Task<ActionResult<StockHistoryRefreshResponse>> RefreshHistory(int id, CancellationToken cancellationToken = default)
    {
        var stock = await _context.Stocks.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stock == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(stock.Ticker))
        {
            return BadRequest("У акции должен быть указан тикер для перезагрузки истории.");
        }

        if (!StockExchanges.TryNormalize(stock.Exchange, out var normalizedExchange))
        {
            return BadRequest("У акции указана некорректная биржа для перезагрузки истории.");
        }

        stock.Exchange = normalizedExchange;

        try
        {
            return Ok(await _stockHistoryService.RefreshHistoryAsync(stock, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    public async Task<ActionResult<Stock>> Create(Stock stock)
    {
        var validationError = NormalizeAndValidateStock(stock);
        if (validationError != null) return validationError;

        var (industryValidationError, _) = await ValidateIndustryAssignmentAsync(stock.IndustryId);
        if (industryValidationError != null) return industryValidationError;

        stock.UpdatedAt = DateTime.UtcNow;
        stock.Industry = null;
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

        var createdStock = await LoadStockWithClassificationAsync(stock.Id);
        return CreatedAtAction(nameof(GetById), new { id = stock.Id }, createdStock);
    }

    /// <summary>
    /// Legacy full-object update endpoint. Returns 410 Gone — callers must migrate to
    /// PUT /api/Stocks/{id}/metadata (editable fields) and PATCH /api/Stocks/{id}/quote (price).
    /// </summary>
    [HttpPut("{id}")]
    public IActionResult Update(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        var userAgent = Request.Headers.UserAgent.ToString();
        _logger.LogWarning(
            "Legacy PUT /api/Stocks/{StockId} rejected (410 Gone). UserId={UserId} UserAgent={UserAgent}",
            id, userId, userAgent);

        return StatusCode(StatusCodes.Status410Gone,
            "PUT /api/Stocks/{id} has been retired. " +
            "Use PUT /api/Stocks/{id}/metadata to update editable fields " +
            "and PATCH /api/Stocks/{id}/quote to update quote data.");
    }

    /// <summary>
    /// Updates editable metadata fields for an existing stock.
    /// Ticker and Exchange are identity fields and cannot be changed after creation.
    /// </summary>
    [HttpPut("{id}/metadata")]
    public async Task<IActionResult> UpdateMetadata(int id, UpdateStockMetadataRequest request)
    {
        var existing = await _context.Stocks
            .Include(s => s.Industry)
            .ThenInclude(i => i!.Sector)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (existing == null) return NotFound();

        // Normalize fields the same way as Create
        var wkn = NormalizeIdentifier(request.Wkn);
        var isin = NormalizeIdentifier(request.Isin);
        var finanzenNetSlug = string.IsNullOrWhiteSpace(request.FinanzenNetSlug)
            ? null
            : request.FinanzenNetSlug.Trim();
        var name = (request.Name ?? string.Empty).Trim();
        var commonName = string.IsNullOrWhiteSpace(request.CommonName) ? name : request.CommonName.Trim();

        var slugError = ValidateFinanzenNetSlug(finanzenNetSlug);
        if (slugError is not null) return slugError;

        var identifierError = ValidateIdentifiers(wkn, isin);
        if (identifierError is not null) return identifierError;

        var (industryValidationError, _) = await ValidateIndustryAssignmentAsync(request.IndustryId, existing.IndustryId);
        if (industryValidationError != null) return industryValidationError;

        existing.Name = name;
        existing.CommonName = commonName;
        existing.Wkn = wkn;
        existing.Isin = isin;
        existing.FinanzenNetSlug = finanzenNetSlug;
        existing.IndustryId = request.IndustryId;
        existing.UpdatedAt = DateTime.UtcNow;

        // Manual price edit: clear stale snapshot fields so the UI never shows outdated
        // change/timestamp alongside a manually entered price.
        existing.CurrentPrice = request.CurrentPrice;
        existing.CurrentPriceChange = null;
        existing.CurrentPriceChangePercent = null;
        existing.CurrentPriceAt = null;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return BadRequest(BuildDuplicateMessage(wkn, isin));
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        var userAgent = Request.Headers.UserAgent.ToString();
        _logger.LogInformation(
            "Stock metadata updated. StockId={StockId} Ticker={Ticker} Exchange={Exchange} UserId={UserId} UserAgent={UserAgent}",
            id, existing.Ticker, existing.Exchange, userId, userAgent);

        return NoContent();
    }

    [HttpPatch("{id}/quote")]
    public async Task<IActionResult> UpdateQuote(int id, UpdateStockQuoteRequest request)
    {
        var existing = await _context.Stocks.FindAsync(id);
        if (existing == null) return NotFound();

        if (request.CurrentPriceAt.HasValue &&
            existing.CurrentPriceAt.HasValue &&
            request.CurrentPriceAt.Value < existing.CurrentPriceAt.Value)
        {
            _logger.LogInformation(
                "Skipping stale stock quote update. StockId={StockId} ExistingPriceAt={ExistingPriceAt} IncomingPriceAt={IncomingPriceAt}",
                id,
                existing.CurrentPriceAt.Value,
                request.CurrentPriceAt.Value);
            return NoContent();
        }

        existing.CurrentPrice = request.CurrentPrice;
        existing.CurrentPriceChange = request.CurrentPriceChange;
        existing.CurrentPriceChangePercent = request.CurrentPriceChangePercent;
        existing.CurrentPriceAt = request.CurrentPriceAt;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var stock = await _context.Stocks.FindAsync(id);
        if (stock == null) return NotFound();

        var isReferenced = await _context.PortfolioItems.AnyAsync(item => item.StockId == id);
        if (isReferenced)
        {
            return Conflict("Невозможно удалить акцию: она используется как минимум в одном портфеле.");
        }

        _context.Stocks.Remove(stock);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<Stock> LoadStockWithClassificationAsync(int id)
        => await _context.Stocks
            .Include(s => s.Industry)
            .ThenInclude(i => i!.Sector)
            .FirstAsync(s => s.Id == id);

    private async Task<(ActionResult? Error, Industry? Industry)> ValidateIndustryAssignmentAsync(int? industryId, int? currentIndustryId = null)
    {
        if (industryId is null)
        {
            return (null, null);
        }

        var industry = await _context.Industries
            .Include(i => i.Sector)
            .FirstOrDefaultAsync(i => i.Id == industryId.Value);

        if (industry is null)
        {
            return (BadRequest("Указанная отрасль не найдена."), null);
        }

        // Allow existing archived bindings to remain unchanged during metadata edits.
        if (industry.Id == currentIndustryId)
        {
            return (null, industry);
        }

        if (industry.IsArchived)
        {
            return (BadRequest("Нельзя привязать акцию к архивной отрасли."), null);
        }

        if (industry.Sector.IsArchived)
        {
            return (BadRequest("Нельзя привязать акцию к отрасли из архивного сектора."), null);
        }

        return (null, industry);
    }

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
