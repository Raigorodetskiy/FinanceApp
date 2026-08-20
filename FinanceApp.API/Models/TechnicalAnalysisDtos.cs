namespace FinanceApp.API.Models;

public sealed class TechnicalAnalysisResponse
{
    public int StockId { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CommonName { get; init; } = string.Empty;
    public string Exchange { get; init; } = string.Empty;
    public string? Isin { get; init; }
    public string? Wkn { get; init; }
    public DateTime? AsOfUtc { get; init; }
    public bool IsPotentiallyStale { get; init; }
    public string HistoryRefreshCadence { get; init; } = string.Empty;
    public DateTime? LastIncrementalHistoryRefreshSucceededAtUtc { get; init; }
    public DateTime? NextIncrementalHistoryRefreshAtUtc { get; init; }
    public DateTime? LastHistoryReconciliationSucceededAtUtc { get; init; }
    public DateTime? NextHistoryReconciliationAtUtc { get; init; }
    public DateTime? LastFullHistoryBackfillSucceededAtUtc { get; init; }
    public DateTime? NextFullHistoryBackfillAtUtc { get; init; }
    public TechnicalAnalysisMetricsDto Metrics { get; init; } = new();
    public TechnicalAnalysisHorizonResultDto ThreeMonths { get; init; } = new();
    public TechnicalAnalysisHorizonResultDto SixMonths { get; init; } = new();
    public TechnicalAnalysisHorizonResultDto OneYear { get; init; } = new();
    public TechnicalAnalysisHorizonResultDto TwoYears { get; init; } = new();
    public IReadOnlyList<TechnicalAnalysisFactorDto> Warnings { get; init; } = Array.Empty<TechnicalAnalysisFactorDto>();
}

public sealed class TechnicalAnalysisMetricsDto
{
    public double? LatestPrice { get; init; }
    public int DailyCandleCount { get; init; }
    public double AdjustedCloseCoverage { get; init; }
    public double? Sma20 { get; init; }
    public double? Sma50 { get; init; }
    public double? Sma200 { get; init; }
    public double? Ema12 { get; init; }
    public double? Ema26 { get; init; }
    public double? Rsi14 { get; init; }
    public double? Macd { get; init; }
    public double? MacdSignal { get; init; }
    public double? MacdHistogram { get; init; }
    public double? Return1Month { get; init; }
    public double? Return3Months { get; init; }
    public double? Return6Months { get; init; }
    public double? Return1Year { get; init; }
    public double? VolatilityAnnualized20 { get; init; }
    public double? VolatilityAnnualized60 { get; init; }
    public double? MaxDrawdown { get; init; }
    public double? Atr14 { get; init; }
    public IReadOnlyList<TechnicalAnalysisPriceBasisDto> PriceBasis { get; init; } = Array.Empty<TechnicalAnalysisPriceBasisDto>();
}

public sealed class TechnicalAnalysisPriceBasisDto
{
    public string Metric { get; init; } = string.Empty;
    public string Basis { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class TechnicalAnalysisHorizonResultDto
{
    public double Score { get; init; }
    public string Signal { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public TechnicalAnalysisComponentScoresDto ComponentScores { get; init; } = new();
    public TechnicalAnalysisComponentWeightsDto ComponentWeights { get; init; } = new();
    public IReadOnlyList<TechnicalAnalysisFactorDto> PositiveFactors { get; init; } = Array.Empty<TechnicalAnalysisFactorDto>();
    public IReadOnlyList<TechnicalAnalysisFactorDto> NegativeFactors { get; init; } = Array.Empty<TechnicalAnalysisFactorDto>();
    public IReadOnlyList<TechnicalAnalysisFactorDto> Warnings { get; init; } = Array.Empty<TechnicalAnalysisFactorDto>();
}

public sealed class TechnicalAnalysisComponentScoresDto
{
    public double? Trend { get; init; }
    public double? Momentum { get; init; }
    public double? Returns { get; init; }
    public double? Risk { get; init; }
    public double? Fundamentals { get; init; }
}

public sealed class TechnicalAnalysisComponentWeightsDto
{
    public double Trend { get; init; }
    public double Momentum { get; init; }
    public double Returns { get; init; }
    public double Risk { get; init; }
    public double Fundamentals { get; init; }
}

public sealed class TechnicalAnalysisFactorDto
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
