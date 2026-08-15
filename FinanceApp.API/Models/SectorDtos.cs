namespace FinanceApp.API.Models;

public sealed class SectorTreeItemDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string NormalizedName { get; init; } = string.Empty;
    public bool IsArchived { get; init; }
    public int SortOrder { get; init; }
    public int IndustryCount { get; init; }
    public int StockCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public IReadOnlyList<IndustryTreeItemDto> Industries { get; init; } = Array.Empty<IndustryTreeItemDto>();
}

public sealed class IndustryTreeItemDto
{
    public int Id { get; init; }
    public int SectorId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string NormalizedName { get; init; } = string.Empty;
    public bool IsArchived { get; init; }
    public int SortOrder { get; init; }
    public int StockCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed class UpsertSectorRequest
{
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

public sealed class UpsertIndustryRequest
{
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

public sealed class MoveIndustryRequest
{
    public int TargetSectorId { get; init; }
}
