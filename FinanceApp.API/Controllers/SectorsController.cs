using FinanceApp.API.Models;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SectorsController : ControllerBase
{
    private readonly AppDbContext _context;

    public SectorsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SectorTreeItemDto>>> GetAll([FromQuery] bool includeArchived = false)
    {
        var sectors = await _context.Sectors
            .AsNoTracking()
            .Where(x => includeArchived || !x.IsArchived)
            .Include(x => x.Industries.Where(i => includeArchived || !i.IsArchived))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var stockCounts = await LoadStockCountsAsync(sectors.SelectMany(x => x.Industries).Select(x => x.Id));

        return Ok(sectors.Select(x => MapSector(x, stockCounts)));
    }

    [HttpPost]
    public async Task<ActionResult<SectorTreeItemDto>> CreateSector(UpsertSectorRequest request)
    {
        var normalized = NormalizeReferenceName(request.Name);
        if (string.IsNullOrEmpty(normalized))
        {
            return BadRequest("Название сектора обязательно.");
        }

        if (await _context.Sectors.AnyAsync(x => x.NormalizedName == normalized))
        {
            return Conflict("Сектор с таким названием уже существует.");
        }

        var now = DateTime.UtcNow;
        var sector = new Sector
        {
            Name = request.Name.Trim(),
            NormalizedName = normalized,
            SortOrder = request.SortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.Sectors.Add(sector);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return Conflict("Сектор с таким названием уже существует.");
        }

        return StatusCode(StatusCodes.Status201Created, await LoadSectorAsync(sector.Id));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SectorTreeItemDto>> UpdateSector(int id, UpsertSectorRequest request)
    {
        var sector = await _context.Sectors.FirstOrDefaultAsync(x => x.Id == id);
        if (sector is null)
        {
            return NotFound("Сектор не найден.");
        }

        var normalized = NormalizeReferenceName(request.Name);
        if (string.IsNullOrEmpty(normalized))
        {
            return BadRequest("Название сектора обязательно.");
        }

        if (await _context.Sectors.AnyAsync(x => x.Id != id && x.NormalizedName == normalized))
        {
            return Conflict("Сектор с таким названием уже существует.");
        }

        sector.Name = request.Name.Trim();
        sector.NormalizedName = normalized;
        sector.SortOrder = request.SortOrder;
        sector.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return Conflict("Сектор с таким названием уже существует.");
        }

        return Ok(await LoadSectorAsync(sector.Id));
    }

    [HttpPost("{id:int}/archive")]
    public async Task<ActionResult<SectorTreeItemDto>> ArchiveSector(int id)
    {
        var sector = await _context.Sectors.FirstOrDefaultAsync(x => x.Id == id);
        if (sector is null)
        {
            return NotFound("Сектор не найден.");
        }

        sector.IsArchived = true;
        sector.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(await LoadSectorAsync(id));
    }

    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<SectorTreeItemDto>> RestoreSector(int id)
    {
        var sector = await _context.Sectors.FirstOrDefaultAsync(x => x.Id == id);
        if (sector is null)
        {
            return NotFound("Сектор не найден.");
        }

        sector.IsArchived = false;
        sector.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(await LoadSectorAsync(id));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteSector(int id)
    {
        var sector = await _context.Sectors.FirstOrDefaultAsync(x => x.Id == id);
        if (sector is null)
        {
            return NotFound("Сектор не найден.");
        }

        if (await _context.Industries.AnyAsync(x => x.SectorId == id))
        {
            return Conflict("Нельзя удалить сектор, в котором есть отрасли.");
        }

        _context.Sectors.Remove(sector);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{sectorId:int}/industries")]
    public async Task<ActionResult<IndustryTreeItemDto>> CreateIndustry(int sectorId, UpsertIndustryRequest request)
    {
        var sector = await _context.Sectors.FirstOrDefaultAsync(x => x.Id == sectorId);
        if (sector is null)
        {
            return NotFound("Сектор не найден.");
        }

        if (sector.IsArchived)
        {
            return Conflict("Нельзя создать отрасль в архивном секторе.");
        }

        var normalized = NormalizeReferenceName(request.Name);
        if (string.IsNullOrEmpty(normalized))
        {
            return BadRequest("Название отрасли обязательно.");
        }

        if (await _context.Industries.AnyAsync(x => x.SectorId == sectorId && x.NormalizedName == normalized))
        {
            return Conflict("Отрасль с таким названием уже существует в этом секторе.");
        }

        var now = DateTime.UtcNow;
        var industry = new Industry
        {
            SectorId = sectorId,
            Name = request.Name.Trim(),
            NormalizedName = normalized,
            SortOrder = request.SortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.Industries.Add(industry);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return Conflict("Отрасль с таким названием уже существует в этом секторе.");
        }

        return StatusCode(StatusCodes.Status201Created, await LoadIndustryAsync(sectorId, industry.Id));
    }

    [HttpPut("{sectorId:int}/industries/{industryId:int}")]
    public async Task<ActionResult<IndustryTreeItemDto>> UpdateIndustry(int sectorId, int industryId, UpsertIndustryRequest request)
    {
        var industry = await _context.Industries
            .Include(x => x.Sector)
            .FirstOrDefaultAsync(x => x.Id == industryId && x.SectorId == sectorId);
        if (industry is null)
        {
            return NotFound("Отрасль не найдена.");
        }

        var normalized = NormalizeReferenceName(request.Name);
        if (string.IsNullOrEmpty(normalized))
        {
            return BadRequest("Название отрасли обязательно.");
        }

        if (await _context.Industries.AnyAsync(x => x.Id != industryId && x.SectorId == sectorId && x.NormalizedName == normalized))
        {
            return Conflict("Отрасль с таким названием уже существует в этом секторе.");
        }

        industry.Name = request.Name.Trim();
        industry.NormalizedName = normalized;
        industry.SortOrder = request.SortOrder;
        industry.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return Conflict("Отрасль с таким названием уже существует в этом секторе.");
        }

        return Ok(await LoadIndustryAsync(industry.SectorId, industry.Id));
    }

    [HttpPost("{sectorId:int}/industries/{industryId:int}/archive")]
    public async Task<ActionResult<IndustryTreeItemDto>> ArchiveIndustry(int sectorId, int industryId)
    {
        var industry = await _context.Industries.FirstOrDefaultAsync(x => x.Id == industryId && x.SectorId == sectorId);
        if (industry is null)
        {
            return NotFound("Отрасль не найдена.");
        }

        industry.IsArchived = true;
        industry.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(await LoadIndustryAsync(sectorId, industryId));
    }

    [HttpPost("{sectorId:int}/industries/{industryId:int}/restore")]
    public async Task<ActionResult<IndustryTreeItemDto>> RestoreIndustry(int sectorId, int industryId)
    {
        var industry = await _context.Industries
            .Include(x => x.Sector)
            .FirstOrDefaultAsync(x => x.Id == industryId && x.SectorId == sectorId);
        if (industry is null)
        {
            return NotFound("Отрасль не найдена.");
        }

        if (industry.Sector.IsArchived)
        {
            return Conflict("Нельзя восстановить отрасль в архивном секторе.");
        }

        industry.IsArchived = false;
        industry.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(await LoadIndustryAsync(sectorId, industryId));
    }

    [HttpDelete("{sectorId:int}/industries/{industryId:int}")]
    public async Task<IActionResult> DeleteIndustry(int sectorId, int industryId)
    {
        var industry = await _context.Industries.FirstOrDefaultAsync(x => x.Id == industryId && x.SectorId == sectorId);
        if (industry is null)
        {
            return NotFound("Отрасль не найдена.");
        }

        if (await _context.Stocks.AnyAsync(x => x.IndustryId == industryId))
        {
            return Conflict("Нельзя удалить отрасль, к которой привязаны акции.");
        }

        _context.Industries.Remove(industry);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{sectorId:int}/industries/{industryId:int}/move")]
    public async Task<ActionResult<IndustryTreeItemDto>> MoveIndustry(int sectorId, int industryId, MoveIndustryRequest request)
    {
        var industry = await _context.Industries
            .Include(x => x.Sector)
            .FirstOrDefaultAsync(x => x.Id == industryId && x.SectorId == sectorId);
        if (industry is null)
        {
            return NotFound("Отрасль не найдена.");
        }

        if (request.TargetSectorId == sectorId)
        {
            return BadRequest("Новый сектор должен отличаться от текущего.");
        }

        var targetSector = await _context.Sectors.FirstOrDefaultAsync(x => x.Id == request.TargetSectorId);
        if (targetSector is null)
        {
            return NotFound("Целевой сектор не найден.");
        }

        if (targetSector.IsArchived)
        {
            return Conflict("Нельзя переместить отрасль в архивный сектор.");
        }

        if (await _context.Industries.AnyAsync(x =>
                x.Id != industryId &&
                x.SectorId == request.TargetSectorId &&
                x.NormalizedName == industry.NormalizedName))
        {
            return Conflict("В целевом секторе уже есть отрасль с таким названием.");
        }

        industry.SectorId = request.TargetSectorId;
        industry.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            return Conflict("В целевом секторе уже есть отрасль с таким названием.");
        }

        return Ok(await LoadIndustryAsync(industry.SectorId, industry.Id));
    }

    // Post-mutation helper: always loads all industries (including archived)
    // so the caller receives the full, consistent state after an operation.
    private async Task<SectorTreeItemDto> LoadSectorAsync(int sectorId)
    {
        var sector = await _context.Sectors
            .AsNoTracking()
            .Include(x => x.Industries)
            .FirstAsync(x => x.Id == sectorId);

        var stockCounts = await LoadStockCountsAsync(sector.Industries.Select(x => x.Id));

        return MapSector(sector, stockCounts);
    }

    private async Task<IndustryTreeItemDto> LoadIndustryAsync(int sectorId, int industryId)
    {
        var industry = await _context.Industries
            .AsNoTracking()
            .FirstAsync(x => x.Id == industryId && x.SectorId == sectorId);

        var stockCounts = await LoadStockCountsAsync(new[] { industry.Id });

        return MapIndustry(industry, stockCounts);
    }

    private async Task<Dictionary<int, int>> LoadStockCountsAsync(IEnumerable<int> industryIds)
    {
        var ids = industryIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, int>();
        }

        return await _context.Stocks
            .AsNoTracking()
            .Where(x => x.IndustryId.HasValue && ids.Contains(x.IndustryId.Value))
            .GroupBy(x => x.IndustryId!.Value)
            .Select(x => new { IndustryId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.IndustryId, x => x.Count);
    }

    private static SectorTreeItemDto MapSector(Sector sector, IReadOnlyDictionary<int, int> stockCounts)
    {
        var industries = sector.Industries
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => MapIndustry(x, stockCounts))
            .ToArray();

        return new SectorTreeItemDto
        {
            Id = sector.Id,
            Name = sector.Name,
            NormalizedName = sector.NormalizedName,
            IsArchived = sector.IsArchived,
            SortOrder = sector.SortOrder,
            IndustryCount = industries.Length,
            StockCount = industries.Sum(x => x.StockCount),
            CreatedAtUtc = sector.CreatedAtUtc,
            UpdatedAtUtc = sector.UpdatedAtUtc,
            Industries = industries
        };
    }

    private static IndustryTreeItemDto MapIndustry(Industry industry, IReadOnlyDictionary<int, int> stockCounts)
        => new()
        {
            Id = industry.Id,
            SectorId = industry.SectorId,
            Name = industry.Name,
            NormalizedName = industry.NormalizedName,
            IsArchived = industry.IsArchived,
            SortOrder = industry.SortOrder,
            StockCount = stockCounts.GetValueOrDefault(industry.Id),
            CreatedAtUtc = industry.CreatedAtUtc,
            UpdatedAtUtc = industry.UpdatedAtUtc
        };

    private static string NormalizeReferenceName(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static bool IsDuplicateKeyException(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
}
