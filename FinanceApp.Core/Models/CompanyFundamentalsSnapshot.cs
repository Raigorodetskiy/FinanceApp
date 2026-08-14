namespace FinanceApp.Core.Models;

public enum FundamentalsState
{
    Fresh,
    Stale,
    Unavailable
}

public class CompanyFundamentalsSnapshot
{
    public int Id { get; set; }
    /// <summary>The canonical Stock this snapshot belongs to.</summary>
    public int StockId { get; set; }
    /// <summary>Yahoo Finance symbol used to fetch this data (e.g. "STX" not "847.F").</summary>
    public string SourceSymbol { get; set; } = string.Empty;
    public decimal? MarketCap { get; set; }
    public decimal? EnterpriseValue { get; set; }
    public decimal? TotalDebt { get; set; }
    public decimal? CashAndEquivalents { get; set; }
    public decimal? RevenueTtm { get; set; }
    public decimal? NetIncomeTtm { get; set; }
    public decimal? EbitdaTtm { get; set; }
    public decimal? OperatingIncomeTtm { get; set; }
    public decimal? FreeCashFlowTtm { get; set; }
    public decimal? TotalAssets { get; set; }
    public decimal? TotalLiabilities { get; set; }
    public decimal? PeRatio { get; set; }
    public decimal? ForwardPeRatio { get; set; }
    public decimal? PbRatio { get; set; }
    public decimal? DividendYield { get; set; }
    public string? Currency { get; set; }
    /// <summary>Provider that supplied this data (e.g. "Yahoo Finance").</summary>
    public string Source { get; set; } = "Yahoo Finance";
    /// <summary>Provider's as-of date for the data.</summary>
    public DateTime? AsOfDate { get; set; }
    /// <summary>UTC time when this snapshot was fetched.</summary>
    public DateTime FetchedAtUtc { get; set; }

    public Stock? Stock { get; set; }
    public ICollection<FinancialPeriod> Periods { get; set; } = new List<FinancialPeriod>();
    public ICollection<EarningsEvent> EarningsEvents { get; set; } = new List<EarningsEvent>();
}
