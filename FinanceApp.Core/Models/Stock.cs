using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

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
    public bool CurrentPriceIsDelayed { get; set; }
    [MaxLength(300)]
    public string? CurrentPriceDelayWarning { get; set; }
    public int? IndustryId { get; set; }
    public Industry? Industry { get; set; }

    /// <summary>
    /// Tracking status. Defaults to <see cref="StockTrackingStatus.Tracked"/> for stocks created
    /// through the standard API. Stocks imported as index constituents start as
    /// <see cref="StockTrackingStatus.CatalogOnly"/> and can be promoted to Tracked explicitly.
    /// </summary>
    public StockTrackingStatus TrackingStatus { get; set; } = StockTrackingStatus.Tracked;

    /// <summary>
    /// Symbol as provided by the data provider used to import this stock (e.g. Yahoo Finance).
    /// Distinct from the user-editable <see cref="Ticker"/>; preserved for deduplication and
    /// re-import matching. Max 50 characters; null when unknown or not imported via a provider.
    /// </summary>
    [MaxLength(50)]
    public string? ProviderSymbol { get; set; }

    [JsonIgnore]
    public ICollection<StockMarketIndex> MarketIndices { get; set; } = new List<StockMarketIndex>();

    [NotMapped]
    public List<int>? MarketIndexIds { get; set; }
}
