using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FinanceApp.Core.Models;

public class MarketIndex
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string NormalizedName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(50)]
    public string NormalizedCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? ProviderSymbol { get; set; }

    public string Description { get; set; } = string.Empty;
    public string CountryOrRegion { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public ICollection<StockMarketIndex> StockMarketIndices { get; set; } = new List<StockMarketIndex>();
}
