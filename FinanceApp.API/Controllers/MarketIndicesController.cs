using FinanceApp.API.Models;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/market-indices")]
[Authorize]
public class MarketIndicesController : ControllerBase
{
    private readonly AppDbContext _context;

    public MarketIndicesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MarketIndexDto>>> GetAll([FromQuery] bool includeArchived = false)
    {
        var marketIndices = await _context.MarketIndices
            .AsNoTracking()
            .Where(x => includeArchived || !x.IsArchived)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        return Ok(marketIndices.Select(MapMarketIndex));
    }

    [HttpPost]
    public async Task<ActionResult<MarketIndexDto>> CreateMarketIndex(UpsertMarketIndexRequest request)
    {
        var normalizedName = Normalize(request.Name);
        if (string.IsNullOrEmpty(normalizedName))
        {
            return BadRequest("Название индекса обязательно.");
        }

        var normalizedCode = Normalize(request.Code);
        if (string.IsNullOrEmpty(normalizedCode))
        {
            return BadRequest("Код индекса обязателен.");
        }

        if (await _context.MarketIndices.AnyAsync(x => x.NormalizedCode == normalizedCode))
        {
            return Conflict("Индекс с таким кодом уже существует.");
        }

        var now = DateTime.UtcNow;
        var marketIndex = new MarketIndex
        {
            Name = request.Name.Trim(),
            NormalizedName = normalizedName,
            Code = request.Code.Trim(),
            NormalizedCode = normalizedCode,
            Description = request.Description?.Trim() ?? string.Empty,
            CountryOrRegion = request.CountryOrRegion?.Trim() ?? string.Empty,
            SortOrder = request.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.MarketIndices.Add(marketIndex);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return Conflict("Индекс с таким кодом уже существует.");
        }

        return StatusCode(StatusCodes.Status201Created, await LoadMarketIndexAsync(marketIndex.Id));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MarketIndexDto>> UpdateMarketIndex(int id, UpsertMarketIndexRequest request)
    {
        var marketIndex = await _context.MarketIndices.FirstOrDefaultAsync(x => x.Id == id);
        if (marketIndex is null)
        {
            return NotFound("Индекс не найден.");
        }

        var normalizedName = Normalize(request.Name);
        if (string.IsNullOrEmpty(normalizedName))
        {
            return BadRequest("Название индекса обязательно.");
        }

        var normalizedCode = Normalize(request.Code);
        if (string.IsNullOrEmpty(normalizedCode))
        {
            return BadRequest("Код индекса обязателен.");
        }

        if (await _context.MarketIndices.AnyAsync(x => x.Id != id && x.NormalizedCode == normalizedCode))
        {
            return Conflict("Индекс с таким кодом уже существует.");
        }

        marketIndex.Name = request.Name.Trim();
        marketIndex.NormalizedName = normalizedName;
        marketIndex.Code = request.Code.Trim();
        marketIndex.NormalizedCode = normalizedCode;
        marketIndex.Description = request.Description?.Trim() ?? string.Empty;
        marketIndex.CountryOrRegion = request.CountryOrRegion?.Trim() ?? string.Empty;
        marketIndex.SortOrder = request.SortOrder;
        marketIndex.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return Conflict("Индекс с таким кодом уже существует.");
        }

        return Ok(await LoadMarketIndexAsync(marketIndex.Id));
    }

    [HttpPost("{id:int}/archive")]
    public async Task<ActionResult<MarketIndexDto>> ArchiveMarketIndex(int id)
    {
        var marketIndex = await _context.MarketIndices.FirstOrDefaultAsync(x => x.Id == id);
        if (marketIndex is null)
        {
            return NotFound("Индекс не найден.");
        }

        marketIndex.IsArchived = true;
        marketIndex.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(await LoadMarketIndexAsync(id));
    }

    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<MarketIndexDto>> RestoreMarketIndex(int id)
    {
        var marketIndex = await _context.MarketIndices.FirstOrDefaultAsync(x => x.Id == id);
        if (marketIndex is null)
        {
            return NotFound("Индекс не найден.");
        }

        marketIndex.IsArchived = false;
        marketIndex.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(await LoadMarketIndexAsync(id));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMarketIndex(int id)
    {
        var marketIndex = await _context.MarketIndices.FirstOrDefaultAsync(x => x.Id == id);
        if (marketIndex is null)
        {
            return NotFound("Индекс не найден.");
        }

        if (await _context.StockMarketIndices.AnyAsync(x => x.MarketIndexId == id))
        {
            return Conflict("Нельзя удалить индекс, к которому привязаны акции.");
        }

        _context.MarketIndices.Remove(marketIndex);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<MarketIndexDto> LoadMarketIndexAsync(int id)
    {
        var marketIndex = await _context.MarketIndices
            .AsNoTracking()
            .FirstAsync(x => x.Id == id);

        return MapMarketIndex(marketIndex);
    }

    private static MarketIndexDto MapMarketIndex(MarketIndex marketIndex)
        => new()
        {
            Id = marketIndex.Id,
            Name = marketIndex.Name,
            NormalizedName = marketIndex.NormalizedName,
            Code = marketIndex.Code,
            NormalizedCode = marketIndex.NormalizedCode,
            Description = marketIndex.Description,
            CountryOrRegion = marketIndex.CountryOrRegion,
            SortOrder = marketIndex.SortOrder,
            IsArchived = marketIndex.IsArchived,
            CreatedAt = marketIndex.CreatedAt,
            UpdatedAt = marketIndex.UpdatedAt
        };

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        for (var current = ex.InnerException; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
