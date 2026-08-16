using System.ComponentModel.DataAnnotations;

namespace FinanceApp.Core.Models;

/// <summary>
/// Represents a current or historical membership of a <see cref="Stock"/> in a <see cref="MarketIndex"/>.
/// </summary>
public class StockMarketIndex
{
    /// <summary>Surrogate primary key to allow membership history rows.</summary>
    public int Id { get; set; }

    public int StockId { get; set; }
    public int MarketIndexId { get; set; }

    /// <summary>Source/provider that created this membership record (e.g. "Yahoo", "Manual").</summary>
    [MaxLength(100)]
    public string? Source { get; set; }

    /// <summary>Provider-specific key identifying this constituent (e.g. the symbol used by the source).</summary>
    [MaxLength(100)]
    public string? ProviderConstituentKey { get; set; }

    /// <summary>When this membership became effective. Null means the start is unknown.</summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>When this membership ended. Null means the membership is still current.</summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>Last time this membership was verified by a provider snapshot.</summary>
    public DateTime? LastVerifiedAt { get; set; }

    /// <summary>When this row was first imported/created.</summary>
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public Stock Stock { get; set; } = null!;
    public MarketIndex MarketIndex { get; set; } = null!;
}
