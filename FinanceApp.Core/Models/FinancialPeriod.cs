namespace FinanceApp.Core.Models;

public enum PeriodType
{
    Annual,
    Quarterly
}

public class FinancialPeriod
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public PeriodType PeriodType { get; set; }
    public int? FiscalYear { get; set; }
    public int? FiscalQuarter { get; set; }
    public DateTime? PeriodEndDate { get; set; }
    public string? ReportedCurrency { get; set; }
    public decimal? Revenue { get; set; }
    public decimal? OperatingIncome { get; set; }
    public decimal? NetIncome { get; set; }
    public decimal? EpsReported { get; set; }
    public decimal? EpsEstimate { get; set; }
    public decimal? Ebitda { get; set; }
    public decimal? TotalDebt { get; set; }
    public decimal? TotalAssets { get; set; }
    public decimal? TotalLiabilities { get; set; }
    public decimal? FreeCashFlow { get; set; }
    public string Source { get; set; } = "Yahoo Finance";
    public DateTime? AsOfDate { get; set; }
    public DateTime FetchedAtUtc { get; set; }

    public CompanyFundamentalsSnapshot? Snapshot { get; set; }
}
