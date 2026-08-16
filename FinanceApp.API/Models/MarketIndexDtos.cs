namespace FinanceApp.API.Models;

public sealed class MarketIndexDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string NormalizedName { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string NormalizedCode { get; init; } = string.Empty;
    public string? ProviderSymbol { get; init; }
    public string Description { get; init; } = string.Empty;
    public string CountryOrRegion { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsArchived { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class UpsertMarketIndexRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? ProviderSymbol { get; init; }
    public string? Description { get; init; }
    public string? CountryOrRegion { get; init; }
    public int SortOrder { get; init; }
}

public sealed class MarketIndexHistoryPointDto
{
    public DateTime Timestamp { get; init; }
    public string Interval { get; init; } = string.Empty;
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public long? Volume { get; init; }
}

public sealed class MarketIndexHistoryResponse
{
    public int MarketIndexId { get; init; }
    public string Range { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public bool IsStale { get; init; }
    public string? StaleReason { get; init; }
    public IReadOnlyList<MarketIndexHistoryPointDto> Points { get; init; } = Array.Empty<MarketIndexHistoryPointDto>();
}

public sealed class MarketIndexRefreshResponse
{
    public int MarketIndexId { get; init; }
    public int DeletedPoints { get; init; }
    public int ImportedPoints { get; init; }
}

