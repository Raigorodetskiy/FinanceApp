namespace FinanceApp.API.Models;

public sealed class MarketIndexDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string NormalizedName { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string NormalizedCode { get; init; } = string.Empty;
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
    public string? Description { get; init; }
    public string? CountryOrRegion { get; init; }
    public int SortOrder { get; init; }
}
