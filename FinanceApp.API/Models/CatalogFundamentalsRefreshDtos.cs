using FinanceApp.Core.Models;

namespace FinanceApp.API.Models;

public sealed class CatalogFundamentalsRefreshStatusResponse
{
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime NextScheduledRunUtc { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
    public DayOfWeek ScheduledWeekday { get; init; }
    public TimeSpan LocalScheduleTime { get; init; }
    public bool Enabled { get; init; }
    public CatalogFundamentalsRefreshRunDetails? CurrentOrLatestRun { get; init; }
    public IReadOnlyList<string> RecentFailures { get; init; } = Array.Empty<string>();
}

public sealed class CatalogFundamentalsRefreshRunDetails
{
    public string RunKey { get; init; } = string.Empty;
    public DateOnly BusinessWeek { get; init; }
    public CatalogFundamentalsRefreshRunStatus Status { get; init; }
    public DateTime ScheduledAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public int LastProcessedStockId { get; init; }
    public int TotalDiscovered { get; init; }
    public int Processed { get; init; }
    public int Remaining { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int RateLimited { get; init; }
    public string? LastError { get; init; }
    public string? FailureSummary { get; init; }
}
