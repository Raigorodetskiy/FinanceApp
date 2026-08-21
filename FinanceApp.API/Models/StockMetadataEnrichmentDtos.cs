using FinanceApp.Core.Models;

namespace FinanceApp.API.Models;

public sealed class CreateStockMetadataEnrichmentJobRequest
{
    public StockMetadataEnrichmentScope Scope { get; set; } = StockMetadataEnrichmentScope.MissingOnly;
    public bool DryRun { get; set; } = true;
    public List<int>? SelectedStockIds { get; set; }
    public DateTime? MetadataStaleAfterUtc { get; set; }
}

public sealed class StockMetadataEnrichmentJobResponse
{
    public Guid JobId { get; set; }
    public StockMetadataEnrichmentScope Scope { get; set; }
    public bool IsDryRun { get; set; }
    public StockMetadataEnrichmentJobStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int TotalStocks { get; set; }
    public int ProcessedStocks { get; set; }
    public int SucceededStocks { get; set; }
    public int PartialStocks { get; set; }
    public int ReviewStocks { get; set; }
    public int ConflictStocks { get; set; }
    public int NotFoundStocks { get; set; }
    public int RateLimitedStocks { get; set; }
    public int FailedStocks { get; set; }
    public string? DiagnosticSummary { get; set; }
}

public sealed class StockMetadataEnrichmentResultResponse
{
    public long Id { get; set; }
    public int StockId { get; set; }
    public string? ProviderSymbol { get; set; }
    public string? Exchange { get; set; }
    public string? OldIsin { get; set; }
    public string? CandidateIsin { get; set; }
    public string? OldWkn { get; set; }
    public string? CandidateWkn { get; set; }
    public int? OldIndustryId { get; set; }
    public int? CandidateIndustryId { get; set; }
    public string? RawProviderSector { get; set; }
    public string? RawProviderIndustry { get; set; }
    public string? IsinSource { get; set; }
    public string? WknSource { get; set; }
    public string? IndustrySource { get; set; }
    public StockMetadataEnrichmentConfidence IsinConfidence { get; set; }
    public StockMetadataEnrichmentConfidence WknConfidence { get; set; }
    public StockMetadataEnrichmentConfidence IndustryConfidence { get; set; }
    public StockMetadataEnrichmentDecision IsinDecision { get; set; }
    public StockMetadataEnrichmentDecision WknDecision { get; set; }
    public StockMetadataEnrichmentDecision IndustryDecision { get; set; }
    public string? Diagnostics { get; set; }
    public bool ManuallyApproved { get; set; }
    public bool Rejected { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
}

public sealed class StockMetadataEnrichmentResultPageResponse
{
    public required IReadOnlyList<StockMetadataEnrichmentResultResponse> Items { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class ApplyStockMetadataEnrichmentJobRequest
{
    public bool OnlyManuallyApproved { get; set; } = false;
}

public sealed class ReviewStockMetadataEnrichmentResultRequest
{
    public bool Approve { get; set; }
    public int? IndustryId { get; set; }
    public bool SaveMapping { get; set; }
}

public sealed class RetryStockMetadataEnrichmentJobRequest
{
    public bool ResetProgress { get; set; } = false;
}
