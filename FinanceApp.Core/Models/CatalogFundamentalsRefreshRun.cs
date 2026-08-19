namespace FinanceApp.Core.Models;

public enum CatalogFundamentalsRefreshRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    PausedRateLimited = 4,
    Failed = 5,
}

public sealed class CatalogFundamentalsRefreshRun
{
    public int Id { get; set; }
    public string RunKey { get; set; } = string.Empty;
    public DateOnly BusinessWeek { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public CatalogFundamentalsRefreshRunStatus Status { get; set; } = CatalogFundamentalsRefreshRunStatus.Pending;
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public int? LastProcessedStockId { get; set; }
    public int? PendingStockId { get; set; }
    public int TotalDiscovered { get; set; }
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int RateLimited { get; set; }
    public int Remaining { get; set; }
    public string? LastError { get; set; }
    public string? FailureSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
