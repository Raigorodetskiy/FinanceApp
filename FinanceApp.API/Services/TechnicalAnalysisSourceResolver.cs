using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// Default implementation of <see cref="ITechnicalAnalysisSourceResolver"/>.
///
/// <para><b>Resolution algorithm:</b>
/// <list type="number">
///   <item>Load the requested stock and count its usable daily observations.</item>
///   <item>If the requested stock has ≥ <see cref="ITechnicalAnalysisSourceResolver.SufficientDailyObservations"/>
///     usable observations, use it (own-history wins).</item>
///   <item>Otherwise, if the stock has a non-empty, valid normalized ISIN, collect all other
///     stocks sharing the same normalized ISIN.</item>
///   <item>Exclude candidates without any usable daily observations.</item>
///   <item>Rank candidates deterministically:
///     <list type="bullet">
///       <item>Sufficient history (≥252) before insufficient;</item>
///       <item>Larger usable daily-observation count;</item>
///       <item>Fresher latest daily observation (most-recent date descending);</item>
///       <item>Smallest <c>stockId</c> as stable tie-breaker.</item>
///     </list>
///   </item>
///   <item>Pick the top candidate, or return <c>NoSuitableHistory</c> if none qualify.</item>
/// </list>
/// </para>
///
/// <para><b>Important limitations (Phase 1):</b>
/// Cross-listing absolute indicator values are NOT interchangeable due to differences in
/// currency, venue, liquidity, and trading hours. The resolver selects a data source for
/// calculation; callers must disclose inheritance to end-users. No cross-currency conversion
/// is performed.</para>
///
/// <para><b>Normalization:</b> ISIN matching uses the <see cref="Stock.Isin"/> value after
/// trimming and upper-casing. Ticker and company-name matching is never used as a fallback.
/// Different share classes with different ISINs will never match.</para>
/// </summary>
public sealed class TechnicalAnalysisSourceResolver : ITechnicalAnalysisSourceResolver
{
    private readonly AppDbContext _dbContext;

    public TechnicalAnalysisSourceResolver(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc/>
    public async Task<TechnicalAnalysisSourceResolution> ResolveAsync(
        int requestedStockId,
        CancellationToken cancellationToken = default)
    {
        var stock = await _dbContext.Stocks
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == requestedStockId, cancellationToken);

        if (stock is null)
        {
            return new TechnicalAnalysisSourceResolution(
                requestedStockId, null, "NotFound", null, false,
                $"Stock {requestedStockId} not found.");
        }

        var normalizedIsin = NormalizeIsin(stock.Isin);

        // Count usable daily observations for the requested stock
        var requestedCount = await CountUsableDailyObservationsAsync(requestedStockId, cancellationToken);
        var requestedLatest = await GetLatestDailyObservationDateAsync(requestedStockId, cancellationToken);

        // Own sufficient history always wins
        if (requestedCount >= ITechnicalAnalysisSourceResolver.SufficientDailyObservations)
        {
            return new TechnicalAnalysisSourceResolution(
                requestedStockId, requestedStockId, "OwnHistory", normalizedIsin, false,
                $"Requested listing has {requestedCount} usable daily observations (≥ threshold {ITechnicalAnalysisSourceResolver.SufficientDailyObservations}).");
        }

        // No valid ISIN → cannot fallback
        if (string.IsNullOrWhiteSpace(normalizedIsin))
        {
            if (requestedCount > 0)
            {
                // Has some data but insufficient and no ISIN fallback
                return new TechnicalAnalysisSourceResolution(
                    requestedStockId, requestedStockId, "InsufficientHistory", normalizedIsin, false,
                    $"Requested listing has only {requestedCount} usable daily observations (< threshold {ITechnicalAnalysisSourceResolver.SufficientDailyObservations}) and no valid ISIN for fallback.");
            }

            return new TechnicalAnalysisSourceResolution(
                requestedStockId, null, "NoSuitableHistory", normalizedIsin, false,
                $"Requested listing has no usable daily history and no valid ISIN for same-ISIN fallback.");
        }

        // Find same-ISIN candidates (exact normalized-ISIN match only; never ticker/name)
        var candidates = await FindSameIsinCandidatesAsync(normalizedIsin, requestedStockId, cancellationToken);

        // Include the requested stock itself as a candidate (may have some data even if insufficient)
        if (requestedCount > 0)
        {
            candidates.Add(new CandidateInfo(requestedStockId, requestedCount, requestedLatest));
        }

        if (candidates.Count == 0)
        {
            return new TechnicalAnalysisSourceResolution(
                requestedStockId, null, "NoSuitableHistory", normalizedIsin, false,
                $"No same-ISIN ({normalizedIsin}) listings with usable daily history found.");
        }

        // Rank deterministically:
        // 1. Sufficient (≥252) before insufficient
        // 2. Larger observation count
        // 3. Fresher latest observation
        // 4. Smallest stockId (stable tie-breaker)
        var best = candidates
            .OrderByDescending(c => c.ObservationCount >= ITechnicalAnalysisSourceResolver.SufficientDailyObservations ? 1 : 0)
            .ThenByDescending(c => c.ObservationCount)
            .ThenByDescending(c => c.LatestDate ?? DateTime.MinValue)
            .ThenBy(c => c.StockId)
            .First();

        bool isInherited = best.StockId != requestedStockId;
        string resolution = best.ObservationCount >= ITechnicalAnalysisSourceResolver.SufficientDailyObservations
            ? (isInherited ? "SameIsin" : "OwnHistory")
            : (isInherited ? "SameIsinInsufficientHistory" : "InsufficientHistory");

        return new TechnicalAnalysisSourceResolution(
            requestedStockId, best.StockId, resolution, normalizedIsin, isInherited,
            $"Selected stock {best.StockId} with {best.ObservationCount} usable daily observations" +
            (isInherited ? $" (same-ISIN {normalizedIsin} fallback)" : " (own history)") + ".");
    }

    private async Task<int> CountUsableDailyObservationsAsync(int stockId, CancellationToken ct)
    {
        return await _dbContext.StockHistoricalPrices
            .AsNoTracking()
            .CountAsync(p => p.StockId == stockId && p.Interval == "1d" && p.Close > 0, ct);
    }

    private async Task<DateTime?> GetLatestDailyObservationDateAsync(int stockId, CancellationToken ct)
    {
        return await _dbContext.StockHistoricalPrices
            .AsNoTracking()
            .Where(p => p.StockId == stockId && p.Interval == "1d" && p.Close > 0)
            .OrderByDescending(p => p.Timestamp)
            .Select(p => (DateTime?)p.Timestamp)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<List<CandidateInfo>> FindSameIsinCandidatesAsync(
        string normalizedIsin,
        int excludeStockId,
        CancellationToken ct)
    {
        // Load all stocks with matching ISIN (exact normalized match), excluding the requested stock
        var matchingStocks = await _dbContext.Stocks
            .AsNoTracking()
            .Where(s => s.Id != excludeStockId && s.Isin != null)
            .ToListAsync(ct);

        // Filter by normalized ISIN in memory (trim+uppercase)
        var sameIsinIds = matchingStocks
            .Where(s => NormalizeIsin(s.Isin) == normalizedIsin)
            .Select(s => s.Id)
            .ToList();

        if (sameIsinIds.Count == 0) return [];

        // Get observation counts and latest dates for each candidate
        var result = new List<CandidateInfo>();
        foreach (var candidateId in sameIsinIds)
        {
            var count = await CountUsableDailyObservationsAsync(candidateId, ct);
            if (count == 0) continue; // Exclude candidates without usable history

            var latest = await GetLatestDailyObservationDateAsync(candidateId, ct);
            result.Add(new CandidateInfo(candidateId, count, latest));
        }
        return result;
    }

    private sealed record CandidateInfo(int StockId, int ObservationCount, DateTime? LatestDate);

    /// <summary>Normalize an ISIN for comparison: trim whitespace and upper-case.</summary>
    private static string? NormalizeIsin(string? isin)
    {
        if (string.IsNullOrWhiteSpace(isin)) return null;
        return isin.Trim().ToUpperInvariant();
    }
}
