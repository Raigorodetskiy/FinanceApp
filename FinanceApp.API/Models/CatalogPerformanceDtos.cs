namespace FinanceApp.API.Models;

/// <summary>Response for GET /api/Stocks/catalog/performance?range=...</summary>
public sealed class StockCatalogPerformanceResponse
{
    public string Range { get; init; } = string.Empty;
    public DateTime GeneratedAtUtc { get; init; }
    public IReadOnlyList<IndexConstituentPerformanceItemDto> Items { get; init; } = Array.Empty<IndexConstituentPerformanceItemDto>();
}
