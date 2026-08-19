namespace FinanceApp.Core.Models;

public enum CatalogStockRefreshRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    PausedRateLimited = 4,
    Failed = 5,
}

public sealed class CatalogStockRefreshRun
{
    public int Id { get; set; }
    public string RunKey { get; set; } = string.Empty;
    public DateOnly BusinessDate { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public CatalogStockRefreshRunStatus Status { get; set; } = CatalogStockRefreshRunStatus.Pending;

    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }

    public int? LastProcessedStockId { get; set; }
    public int? PendingStockId { get; set; }
    public bool PendingQuoteCompleted { get; set; }
    public bool PendingHistoryCompleted { get; set; }

    public int TotalDiscovered { get; set; }
    public int Processed { get; set; }
    public int QuoteSucceeded { get; set; }
    public int QuoteFailed { get; set; }
    public int QuoteSkipped { get; set; }
    public int HistorySucceeded { get; set; }
    public int HistoryFailed { get; set; }
    public int HistorySkipped { get; set; }
    public int RateLimited { get; set; }
    public int Remaining { get; set; }

    public string? LastError { get; set; }
    public string? FailureSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
