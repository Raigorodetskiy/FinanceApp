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

    public long Volume { get; set; }
}
