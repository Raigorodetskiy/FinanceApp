using System.Collections.Concurrent;
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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> RefreshLocks = new();
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> BatchHistoryRefreshLocks = new();
    private const int MaxBatchResultDetails = 200;

    private readonly AppDbContext _context;
    private readonly IMarketIndexHistoryService _historyService;
    private readonly IIndexConstituentsProvider _constituentsProvider;
    private readonly IStockHistoryService _stockHistoryService;
    private readonly ILogger<MarketIndicesController> _logger;

    public MarketIndicesController(
        AppDbContext context,
        IMarketIndexHistoryService historyService,
        IIndexConstituentsProvider constituentsProvider,
        IStockHistoryService stockHistoryService,
        ILogger<MarketIndicesController> logger)
    {
        _context = context;
        _historyService = historyService;
        _constituentsProvider = constituentsProvider;
        _stockHistoryService = stockHistoryService;
        _logger = logger;
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
            CommonName = x.Stock.CommonName,
            Exchange = x.Stock.Exchange,
            Isin = x.Stock.Isin,
            Wkn = x.Stock.Wkn,
            FinanzenNetSlug = x.Stock.FinanzenNetSlug,
            CurrentPrice = x.Stock.CurrentPrice,
            CurrentPriceChange = x.Stock.CurrentPriceChange,
            CurrentPriceChangePercent = x.Stock.CurrentPriceChangePercent,
            CurrentPriceAt = x.Stock.CurrentPriceAt,
            TrackingStatus = x.Stock.TrackingStatus.ToString(),
            Source = x.Source,
            ProviderConstituentKey = x.ProviderConstituentKey,
            EffectiveFrom = x.EffectiveFrom,
            EffectiveTo = x.EffectiveTo,
            LastVerifiedAt = x.LastVerifiedAt,
            ImportedAt = x.ImportedAt,
        }).ToList();

        var latestMembership = memberships
            .OrderByDescending(x => x.LastVerifiedAt ?? x.ImportedAt)
            .ThenByDescending(x => x.ImportedAt)
            .FirstOrDefault();

        return Ok(new IndexConstituentsResponse
        {
            MarketIndexId = id,
            IndexName = marketIndex.Name,
            TotalCount = dtos.Count,
            Source = latestMembership?.Source,
            AsOfDate = latestMembership?.LastVerifiedAt,
            IsCuratedSnapshot = GetNonEmptyTrimmed(latestMembership?.Source)?
                .Contains("curated snapshot", StringComparison.OrdinalIgnoreCase) == true,
            IsStale = false,
            StaleReason = null,
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

        var refreshLock = RefreshLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var providerResult = await _constituentsProvider.GetConstituentsAsync(marketIndex, cancellationToken);

            if (providerResult.Status == IndexConstituentsStatus.Unsupported)
            {
                return UnprocessableEntity(CreateRefreshResponse(id, providerResult));
            }

            if (providerResult.Status == IndexConstituentsStatus.RateLimited)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, CreateRefreshResponse(id, providerResult));
            }

            if (providerResult.Status == IndexConstituentsStatus.ProviderFailure)
            {
                return StatusCode(StatusCodes.Status502BadGateway, CreateRefreshResponse(id, providerResult));
            }

            var now = providerResult.FetchedAt;
            int added = 0, updated = 0, unchanged = 0, closed = 0, conflicts = 0;

            var normalizedConstituents = new List<(string Ticker, string ProviderSymbol, string CompanyName, string Exchange, string? Isin)>();
            var sourceIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var constituent in providerResult.Constituents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ticker = constituent.Ticker?.Trim().ToUpperInvariant();
                var companyName = constituent.CompanyName?.Trim();
                var exchangeRaw = constituent.ProviderExchange?.Trim();
                var providerSymbol = constituent.ProviderSymbol?.Trim();
                var normalizedIsin = StockIdentifiers.Normalize(constituent.Isin);

                if (string.IsNullOrWhiteSpace(ticker)
                    || string.IsNullOrWhiteSpace(companyName)
                    || string.IsNullOrWhiteSpace(exchangeRaw)
                    || !StockExchanges.TryNormalize(exchangeRaw, out var normalizedExchange))
                {
                    conflicts++;
                    continue;
                }

                if (normalizedIsin is not null && !StockIdentifiers.IsValidIsin(normalizedIsin))
                {
                    conflicts++;
                    continue;
                }

                providerSymbol ??= StockExchanges.ResolveProviderSymbol(ticker, normalizedExchange);
                if (string.IsNullOrWhiteSpace(providerSymbol))
                {
                    conflicts++;
                    continue;
                }

                var sourceIdentity = $"{providerSymbol}|{normalizedExchange}";
                if (!sourceIdentities.Add(sourceIdentity))
                {
                    conflicts++;
                    continue;
                }

                normalizedConstituents.Add((ticker, providerSymbol, companyName, normalizedExchange, normalizedIsin));
            }

            var effectiveStatus = providerResult.Status;
            var canCloseMissingMemberships = providerResult.Status == IndexConstituentsStatus.Success && conflicts == 0;
            if (providerResult.Status == IndexConstituentsStatus.Success && conflicts > 0)
            {
                effectiveStatus = IndexConstituentsStatus.Partial;
            }

            // Load existing current memberships for this index.
            var existingMemberships = await _context.StockMarketIndices
                .Include(x => x.Stock)
                .Where(x => x.MarketIndexId == id && x.EffectiveTo == null)
                .ToListAsync(cancellationToken);

            var allIsins = normalizedConstituents
                .Where(c => c.Isin is not null)
                .Select(c => c.Isin!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var allProviderSymbols = normalizedConstituents
                .Select(c => c.ProviderSymbol)
                .ToHashSet(StringComparer.Ordinal);

            var allTickers = normalizedConstituents
                .Select(c => c.Ticker)
                .ToHashSet(StringComparer.Ordinal);

            var existingStocks = await _context.Stocks
                .Where(s =>
                    (s.Isin != null && allIsins.Contains(s.Isin)) ||
                    (s.ProviderSymbol != null && allProviderSymbols.Contains(s.ProviderSymbol)) ||
                    allTickers.Contains(s.Ticker))
                .ToListAsync(cancellationToken);

            var byIsin = existingStocks
                .Where(s => s.Isin != null)
                .GroupBy(s => s.Isin!)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var byProviderSymbol = existingStocks
                .Where(s => s.ProviderSymbol != null)
                .GroupBy(s => s.ProviderSymbol!)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var byTickerExchange = existingStocks
                .GroupBy(s => $"{s.Ticker}|{s.Exchange}")
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var seenStocks = new HashSet<Stock>(ReferenceEqualityComparer.Instance);
            try
            {
                foreach (var constituent in normalizedConstituents)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Deduplication order: ISIN → ProviderSymbol → Ticker+Exchange.
                    Stock? stock = null;

                    if (constituent.Isin is not null)
                        byIsin.TryGetValue(constituent.Isin, out stock);

                    if (stock is null)
                        byProviderSymbol.TryGetValue(constituent.ProviderSymbol, out stock);

                    if (stock is null)
                        byTickerExchange.TryGetValue($"{constituent.Ticker}|{constituent.Exchange}", out stock);

                    if (stock is null)
                    {
                        stock = new Stock
                        {
                            Ticker = constituent.Ticker,
                            Name = constituent.CompanyName,
                            CommonName = constituent.CompanyName,
                            Exchange = constituent.Exchange,
                            Isin = constituent.Isin,
                            ProviderSymbol = constituent.ProviderSymbol,
                            TrackingStatus = StockTrackingStatus.CatalogOnly,
                            UpdatedAt = now,
                        };
                        _context.Stocks.Add(stock);
                        if (stock.Isin != null) byIsin[stock.Isin] = stock;
                        byProviderSymbol[stock.ProviderSymbol!] = stock;
                        byTickerExchange[$"{stock.Ticker}|{stock.Exchange}"] = stock;
                    }
                    else
                    {
                        var stockChanged = false;
                        if (!string.Equals(stock.Name, constituent.CompanyName, StringComparison.Ordinal))
                        {
                            var previousName = stock.Name;
                            stock.Name = constituent.CompanyName;
                            if (string.Equals(stock.CommonName, previousName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(stock.CommonName))
                            {
                                stock.CommonName = constituent.CompanyName;
                            }
                            stockChanged = true;
                        }
                        if (stock.Isin is null && constituent.Isin is not null)
                        {
                            stock.Isin = constituent.Isin;
                            stockChanged = true;
                        }
                        if (string.IsNullOrWhiteSpace(stock.ProviderSymbol))
                        {
                            stock.ProviderSymbol = constituent.ProviderSymbol;
                            stockChanged = true;
                        }
                        if (stockChanged)
                        {
                            stock.UpdatedAt = now;
                            updated++;
                        }
                    }

                    var membership = existingMemberships.FirstOrDefault(m => ReferenceEquals(m.Stock, stock) || (stock.Id != 0 && m.StockId == stock.Id));
                    if (membership is null)
                    {
                        membership = new StockMarketIndex
                        {
                            Stock = stock,
                            MarketIndexId = id,
                            Source = providerResult.ProviderName,
                            ProviderConstituentKey = constituent.ProviderSymbol,
                            EffectiveFrom = now,
                            ImportedAt = now,
                            LastVerifiedAt = providerResult.AsOfDate ?? now,
                        };
                        _context.StockMarketIndices.Add(membership);
                        existingMemberships.Add(membership);
                        added++;
                    }
                    else
                    {
                        var membershipChanged = false;
                        if (!string.Equals(membership.Source, providerResult.ProviderName, StringComparison.Ordinal))
                        {
                            membership.Source = providerResult.ProviderName;
                            membershipChanged = true;
                        }
                        if (!string.Equals(membership.ProviderConstituentKey, constituent.ProviderSymbol, StringComparison.Ordinal))
                        {
                            membership.ProviderConstituentKey = constituent.ProviderSymbol;
                            membershipChanged = true;
                        }
                        membership.LastVerifiedAt = providerResult.AsOfDate ?? now;
                        if (membershipChanged)
                        {
                            updated++;
                        }
                        else
                        {
                            unchanged++;
                        }
                    }

                    seenStocks.Add(stock);
                }

                if (canCloseMissingMemberships)
                {
                    foreach (var membership in existingMemberships
                        .Where(m => m.EffectiveTo == null && !seenStocks.Contains(m.Stock)))
                    {
                        membership.EffectiveTo = now;
                        closed++;
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
            {
                return Conflict("Конкурентное обновление состава индекса. Повторите попытку.");
            }

            var providerMessage = providerResult.Message;
            if (conflicts > 0)
            {
                var conflictsMessage = $"Пропущено конфликтных/некорректных записей: {conflicts}.";
                providerMessage = string.IsNullOrWhiteSpace(providerMessage)
                    ? conflictsMessage
                    : $"{providerMessage} {conflictsMessage}";
            }

            return Ok(new IndexConstituentsRefreshResponse
            {
                MarketIndexId = id,
                ProviderStatus = effectiveStatus.ToString(),
                ProviderName = providerResult.ProviderName,
                ProviderMessage = providerMessage,
                FetchedAt = providerResult.FetchedAt,
                AsOfDate = providerResult.AsOfDate,
                SourceUrl = providerResult.SourceUrl,
                IsCuratedSnapshot = providerResult.IsCuratedSnapshot,
                IsStale = providerResult.IsStale || effectiveStatus == IndexConstituentsStatus.Partial,
                Added = added,
                Updated = updated,
                Unchanged = unchanged,
                Closed = closed,
                Conflicts = conflicts,
            });
        }
        finally
        {
            refreshLock.Release();
        }
    }

    [HttpPost("{indexId:int}/constituents/{stockId:int}/history/refresh")]
    public async Task<ActionResult<StockHistoryRefreshResponse>> RefreshConstituentHistory(
        int indexId,
        int stockId,
        CancellationToken cancellationToken = default)
    {
        var marketIndex = await _context.MarketIndices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == indexId, cancellationToken);
        if (marketIndex is null)
        {
            return NotFound("Индекс не найден.");
        }

        if (marketIndex.IsArchived)
        {
            return Conflict("Нельзя обновлять историю акций для архивного индекса.");
        }

        var membership = await _context.StockMarketIndices
            .AsNoTracking()
            .Include(x => x.Stock)
            .FirstOrDefaultAsync(
                x => x.MarketIndexId == indexId && x.StockId == stockId && x.EffectiveTo == null,
                cancellationToken);

        if (membership?.Stock is null)
        {
            return NotFound("Акция не входит в текущий состав выбранного индекса.");
        }

        var stock = membership.Stock;
        if (!TryValidateTickerAndExchange(stock, out var validationError))
        {
            return BadRequest(validationError);
        }

        try
        {
            var result = await _stockHistoryService.RefreshHistoryAsync(stock, cancellationToken);
            _logger.LogInformation(
                "Index constituent history refreshed: indexId={IndexId}, stockId={StockId}, deleted={DeletedPoints}, imported={ImportedPoints}, rateLimited={RateLimited}",
                indexId,
                stockId,
                result.DeletedPoints,
                result.ImportedPoints,
                result.RateLimited);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Index constituent history refresh failed: indexId={IndexId}, stockId={StockId}", indexId, stockId);
            return StatusCode(StatusCodes.Status502BadGateway, "Не удалось обновить исторические данные акции. Попробуйте позже.");
        }
    }

    [HttpPost("{indexId:int}/constituents/history/refresh")]
    public async Task<ActionResult<IndexConstituentHistoryRefreshBatchResponse>> RefreshConstituentsHistory(
        int indexId,
        CancellationToken cancellationToken = default)
    {
        var marketIndex = await _context.MarketIndices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == indexId, cancellationToken);
        if (marketIndex is null)
        {
            return NotFound("Индекс не найден.");
        }

        if (marketIndex.IsArchived)
        {
            return Conflict("Нельзя обновлять историю акций для архивного индекса.");
        }

        var batchLock = BatchHistoryRefreshLocks.GetOrAdd(indexId, static _ => new SemaphoreSlim(1, 1));
        if (!await batchLock.WaitAsync(0, cancellationToken))
        {
            return Conflict("Обновление исторических данных для этого индекса уже выполняется.");
        }

        try
        {
            var membershipRows = await _context.StockMarketIndices
                .AsNoTracking()
                .Include(x => x.Stock)
                .Where(x => x.MarketIndexId == indexId && x.EffectiveTo == null)
                .OrderBy(x => x.StockId)
                .ThenBy(x => x.Stock.Ticker)
                .ToListAsync(cancellationToken);
            var currentConstituents = membershipRows
                .GroupBy(x => x.StockId)
                .Select(x => x.First().Stock)
                .ToList();

            var items = new List<IndexConstituentHistoryRefreshItemResponse>();
            var attempted = 0;
            var succeeded = 0;
            var failed = 0;
            var rateLimited = 0;
            var skippedRateLimited = 0;
            var detailsTruncated = false;
            var stopDueToRateLimit = false;

            for (var i = 0; i < currentConstituents.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stock = currentConstituents[i];

                if (stopDueToRateLimit)
                {
                    skippedRateLimited++;
                    AppendResult(new IndexConstituentHistoryRefreshItemResponse
                    {
                        StockId = stock.Id,
                        Ticker = stock.Ticker,
                        Exchange = stock.Exchange,
                        Status = "SkippedRateLimited",
                        Error = "Пропущено из-за общего лимита/паузы у поставщика."
                    });
                    continue;
                }

                attempted++;

                if (!TryValidateTickerAndExchange(stock, out var validationError))
                {
                    failed++;
                    AppendResult(new IndexConstituentHistoryRefreshItemResponse
                    {
                        StockId = stock.Id,
                        Ticker = stock.Ticker,
                        Exchange = stock.Exchange,
                        Status = "Failed",
                        Error = validationError
                    });
                    continue;
                }

                try
                {
                    var refreshResult = await _stockHistoryService.RefreshHistoryAsync(stock, cancellationToken);
                    if (refreshResult.RateLimited)
                    {
                        rateLimited++;
                        stopDueToRateLimit = true;
                        AppendResult(new IndexConstituentHistoryRefreshItemResponse
                        {
                            StockId = stock.Id,
                            Ticker = stock.Ticker,
                            Exchange = stock.Exchange,
                            Status = "RateLimited",
                            DeletedPoints = refreshResult.DeletedPoints,
                            ImportedPoints = refreshResult.ImportedPoints,
                            Error = "Поставщик временно ограничил запросы."
                        });
                        continue;
                    }

                    succeeded++;
                    AppendResult(new IndexConstituentHistoryRefreshItemResponse
                    {
                        StockId = stock.Id,
                        Ticker = stock.Ticker,
                        Exchange = stock.Exchange,
                        Status = "Succeeded",
                        DeletedPoints = refreshResult.DeletedPoints,
                        ImportedPoints = refreshResult.ImportedPoints
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    failed++;
                    AppendResult(new IndexConstituentHistoryRefreshItemResponse
                    {
                        StockId = stock.Id,
                        Ticker = stock.Ticker,
                        Exchange = stock.Exchange,
                        Status = "Failed",
                        Error = ex.Message
                    });
                }
                catch (Exception ex)
                {
                    failed++;
                    AppendResult(new IndexConstituentHistoryRefreshItemResponse
                    {
                        StockId = stock.Id,
                        Ticker = stock.Ticker,
                        Exchange = stock.Exchange,
                        Status = "Failed",
                        Error = "Не удалось обновить исторические данные."
                    });
                    _logger.LogWarning(ex, "Batch constituent history refresh failed: indexId={IndexId}, stockId={StockId}", indexId, stock.Id);
                }
            }

            _logger.LogInformation(
                "Batch constituent history refresh completed: indexId={IndexId}, total={Total}, attempted={Attempted}, succeeded={Succeeded}, failed={Failed}, rateLimited={RateLimited}, skippedRateLimited={SkippedRateLimited}, stoppedDueToRateLimit={StoppedDueToRateLimit}",
                indexId,
                currentConstituents.Count,
                attempted,
                succeeded,
                failed,
                rateLimited,
                skippedRateLimited,
                stopDueToRateLimit);

            return Ok(new IndexConstituentHistoryRefreshBatchResponse
            {
                MarketIndexId = indexId,
                Total = currentConstituents.Count,
                Attempted = attempted,
                Succeeded = succeeded,
                Failed = failed,
                RateLimited = rateLimited,
                SkippedRateLimited = skippedRateLimited,
                StoppedDueToRateLimit = stopDueToRateLimit,
                DetailsTruncated = detailsTruncated,
                Results = items
            });

            void AppendResult(IndexConstituentHistoryRefreshItemResponse result)
            {
                if (items.Count < MaxBatchResultDetails)
                {
                    items.Add(result);
                    return;
                }

                detailsTruncated = true;
            }
        }
        finally
        {
            batchLock.Release();
            if (batchLock.CurrentCount == 1)
            {
                BatchHistoryRefreshLocks.TryRemove(new KeyValuePair<int, SemaphoreSlim>(indexId, batchLock));
            }
        }
    }

    private static bool TryValidateTickerAndExchange(Stock stock, out string? validationError)
    {
        if (string.IsNullOrWhiteSpace(stock.Ticker))
        {
            validationError = "У акции должен быть указан тикер для обновления исторических данных.";
            return false;
        }

        if (!StockExchanges.TryNormalize(stock.Exchange, out var normalizedExchange))
        {
            validationError = "У акции указана некорректная биржа для обновления исторических данных.";
            return false;
        }

        stock.Exchange = normalizedExchange;
        validationError = null;
        return true;
    }

    private static string? GetNonEmptyTrimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IndexConstituentsRefreshResponse CreateRefreshResponse(int marketIndexId, IndexConstituentsResult providerResult)
        => new()
        {
            MarketIndexId = marketIndexId,
            ProviderStatus = providerResult.Status.ToString(),
            ProviderName = providerResult.ProviderName,
            ProviderMessage = providerResult.Message,
            FetchedAt = providerResult.FetchedAt,
            AsOfDate = providerResult.AsOfDate,
            SourceUrl = providerResult.SourceUrl,
            IsCuratedSnapshot = providerResult.IsCuratedSnapshot,
            IsStale = providerResult.IsStale,
            Added = 0,
            Updated = 0,
            Unchanged = 0,
            Closed = 0,
            Conflicts = 0,
        };

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
