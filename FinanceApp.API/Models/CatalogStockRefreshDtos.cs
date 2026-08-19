using System.Text.Json.Serialization;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CatalogStockRefreshExecutionType
{
    Scheduled,
    StartupCatchUp,
    Manual
}

public sealed class CatalogStockRefreshStatusResponse
{
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime NextScheduledRunUtc { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
    public TimeSpan LocalScheduleTime { get; init; }
    public bool Enabled { get; init; }
    public CatalogStockRefreshRunDetails? CurrentOrLatestRun { get; init; }
}

public sealed class CatalogStockRefreshRunDetails
{
    public string RunKey { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public CatalogStockRefreshRunStatus Status { get; init; }
    public DateTime ScheduledAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public int LastProcessedStockId { get; init; }
    public int TotalDiscovered { get; init; }
    public int Processed { get; init; }
    public int Remaining { get; init; }
    public int QuoteSucceeded { get; init; }
    public int QuoteFailed { get; init; }
    public int QuoteSkipped { get; init; }
    public int HistorySucceeded { get; init; }
    public int HistoryFailed { get; init; }
    public int HistorySkipped { get; init; }
    public int RateLimited { get; init; }
    public string? LastError { get; init; }
    public string? FailureSummary { get; init; }
}
