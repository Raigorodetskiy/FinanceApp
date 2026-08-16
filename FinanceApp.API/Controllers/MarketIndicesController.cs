using FinanceApp.API.Models;
using FinanceApp.API.Services;
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
    private readonly IMarketIndexHistoryService _historyService;
    private readonly IIndexConstituentsProvider _constituentsProvider;

    public MarketIndicesController(
        AppDbContext context,
        IMarketIndexHistoryService historyService,
        IIndexConstituentsProvider constituentsProvider)
    {
        _context = context;
        _historyService = historyService;
        _constituentsProvider = constituentsProvider;
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
        var normalizedProviderSymbol = NormalizeProviderSymbol(request.ProviderSymbol);
        if (normalizedProviderSymbol is not null && !MarketIndexHistoryService.IsValidProviderSymbol(normalizedProviderSymbol))
        {
            return BadRequest("Символ поставщика содержит недопустимые символы.");
        }

        var marketIndex = new MarketIndex
        {
            Name = request.Name.Trim(),
            NormalizedName = normalizedName,
            Code = request.Code.Trim(),
            NormalizedCode = normalizedCode,
            ProviderSymbol = normalizedProviderSymbol,
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

        var newProviderSymbol = NormalizeProviderSymbol(request.ProviderSymbol);
        if (newProviderSymbol is not null && !MarketIndexHistoryService.IsValidProviderSymbol(newProviderSymbol))
        {
            return BadRequest("Символ поставщика содержит недопустимые символы.");
        }

        var symbolChanged = !string.Equals(
            marketIndex.ProviderSymbol, newProviderSymbol, StringComparison.OrdinalIgnoreCase);

        marketIndex.Name = request.Name.Trim();
        marketIndex.NormalizedName = normalizedName;
        marketIndex.Code = request.Code.Trim();
        marketIndex.NormalizedCode = normalizedCode;
        marketIndex.ProviderSymbol = newProviderSymbol;
        marketIndex.Description = request.Description?.Trim() ?? string.Empty;
        marketIndex.CountryOrRegion = request.CountryOrRegion?.Trim() ?? string.Empty;
        marketIndex.SortOrder = request.SortOrder;
        marketIndex.UpdatedAt = DateTime.UtcNow;

        // When ProviderSymbol changes, invalidate old history to prevent mixing data from different symbols
        if (symbolChanged)
        {
            var oldHistory = await _context.MarketIndexHistoricalPrices
                .Where(x => x.MarketIndexId == id)
                .ToListAsync();
            if (oldHistory.Count > 0)
            {
                _context.MarketIndexHistoricalPrices.RemoveRange(oldHistory);
            }
        }

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

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<MarketIndexHistoryResponse>> GetHistory(
        int id,
        [FromQuery] string range = "1y",
        CancellationToken cancellationToken = default)
    {
        var marketIndex = await _context.MarketIndices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (marketIndex is null)
        {
            return NotFound("Индекс не найден.");
        }

        if (string.IsNullOrWhiteSpace(marketIndex.ProviderSymbol))
        {
            return UnprocessableEntity("Символ поставщика не указан для этого индекса. Укажите ProviderSymbol в настройках индекса.");
        }

        string normalizedRange;
        try
        {
            normalizedRange = MarketIndexHistoryService.NormalizeRange(range);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        try
        {
            var result = await _historyService.GetHistoryAsync(marketIndex, normalizedRange, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return StatusCode(502, "Не удалось загрузить исторические данные. Попробуйте позже.");
        }
    }

    [HttpPost("{id:int}/history/refresh")]
    public async Task<ActionResult<MarketIndexRefreshResponse>> RefreshHistory(
        int id,
        CancellationToken cancellationToken = default)
    {
        var marketIndex = await _context.MarketIndices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (marketIndex is null)
        {
            return NotFound("Индекс не найден.");
        }

        if (string.IsNullOrWhiteSpace(marketIndex.ProviderSymbol))
        {
            return UnprocessableEntity("Символ поставщика не указан. Укажите ProviderSymbol для обновления истории.");
        }

        if (marketIndex.IsArchived)
        {
            return Conflict("Нельзя обновлять историю архивного индекса через этот endpoint. Снимите индекс с архива.");
        }

        try
        {
            var result = await _historyService.RefreshHistoryAsync(marketIndex, "5y", cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return StatusCode(502, "Не удалось обновить исторические данные. Попробуйте позже.");
        }
    }

    [HttpGet("{id:int}/constituents")]
    public async Task<ActionResult<IndexConstituentsResponse>> GetConstituents(
        int id,
        [FromQuery] bool includeFormer = false,
        CancellationToken cancellationToken = default)
    {
        var marketIndex = await _context.MarketIndices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (marketIndex is null) return NotFound("Индекс не найден.");

        var membershipsQuery = _context.StockMarketIndices
            .Include(x => x.Stock)
            .Where(x => x.MarketIndexId == id);

        if (!includeFormer)
        {
            membershipsQuery = membershipsQuery.Where(x => x.EffectiveTo == null);
        }

        var memberships = await membershipsQuery
            .AsNoTracking()
            .OrderBy(x => x.Stock.Name)
            .ToListAsync(cancellationToken);

        var dtos = memberships.Select(x => new IndexConstituentDto
        {
            StockId = x.StockId,
            Ticker = x.Stock.Ticker,
            ProviderSymbol = x.Stock.ProviderSymbol,
            Name = x.Stock.Name,
            Exchange = x.Stock.Exchange,
            Isin = x.Stock.Isin,
            TrackingStatus = x.Stock.TrackingStatus.ToString(),
            Source = x.Source,
            ProviderConstituentKey = x.ProviderConstituentKey,
            EffectiveFrom = x.EffectiveFrom,
            EffectiveTo = x.EffectiveTo,
            LastVerifiedAt = x.LastVerifiedAt,
            ImportedAt = x.ImportedAt,
        }).ToList();

        return Ok(new IndexConstituentsResponse
        {
            MarketIndexId = id,
            IndexName = marketIndex.Name,
            TotalCount = dtos.Count,
            Constituents = dtos,
        });
    }

    [HttpPost("{id:int}/constituents/refresh")]
    public async Task<ActionResult<IndexConstituentsRefreshResponse>> RefreshConstituents(
        int id,
        CancellationToken cancellationToken = default)
    {
        var marketIndex = await _context.MarketIndices
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (marketIndex is null) return NotFound("Индекс не найден.");

        if (marketIndex.IsArchived)
        {
            return Conflict("Нельзя обновлять состав архивного индекса.");
        }

        var providerResult = await _constituentsProvider.GetConstituentsAsync(marketIndex, cancellationToken);

        if (providerResult.Status == IndexConstituentsStatus.Unsupported)
        {
            return UnprocessableEntity(new IndexConstituentsRefreshResponse
            {
                MarketIndexId = id,
                ProviderStatus = providerResult.Status.ToString(),
                ProviderMessage = providerResult.Message,
            });
        }

        if (providerResult.Status == IndexConstituentsStatus.RateLimited)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new IndexConstituentsRefreshResponse
            {
                MarketIndexId = id,
                ProviderStatus = providerResult.Status.ToString(),
                ProviderMessage = providerResult.Message,
            });
        }

        if (providerResult.Status == IndexConstituentsStatus.ProviderFailure)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new IndexConstituentsRefreshResponse
            {
                MarketIndexId = id,
                ProviderStatus = providerResult.Status.ToString(),
                ProviderMessage = providerResult.Message,
            });
        }

        // Partial result: update/add but do NOT close missing memberships.
        var isFullSnapshot = providerResult.Status == IndexConstituentsStatus.Success;

        var now = providerResult.FetchedAt;
        int added = 0, updated = 0, unchanged = 0, closed = 0;

        // Load existing current memberships for this index.
        var existingMemberships = await _context.StockMarketIndices
            .Include(x => x.Stock)
            .Where(x => x.MarketIndexId == id && x.EffectiveTo == null)
            .ToListAsync(cancellationToken);

        var existingByProviderKey = existingMemberships
            .Where(x => x.ProviderConstituentKey != null)
            .ToDictionary(x => x.ProviderConstituentKey!);

        var seenStockIds = new HashSet<int>();

        foreach (var constituent in providerResult.Constituents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Deduplication: try by provider symbol first, then ticker+exchange.
            Stock? stock = null;

            if (!string.IsNullOrWhiteSpace(constituent.Isin))
            {
                stock = await _context.Stocks
                    .FirstOrDefaultAsync(s => s.Isin == constituent.Isin, cancellationToken);
            }

            if (stock is null && !string.IsNullOrWhiteSpace(constituent.ProviderSymbol))
            {
                stock = await _context.Stocks
                    .FirstOrDefaultAsync(s => s.ProviderSymbol == constituent.ProviderSymbol, cancellationToken);
            }

            if (stock is null)
            {
                stock = await _context.Stocks
                    .FirstOrDefaultAsync(
                        s => s.Ticker == constituent.Ticker && s.Exchange == (constituent.ProviderExchange ?? s.Exchange),
                        cancellationToken);
            }

            if (stock is null)
            {
                // Create new CatalogOnly record.
                stock = new Stock
                {
                    Ticker = constituent.Ticker,
                    Name = constituent.CompanyName,
                    CommonName = constituent.CompanyName,
                    Exchange = StockExchanges.TryNormalize(constituent.ProviderExchange ?? string.Empty, out var ex)
                        ? ex
                        : StockExchanges.Nyse,
                    Isin = StockIdentifiers.Normalize(constituent.Isin),
                    ProviderSymbol = constituent.ProviderSymbol,
                    TrackingStatus = StockTrackingStatus.CatalogOnly,
                    UpdatedAt = now,
                };
                _context.Stocks.Add(stock);
                await _context.SaveChangesAsync(cancellationToken);
                added++;
            }

            seenStockIds.Add(stock.Id);

            // Find or create current membership.
            var membership = existingMemberships.FirstOrDefault(m => m.StockId == stock.Id);
            if (membership is null)
            {
                membership = new StockMarketIndex
                {
                    StockId = stock.Id,
                    MarketIndexId = id,
                    Source = _constituentsProvider.ProviderName,
                    ProviderConstituentKey = constituent.ProviderSymbol,
                    EffectiveFrom = now,
                    ImportedAt = now,
                    LastVerifiedAt = now,
                };
                _context.StockMarketIndices.Add(membership);
                if (stock.Id != 0) added++; // stock was pre-existing
            }
            else
            {
                membership.LastVerifiedAt = now;
                unchanged++;
            }
        }

        // For full snapshots, close memberships for stocks no longer in the list.
        if (isFullSnapshot)
        {
            foreach (var m in existingMemberships.Where(m => !seenStockIds.Contains(m.StockId)))
            {
                m.EffectiveTo = now;
                closed++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new IndexConstituentsRefreshResponse
        {
            MarketIndexId = id,
            ProviderStatus = providerResult.Status.ToString(),
            ProviderMessage = providerResult.Message,
            Added = added,
            Updated = updated,
            Unchanged = unchanged,
            Closed = closed,
        });
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
            ProviderSymbol = marketIndex.ProviderSymbol,
            Description = marketIndex.Description,
            CountryOrRegion = marketIndex.CountryOrRegion,
            SortOrder = marketIndex.SortOrder,
            IsArchived = marketIndex.IsArchived,
            CreatedAt = marketIndex.CreatedAt,
            UpdatedAt = marketIndex.UpdatedAt
        };

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeProviderSymbol(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
