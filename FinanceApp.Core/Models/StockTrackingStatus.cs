namespace FinanceApp.Core.Models;

/// <summary>
/// Controls whether a stock participates in price tracking, history sync, and portfolio operations.
/// </summary>
public enum StockTrackingStatus
{
    /// <summary>
    /// The stock is stored in the catalog (e.g. as a market-index component) but is not tracked.
    /// CatalogOnly stocks are excluded from the default GET /api/stocks response, portfolio
    /// selectors, bulk quote/history refresh, and all background sync jobs.
    /// </summary>
    CatalogOnly = 0,

    /// <summary>
    /// The stock is actively tracked. All existing stock behaviour applies.
    /// Existing stocks are backfilled to this status during migration.
    /// </summary>
    Tracked = 1,
}
