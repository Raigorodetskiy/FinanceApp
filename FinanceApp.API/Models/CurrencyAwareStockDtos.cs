namespace FinanceApp.API.Models;

public sealed class StockQuoteResponse
{
    public string Symbol { get; init; } = string.Empty;
    public decimal RawCurrentPrice { get; init; }
    public decimal RawPreviousClose { get; init; }
    public decimal RawChange { get; init; }
    public string? Currency { get; init; }
    public string? FinancialCurrency { get; init; }
    public string? NormalizedQuoteCurrency { get; init; }
    public decimal QuoteUnitMultiplier { get; init; }
    public decimal NormalizedCurrentPrice { get; init; }
    public decimal NormalizedPreviousClose { get; init; }
    public decimal NormalizedChange { get; init; }
    public decimal? CurrentPriceEur { get; init; }
    public decimal? ChangeEur { get; init; }
    public decimal PercentChange { get; init; }
    public string MarketState { get; init; } = "CLOSED";
    /// <summary>
    /// Session that the returned price belongs to (e.g. "REGULAR", "LAST").
    /// Distinct from <see cref="MarketState"/>, which reflects the current trading session.
    /// </summary>
    public string PriceSession { get; init; } = "REGULAR";
    /// <summary>
    /// UTC timestamp of the price as reported by the provider.
    /// Null when the provider did not supply a reliable timestamp.
    /// </summary>
    public DateTime? PriceTimestampUtc { get; init; }
    /// <summary>
    /// True when <see cref="PriceTimestampUtc"/> is present and more than
    /// <c>24 hours</c> old at the time of the request.
    /// </summary>
    public bool IsStale { get; init; }
    /// <summary>
    /// Identifies which quote provider supplied the <see cref="RawCurrentPrice"/>.
    /// Populated only for experimental or non-default providers (e.g. <c>"finanzen.net"</c>).
    /// Null when the primary provider (Yahoo/Finnhub) is used.
    /// </summary>
    public string? PriceSource { get; init; }
    public decimal? RateToEur { get; init; }
    public DateTime? RateTimestampUtc { get; init; }
    public string? RateSource { get; init; }
    public string? ConversionWarning { get; init; }
}

public sealed class StockHistoryResponse
{
    public string Range { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string? Currency { get; init; }
    public string? FinancialCurrency { get; init; }
    public string? NormalizedQuoteCurrency { get; init; }
    public decimal QuoteUnitMultiplier { get; init; } = 1m;
    public decimal? RateToEur { get; init; }
    public DateTime? RateTimestampUtc { get; init; }
    public string? RateSource { get; init; }
    public string? ConversionWarning { get; init; }
    public IReadOnlyList<StockHistoryPointResponse> Points { get; init; } = Array.Empty<StockHistoryPointResponse>();
}

public sealed class StockHistoryPointResponse
{
    public DateTime Timestamp { get; init; }
    public string Interval { get; init; } = string.Empty;
    public decimal OpenRaw { get; init; }
    public decimal HighRaw { get; init; }
    public decimal LowRaw { get; init; }
    public decimal CloseRaw { get; init; }
    public decimal OpenNormalized { get; init; }
    public decimal HighNormalized { get; init; }
    public decimal LowNormalized { get; init; }
    public decimal CloseNormalized { get; init; }
    public decimal? OpenEur { get; init; }
    public decimal? HighEur { get; init; }
    public decimal? LowEur { get; init; }
    public decimal? CloseEur { get; init; }
    public long Volume { get; init; }
}
