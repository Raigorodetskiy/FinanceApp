using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApp.Core.Models;

public class Stock
{
    public int Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CommonName { get; set; } = string.Empty;
    public string Exchange { get; set; } = StockExchanges.Nyse;
    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentPrice { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Wkn { get; set; }
    public string? Isin { get; set; }
    /// <summary>
    /// Optional finanzen.net instrument slug (e.g. <c>microsoft-aktie</c>).
    /// Used by the experimental <c>FinanzenNetQuoteService</c> when it is enabled.
    /// Must consist only of lowercase letters, digits, and hyphens.
    /// Null means the stock is not mapped to a finanzen.net instrument.
    /// </summary>
    public string? FinanzenNetSlug { get; set; }

    /// <summary>
    /// Absolute daily change of <see cref="CurrentPrice"/> in the application/normalized currency
    /// (e.g. EUR). Null when not yet populated or unavailable from the provider.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? CurrentPriceChange { get; set; }

    /// <summary>
    /// Percentage daily change corresponding to <see cref="CurrentPrice"/>.
    /// Null when not yet populated or unavailable from the provider.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? CurrentPriceChangePercent { get; set; }

    /// <summary>
    /// UTC timestamp of the price as reported by the quote provider.
    /// Null when not yet populated. Distinct from <see cref="UpdatedAt"/>,
    /// which records when the row was last written to the database.
    /// </summary>
    public DateTime? CurrentPriceAt { get; set; }
    public int? IndustryId { get; set; }
    public Industry? Industry { get; set; }
}
