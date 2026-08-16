using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace FinanceApp.Core.Models;

public class MarketIndexHistoricalPrice
{
    public int Id { get; set; }
    public int MarketIndexId { get; set; }

    [JsonIgnore]
    public MarketIndex MarketIndex { get; set; } = null!;

    public DateTime Timestamp { get; set; }

    [Column(TypeName = "varchar(10)")]
    public string Interval { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,6)")]
    public decimal Open { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    public decimal High { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    public decimal Low { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    public decimal Close { get; set; }

    public long? Volume { get; set; }

    [Column(TypeName = "varchar(64)")]
    public string? Provider { get; set; }

    public DateTime? FetchedAt { get; set; }

    [Column(TypeName = "varchar(50)")]
    public string? ProviderSymbol { get; set; }
}
