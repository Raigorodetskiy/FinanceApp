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

    [Column(TypeName = "decimal(18,4)")]
    public decimal Close { get; set; }

    public string? QuoteCurrency { get; set; }
    public string? FinancialCurrency { get; set; }
    public string? NormalizedQuoteCurrency { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    public decimal QuoteUnitMultiplier { get; set; } = 1m;

    public long Volume { get; set; }
}
