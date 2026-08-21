using System.ComponentModel.DataAnnotations;

namespace FinanceApp.Core.Models;

public enum StockMetadataEnrichmentJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    CompletedWithWarnings = 3,
    Failed = 4,
    Cancelled = 5,
}

public enum StockMetadataEnrichmentScope
{
    MissingOnly = 0,
    RefreshStale = 1,
    Selected = 2,
    AllEligible = 3,
}

public enum StockMetadataEnrichmentDecision
{
    WouldApply = 0,
    Applied = 1,
    Unchanged = 2,
    Conflict = 3,
    Invalid = 4,
    NotFound = 5,
    NeedsReview = 6,
    RateLimited = 7,
    Failed = 8,
    Rejected = 9,
}

public enum StockMetadataEnrichmentConfidence
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public class StockMetadataEnrichmentJob
{
    public Guid Id { get; set; }
    public StockMetadataEnrichmentScope Scope { get; set; } = StockMetadataEnrichmentScope.MissingOnly;
    [MaxLength(2000)]
    public string? SelectedStockIdsJson { get; set; }
    public bool IsDryRun { get; set; } = true;
    public StockMetadataEnrichmentJobStatus Status { get; set; } = StockMetadataEnrichmentJobStatus.Queued;
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
    public int LastProcessedStockId { get; set; }
    public int RetryCount { get; set; }
    public DateTime? RetryAfterUtc { get; set; }
    [MaxLength(128)]
    public string? InitiatedByUserId { get; set; }
    [MaxLength(2000)]
    public string? DiagnosticSummary { get; set; }
    public DateTime? MetadataStaleAfterUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<StockMetadataEnrichmentResult> Results { get; set; } = new List<StockMetadataEnrichmentResult>();
}

public class StockMetadataEnrichmentResult
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public int StockId { get; set; }
    [MaxLength(50)]
    public string? ProviderSymbol { get; set; }
    [MaxLength(32)]
    public string? Exchange { get; set; }

    [MaxLength(12)]
    public string? OldIsin { get; set; }
    [MaxLength(12)]
    public string? CandidateIsin { get; set; }
    [MaxLength(6)]
    public string? OldWkn { get; set; }
    [MaxLength(6)]
    public string? CandidateWkn { get; set; }
    public int? OldIndustryId { get; set; }
    public int? CandidateIndustryId { get; set; }

    [MaxLength(200)]
    public string? RawProviderSector { get; set; }
    [MaxLength(200)]
    public string? RawProviderIndustry { get; set; }

    [MaxLength(100)]
    public string? IsinSource { get; set; }
    [MaxLength(100)]
    public string? WknSource { get; set; }
    [MaxLength(100)]
    public string? IndustrySource { get; set; }

    public StockMetadataEnrichmentConfidence IsinConfidence { get; set; }
    public StockMetadataEnrichmentConfidence WknConfidence { get; set; }
    public StockMetadataEnrichmentConfidence IndustryConfidence { get; set; }

    public StockMetadataEnrichmentDecision IsinDecision { get; set; }
    public StockMetadataEnrichmentDecision WknDecision { get; set; }
    public StockMetadataEnrichmentDecision IndustryDecision { get; set; }

    [MaxLength(1000)]
    public string? Diagnostics { get; set; }
    public bool ManuallyApproved { get; set; }
    public bool Rejected { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }

    public StockMetadataEnrichmentJob Job { get; set; } = null!;
}

public class StockMetadataIndustryMapping
{
    public int Id { get; set; }
    [MaxLength(100)]
    public string Provider { get; set; } = string.Empty;
    [MaxLength(200)]
    public string NormalizedSector { get; set; } = string.Empty;
    [MaxLength(200)]
    public string NormalizedIndustry { get; set; } = string.Empty;
    public int IndustryId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Industry Industry { get; set; } = null!;
}
