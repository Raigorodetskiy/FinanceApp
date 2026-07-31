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
}
