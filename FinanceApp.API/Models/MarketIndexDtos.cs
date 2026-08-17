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

/// <summary>DTO representing one current or historical constituent of an index.</summary>
public sealed class IndexConstituentDto
{
    public int StockId { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public string? ProviderSymbol { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? CommonName { get; init; }
    public string Exchange { get; init; } = string.Empty;
    public string? Isin { get; init; }
    public string? Wkn { get; init; }
    public string? FinanzenNetSlug { get; init; }
    public decimal? CurrentPrice { get; init; }
    public decimal? CurrentPriceChange { get; init; }
    public decimal? CurrentPriceChangePercent { get; init; }
    public DateTime? CurrentPriceAt { get; init; }
    /// <summary>"CatalogOnly" or "Tracked"</summary>
    public string TrackingStatus { get; init; } = string.Empty;
    public string? Source { get; init; }
    public string? ProviderConstituentKey { get; init; }
    public DateTime? EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public DateTime? LastVerifiedAt { get; init; }
    public DateTime ImportedAt { get; init; }
}

/// <summary>Response for GET /api/market-indices/{id}/constituents</summary>
public sealed class IndexConstituentsResponse
{
    public int MarketIndexId { get; init; }
    public string IndexName { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public string? Source { get; init; }
    public DateTime? AsOfDate { get; init; }
    public bool IsCuratedSnapshot { get; init; }
    public bool IsStale { get; init; }
    public string? StaleReason { get; init; }
    public IReadOnlyList<IndexConstituentDto> Constituents { get; init; } = Array.Empty<IndexConstituentDto>();
}

/// <summary>Response for POST /api/market-indices/{id}/constituents/refresh</summary>
public sealed class IndexConstituentsRefreshResponse
{
    public int MarketIndexId { get; init; }
    public string ProviderStatus { get; init; } = string.Empty;
    public string? ProviderName { get; init; }
    public string? ProviderMessage { get; init; }
    public DateTime? FetchedAt { get; init; }
    public DateTime? AsOfDate { get; init; }
    public string? SourceUrl { get; init; }
    public bool IsCuratedSnapshot { get; init; }
    public bool IsStale { get; init; }
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Unchanged { get; init; }
    public int Closed { get; init; }
    public int Conflicts { get; init; }
}
