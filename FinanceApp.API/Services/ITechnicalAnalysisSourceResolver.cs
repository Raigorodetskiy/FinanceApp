namespace FinanceApp.API.Services;

/// <summary>
/// Resolves which <c>stockId</c> / listing to use as the data source for technical analysis
/// for a requested stock.
///
/// <para><b>Why a resolver?</b> Some securities have multiple <c>Stock</c> rows (one per exchange
/// listing). ISINs identify the security; each <c>stockId</c> identifies an exchange-specific
/// listing. A same-ISIN listing may have fuller history and can be used as a data source, but
/// cross-listing absolute-value comparisons are not performed in Phase 1 (different currency,
/// venue, liquidity, and trading hours mean absolute indicator values are NOT interchangeable).</para>
///
/// <para><b>Sufficient-history threshold (Phase 1):</b>
/// A listing is considered to have <em>sufficient</em> history when it has at least
/// <see cref="SufficientDailyObservations"/> daily observations, which supports SMA200 (the core
/// long-term indicator). Listings with fewer observations are considered insufficient but still
/// ranked so that future partial-calculation services can use them.</para>
/// </summary>
public interface ITechnicalAnalysisSourceResolver
{
    /// <summary>
    /// Minimum daily observations for a listing to be considered "sufficient" for Phase 1
    /// full-indicator calculations. Value = 252 (≈ 1 trading year, supports SMA200).
    /// </summary>
    const int SufficientDailyObservations = 252;

    Task<TechnicalAnalysisSourceResolution> ResolveAsync(
        int requestedStockId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a technical-analysis source resolution.
/// </summary>
/// <param name="RequestedStockId">The originally-requested stock ID.</param>
/// <param name="AnalysisStockId">The selected stock ID to use for analysis; null when no suitable source found.</param>
/// <param name="Resolution">Human-readable resolution category (e.g. "OwnHistory", "SameIsin", "NoSuitableHistory").</param>
/// <param name="Isin">ISIN of the requested stock (null when unavailable).</param>
/// <param name="IsInherited">True when the selected source is a different listing (same-ISIN fallback).</param>
/// <param name="Reason">Diagnostic message explaining the resolution decision.</param>
public sealed record TechnicalAnalysisSourceResolution(
    int RequestedStockId,
    int? AnalysisStockId,
    string Resolution,
    string? Isin,
    bool IsInherited,
    string? Reason);
