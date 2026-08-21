using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApp.Core.Models;

public class StockHistoricalPrice
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public Stock Stock { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public string Interval { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal Open { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal High { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Low { get; set; }

    /// <summary>
    /// Unadjusted (raw) close price as returned by the Yahoo Finance v8 chart API
    /// <c>indicators.quote.close</c> field. This value is NOT split-adjusted or
    /// dividend-adjusted. Phase 1 technical indicators consume this field directly.
    /// Consumers performing multi-period return calculations should account for the
    /// potential impact of corporate actions (splits, dividends) on comparability.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal Close { get; set; }

    /// <summary>
    /// Split- and dividend-adjusted close price when the upstream provider exposes a valid
    /// <c>indicators.adjclose</c> value aligned to this candle. Null when unavailable, malformed,
    /// non-positive, or not provided for the interval. Raw <see cref="Close"/> remains the
    /// canonical audit value and is never overwritten by this field.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? AdjustedClose { get; set; }

    public string? QuoteCurrency { get; set; }
    public string? FinancialCurrency { get; set; }
    public string? NormalizedQuoteCurrency { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    public decimal QuoteUnitMultiplier { get; set; } = 1m;

    public long Volume { get; set; }
    public bool IsQuoteDerived { get; set; }
}
