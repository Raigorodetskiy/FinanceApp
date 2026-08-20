using System.Collections.Immutable;
using FinanceApp.API.Models;
using FinanceApp.Core.Services;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public interface IStockTechnicalAnalysisService
{
    Task<TechnicalAnalysisResponse?> GetTechnicalAnalysisAsync(int stockId, CancellationToken cancellationToken = default);
}

public sealed class StockTechnicalAnalysisService : IStockTechnicalAnalysisService
{
    private const int MaxDailyCandlesToLoad = 800;

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public StockTechnicalAnalysisService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<TechnicalAnalysisResponse?> GetTechnicalAnalysisAsync(int stockId, CancellationToken cancellationToken = default)
    {
        var stock = await _dbContext.Stocks
            .AsNoTracking()
            .Where(s => s.Id == stockId)
            .Select(s => new
            {
                s.Id,
                s.Ticker,
                s.Name,
                s.CommonName,
                s.Exchange,
                s.Isin,
                s.Wkn,
                s.HistoryRefreshCadence,
                s.LastIncrementalHistoryRefreshSucceededAtUtc,
                s.NextIncrementalHistoryRefreshAtUtc,
                s.LastHistoryReconciliationSucceededAtUtc,
                s.NextHistoryReconciliationAtUtc,
                s.LastFullHistoryBackfillSucceededAtUtc,
                s.NextFullHistoryBackfillAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (stock is null)
        {
            return null;
        }

        var rawCandles = await _dbContext.StockHistoricalPrices
            .AsNoTracking()
            .Where(p => p.StockId == stockId && p.Interval == "1d")
            .OrderByDescending(p => p.Timestamp)
            .ThenByDescending(p => p.Id)
            .Take(MaxDailyCandlesToLoad)
            .Select(p => new TechnicalAnalysisScoring.RawDailyCandle(
                p.Id,
                p.Timestamp,
                p.Open,
                p.High,
                p.Low,
                p.Close,
                p.AdjustedClose,
                p.Volume))
            .ToListAsync(cancellationToken);

        var fundamentals = await _dbContext.FundamentalsSnapshots
            .AsNoTracking()
            .Where(x => x.StockId == stockId)
            .OrderByDescending(x => x.FetchedAtUtc)
            .Select(x => new TechnicalAnalysisScoring.FundamentalsSnapshotInput(
                x.FetchedAtUtc,
                x.AsOfDate,
                x.MarketCap,
                x.TotalDebt,
                x.CashAndEquivalents,
                x.EbitdaTtm,
                x.NetIncomeTtm,
                x.FreeCashFlowTtm,
                x.PeRatio,
                x.PbRatio,
                x.DividendYield))
            .FirstOrDefaultAsync(cancellationToken);

        var fundamentalsPeriodRange = await _dbContext.FinancialPeriods
            .AsNoTracking()
            .Where(p => p.Snapshot != null && p.Snapshot.StockId == stockId)
            .GroupBy(_ => 1)
            .Select(g => new TechnicalAnalysisScoring.FundamentalPeriodRangeInput(
                g.Count(),
                g.Min(x => x.PeriodEndDate),
                g.Max(x => x.PeriodEndDate)))
            .FirstOrDefaultAsync(cancellationToken);

        var scoringInput = new TechnicalAnalysisScoring.Input(
            rawCandles,
            fundamentals,
            fundamentalsPeriodRange,
            _timeProvider.GetUtcNow().UtcDateTime);

        var computed = TechnicalAnalysisScoring.Compute(scoringInput);

        return new TechnicalAnalysisResponse
        {
            StockId = stock.Id,
            Ticker = stock.Ticker,
            Name = stock.Name,
            CommonName = stock.CommonName,
            Exchange = stock.Exchange,
            Isin = stock.Isin,
            Wkn = stock.Wkn,
            AsOfUtc = computed.AsOfUtc,
            IsPotentiallyStale = computed.IsPotentiallyStale,
            HistoryRefreshCadence = stock.HistoryRefreshCadence.ToString(),
            LastIncrementalHistoryRefreshSucceededAtUtc = stock.LastIncrementalHistoryRefreshSucceededAtUtc,
            NextIncrementalHistoryRefreshAtUtc = stock.NextIncrementalHistoryRefreshAtUtc,
            LastHistoryReconciliationSucceededAtUtc = stock.LastHistoryReconciliationSucceededAtUtc,
            NextHistoryReconciliationAtUtc = stock.NextHistoryReconciliationAtUtc,
            LastFullHistoryBackfillSucceededAtUtc = stock.LastFullHistoryBackfillSucceededAtUtc,
            NextFullHistoryBackfillAtUtc = stock.NextFullHistoryBackfillAtUtc,
            Metrics = computed.Metrics,
            ThreeMonths = computed.ThreeMonths,
            SixMonths = computed.SixMonths,
            OneYear = computed.OneYear,
            TwoYears = computed.TwoYears,
            Warnings = computed.Warnings
        };
    }
}

public static class TechnicalAnalysisScoring
{
    public const int TradingDays3Months = 63;
    public const int TradingDays6Months = 126;
    public const int TradingDays1Year = 252;
    public const int TradingDays2Years = 504;

    private static readonly ImmutableDictionary<string, TechnicalAnalysisComponentWeightsDto> WeightsByHorizon =
        new Dictionary<string, TechnicalAnalysisComponentWeightsDto>(StringComparer.Ordinal)
        {
            ["ThreeMonths"] = new() { Trend = 0.35, Momentum = 0.35, Returns = 0.20, Risk = 0.10, Fundamentals = 0.0 },
            ["SixMonths"] = new() { Trend = 0.35, Momentum = 0.25, Returns = 0.20, Risk = 0.15, Fundamentals = 0.05 },
            ["OneYear"] = new() { Trend = 0.30, Momentum = 0.15, Returns = 0.20, Risk = 0.15, Fundamentals = 0.20 },
            ["TwoYears"] = new() { Trend = 0.15, Momentum = 0.05, Returns = 0.15, Risk = 0.20, Fundamentals = 0.45 }
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, TechnicalAnalysisComponentWeightsDto> HorizonWeights => WeightsByHorizon;

    public sealed record RawDailyCandle(
        int Id,
        DateTime Timestamp,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal? AdjustedClose,
        long Volume);

    public sealed record FundamentalsSnapshotInput(
        DateTime FetchedAtUtc,
        DateTime? AsOfDate,
        decimal? MarketCap,
        decimal? TotalDebt,
        decimal? CashAndEquivalents,
        decimal? EbitdaTtm,
        decimal? NetIncomeTtm,
        decimal? FreeCashFlowTtm,
        decimal? PeRatio,
        decimal? PbRatio,
        decimal? DividendYield);

    public sealed record FundamentalPeriodRangeInput(
        int PeriodCount,
        DateTime? MinPeriodEndDate,
        DateTime? MaxPeriodEndDate);

    public sealed record Input(
        IReadOnlyList<RawDailyCandle> Candles,
        FundamentalsSnapshotInput? Fundamentals,
        FundamentalPeriodRangeInput? FundamentalPeriods,
        DateTime UtcNow);

    private sealed record NormalizedCandle(
        DateTime Date,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal? AdjustedClose,
        decimal EffectiveClose,
        bool UsedAdjusted);

    public sealed record Computed(
        DateTime? AsOfUtc,
        bool IsPotentiallyStale,
        TechnicalAnalysisMetricsDto Metrics,
        TechnicalAnalysisHorizonResultDto ThreeMonths,
        TechnicalAnalysisHorizonResultDto SixMonths,
        TechnicalAnalysisHorizonResultDto OneYear,
        TechnicalAnalysisHorizonResultDto TwoYears,
        IReadOnlyList<TechnicalAnalysisFactorDto> Warnings);

    public static Computed Compute(Input input)
    {
        ValidateWeightConfiguration();

        var warnings = new List<TechnicalAnalysisFactorDto>();
        var normalized = Normalize(input.Candles, warnings);

        if (normalized.Count == 0)
        {
            warnings.Add(Factor("HISTORY_MISSING", "No usable daily history is available for technical analysis."));
            var emptyMetrics = new TechnicalAnalysisMetricsDto
            {
                DailyCandleCount = 0,
                AdjustedCloseCoverage = 0
            };

            return new Computed(
                null,
                true,
                emptyMetrics,
                BuildHorizonResult("ThreeMonths", emptyMetrics, normalized.Count, null, input.Fundamentals, input.FundamentalPeriods, input.UtcNow, warnings),
                BuildHorizonResult("SixMonths", emptyMetrics, normalized.Count, null, input.Fundamentals, input.FundamentalPeriods, input.UtcNow, warnings),
                BuildHorizonResult("OneYear", emptyMetrics, normalized.Count, null, input.Fundamentals, input.FundamentalPeriods, input.UtcNow, warnings),
                BuildHorizonResult("TwoYears", emptyMetrics, normalized.Count, null, input.Fundamentals, input.FundamentalPeriods, input.UtcNow, warnings),
                SortFactors(warnings));
        }

        var closes = normalized.Select(c => (double)c.EffectiveClose).ToArray();
        var asOf = normalized[^1].Date;

        var sma20 = TechnicalIndicators.CalcSma(closes, 20);
        var sma50 = TechnicalIndicators.CalcSma(closes, 50);
        var sma200 = TechnicalIndicators.CalcSma(closes, 200);
        var ema12 = TechnicalIndicators.CalcEma(closes, 12);
        var ema26 = TechnicalIndicators.CalcEma(closes, 26);
        var rsi14 = TechnicalIndicators.CalcRsi14(closes);
        var macd = TechnicalIndicators.CalcMacd(closes);
        var return1Month = ReturnPercent(closes, 21);
        var return3Months = ReturnPercent(closes, TradingDays3Months);
        var return6Months = ReturnPercent(closes, TradingDays6Months);
        var return1Year = ReturnPercent(closes, TradingDays1Year);
        var vol20 = TechnicalIndicators.CalcVolatility(closes, 20);
        var vol60 = TechnicalIndicators.CalcVolatility(closes, 60);
        var drawdown = TechnicalIndicators.CalcCurrentDrawdown(closes);
        var atr14 = TechnicalIndicators.CalcAtr14(
            normalized.Select(c => new TechnicalIndicators.DailyObservation(c.Date, c.Open, c.High, c.Low, c.Close, 0, c.AdjustedClose)).ToList());

        var adjustedCoverage = normalized.Count == 0
            ? 0d
            : normalized.Count(c => c.UsedAdjusted) / (double)normalized.Count;

        if (sma200 is null)
        {
            warnings.Add(Factor("SMA200_UNAVAILABLE", "SMA200 is unavailable due to insufficient daily history."));
        }

        if (adjustedCoverage < 1d)
        {
            warnings.Add(Factor("ADJUSTED_CLOSE_INCOMPLETE", "AdjustedClose coverage is incomplete; Close fallback was used for part of the history."));
        }

        if (closes.Distinct().Count() == 1)
        {
            warnings.Add(Factor("CONSTANT_PRICE_SERIES", "Price series is constant in the loaded analysis window."));
        }

        var latestAgeDays = (input.UtcNow.Date - asOf.Date).TotalDays;
        var isPotentiallyStale = latestAgeDays > 3;
        if (isPotentiallyStale)
        {
            warnings.Add(Factor("HISTORY_STALE", "Latest candle is stale relative to current UTC date."));
        }

        var metrics = new TechnicalAnalysisMetricsDto
        {
            LatestPrice = closes[^1],
            DailyCandleCount = normalized.Count,
            AdjustedCloseCoverage = Clamp(adjustedCoverage, 0, 1),
            Sma20 = sma20,
            Sma50 = sma50,
            Sma200 = sma200,
            Ema12 = ema12,
            Ema26 = ema26,
            Rsi14 = rsi14,
            Macd = macd?.MacdLine,
            MacdSignal = macd?.SignalLine,
            MacdHistogram = macd?.Histogram,
            Return1Month = return1Month,
            Return3Months = return3Months,
            Return6Months = return6Months,
            Return1Year = return1Year,
            VolatilityAnnualized20 = vol20,
            VolatilityAnnualized60 = vol60,
            MaxDrawdown = drawdown,
            Atr14 = atr14,
            PriceBasis = new[]
            {
                new TechnicalAnalysisPriceBasisDto
                {
                    Metric = "CloseBasedIndicators",
                    Basis = "AdjustedClosePreferredWithPerPointCloseFallback",
                    Reason = "Each candle uses AdjustedClose when valid; otherwise Close for that candle."
                },
                new TechnicalAnalysisPriceBasisDto
                {
                    Metric = "ATR14",
                    Basis = "UnadjustedOHLC",
                    Reason = "ATR uses unadjusted OHLC because adjusted OHLC is not persisted."
                }
            }
        };

        return new Computed(
            asOf,
            isPotentiallyStale,
            metrics,
            BuildHorizonResult("ThreeMonths", metrics, normalized.Count, asOf, input.Fundamentals, input.FundamentalPeriods, input.UtcNow, warnings),
            BuildHorizonResult("SixMonths", metrics, normalized.Count, asOf, input.Fundamentals, input.FundamentalPeriods, input.UtcNow, warnings),
            BuildHorizonResult("OneYear", metrics, normalized.Count, asOf, input.Fundamentals, input.FundamentalPeriods, input.UtcNow, warnings),
            BuildHorizonResult("TwoYears", metrics, normalized.Count, asOf, input.Fundamentals, input.FundamentalPeriods, input.UtcNow, warnings),
            SortFactors(warnings));
    }

    public static string ToSignal(double score)
    {
        var bounded = Clamp(score, 0, 100);
        return bounded switch
        {
            >= 80 => "StrongBullish",
            >= 65 => "ModeratelyBullish",
            >= 45 => "Neutral",
            >= 30 => "ModeratelyBearish",
            _ => "StrongBearish"
        };
    }

    public static void ValidateWeightConfiguration()
    {
        foreach (var entry in WeightsByHorizon)
        {
            var w = entry.Value;
            var sum = w.Trend + w.Momentum + w.Returns + w.Risk + w.Fundamentals;
            if (Math.Abs(sum - 1.0) > 1e-9)
            {
                throw new InvalidOperationException($"Weights for {entry.Key} must sum to 1.0 exactly.");
            }
        }
    }

    private static TechnicalAnalysisHorizonResultDto BuildHorizonResult(
        string horizon,
        TechnicalAnalysisMetricsDto metrics,
        int candleCount,
        DateTime? asOf,
        FundamentalsSnapshotInput? fundamentals,
        FundamentalPeriodRangeInput? fundamentalPeriods,
        DateTime utcNow,
        IReadOnlyList<TechnicalAnalysisFactorDto> sharedWarnings)
    {
        var positive = new List<TechnicalAnalysisFactorDto>();
        var negative = new List<TechnicalAnalysisFactorDto>();
        var warnings = new List<TechnicalAnalysisFactorDto>(sharedWarnings);

        var trend = ScoreTrend(metrics, positive, negative, warnings);
        var momentum = ScoreMomentum(metrics, positive, negative, warnings);
        var returns = ScoreReturns(horizon, metrics, positive, negative, warnings);
        var risk = ScoreRisk(metrics, positive, negative, warnings);
        var fundamentalsScore = ScoreFundamentals(horizon, fundamentals, fundamentalPeriods, utcNow, positive, negative, warnings);

        var weights = WeightsByHorizon[horizon];

        var weightedScore = 0d;
        var availableWeight = 0d;
        AddComponent(ref weightedScore, ref availableWeight, trend, weights.Trend);
        AddComponent(ref weightedScore, ref availableWeight, momentum, weights.Momentum);
        AddComponent(ref weightedScore, ref availableWeight, returns, weights.Returns);
        AddComponent(ref weightedScore, ref availableWeight, risk, weights.Risk);
        AddComponent(ref weightedScore, ref availableWeight, fundamentalsScore, weights.Fundamentals);

        if (availableWeight < 1d)
        {
            warnings.Add(Factor("COMPONENTS_MISSING", "One or more scoring components are unavailable; final score renormalizes available component weights."));
        }

        var score = availableWeight <= 0
            ? 50d
            : weightedScore / availableWeight;

        var confidence = CalculateConfidence(
            horizon,
            candleCount,
            asOf,
            metrics,
            fundamentals,
            fundamentalPeriods,
            warnings,
            availableWeight,
            utcNow);

        return new TechnicalAnalysisHorizonResultDto
        {
            Score = Clamp(score, 0, 100),
            Signal = ToSignal(score),
            Confidence = Clamp(confidence, 0, 1),
            ComponentScores = new TechnicalAnalysisComponentScoresDto
            {
                Trend = trend,
                Momentum = momentum,
                Returns = returns,
                Risk = risk,
                Fundamentals = fundamentalsScore
            },
            ComponentWeights = weights,
            PositiveFactors = SortFactors(positive),
            NegativeFactors = SortFactors(negative),
            Warnings = SortFactors(warnings)
        };
    }

    private static List<NormalizedCandle> Normalize(
        IReadOnlyList<RawDailyCandle> candles,
        List<TechnicalAnalysisFactorDto> warnings)
    {
        if (candles.Count == 0)
        {
            return new List<NormalizedCandle>();
        }

        var duplicatesRemoved = 0;
        var invalidClosePoints = 0;

        var deduped = candles
            .OrderBy(c => c.Timestamp)
            .ThenBy(c => c.Id)
            .GroupBy(c => c.Timestamp)
            .Select(g =>
            {
                if (g.Count() > 1)
                {
                    duplicatesRemoved += g.Count() - 1;
                }

                return g.Last();
            })
            .ToList();

        var normalized = new List<NormalizedCandle>(deduped.Count);
        foreach (var candle in deduped)
        {
            var effectiveClose = PickEffectiveClose(candle.Close, candle.AdjustedClose, out var usedAdjusted);
            if (effectiveClose is null || effectiveClose <= 0)
            {
                invalidClosePoints++;
                continue;
            }

            normalized.Add(new NormalizedCandle(
                candle.Timestamp,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.AdjustedClose,
                effectiveClose.Value,
                usedAdjusted));
        }

        if (duplicatesRemoved > 0)
        {
            warnings.Add(Factor("DUPLICATE_CANDLES", $"Removed {duplicatesRemoved} duplicate daily candles by timestamp (last value kept)."));
        }

        if (invalidClosePoints > 0)
        {
            warnings.Add(Factor("INVALID_CLOSE_POINTS", $"Excluded {invalidClosePoints} candles with missing, non-positive, or unusable close values."));
        }

        return normalized;
    }

    private static decimal? PickEffectiveClose(decimal close, decimal? adjustedClose, out bool usedAdjusted)
    {
        if (adjustedClose.HasValue && adjustedClose.Value > 0)
        {
            usedAdjusted = true;
            return adjustedClose.Value;
        }

        usedAdjusted = false;
        return close > 0 ? close : null;
    }

    private static double? ScoreTrend(
        TechnicalAnalysisMetricsDto metrics,
        List<TechnicalAnalysisFactorDto> positive,
        List<TechnicalAnalysisFactorDto> negative,
        List<TechnicalAnalysisFactorDto> warnings)
    {
        if (metrics.LatestPrice is null)
        {
            warnings.Add(Factor("TREND_MISSING_PRICE", "Trend score unavailable because latest price is missing."));
            return null;
        }

        var score = 50d;

        if (metrics.Sma50.HasValue)
        {
            if (metrics.LatestPrice > metrics.Sma50.Value)
            {
                score += 10;
                positive.Add(Factor("PRICE_ABOVE_SMA50", "Latest price is above SMA50."));
            }
            else
            {
                score -= 10;
                negative.Add(Factor("PRICE_BELOW_SMA50", "Latest price is below SMA50."));
            }
        }

        if (metrics.Sma200.HasValue)
        {
            if (metrics.LatestPrice > metrics.Sma200.Value)
            {
                score += 10;
                positive.Add(Factor("PRICE_ABOVE_SMA200", "Latest price is above SMA200."));
            }
            else
            {
                score -= 10;
                negative.Add(Factor("PRICE_BELOW_SMA200", "Latest price is below SMA200."));
            }
        }

        if (metrics.Sma50.HasValue && metrics.Sma200.HasValue)
        {
            if (metrics.Sma50 > metrics.Sma200)
            {
                score += 15;
                positive.Add(Factor("MA_ORDER_BULLISH", "Moving-average ordering is bullish (SMA50 > SMA200)."));
            }
            else
            {
                score -= 15;
                negative.Add(Factor("MA_ORDER_BEARISH", "Moving-average ordering is bearish (SMA50 <= SMA200)."));
            }
        }

        if (metrics.Sma20.HasValue && metrics.Sma50.HasValue)
        {
            if (metrics.Sma20 > metrics.Sma50)
            {
                score += 8;
                positive.Add(Factor("SMA20_ABOVE_SMA50", "Short-term trend confirmation (SMA20 > SMA50)."));
            }
            else
            {
                score -= 8;
                negative.Add(Factor("SMA20_BELOW_SMA50", "Short-term trend pressure (SMA20 <= SMA50)."));
            }
        }

        return Clamp(score, 0, 100);
    }

    private static double? ScoreMomentum(
        TechnicalAnalysisMetricsDto metrics,
        List<TechnicalAnalysisFactorDto> positive,
        List<TechnicalAnalysisFactorDto> negative,
        List<TechnicalAnalysisFactorDto> warnings)
    {
        var hasAny = false;
        var score = 50d;

        if (metrics.Rsi14.HasValue)
        {
            hasAny = true;
            var rsi = metrics.Rsi14.Value;
            if (rsi is >= 55 and <= 70)
            {
                score += 10;
                positive.Add(Factor("RSI_BULLISH_RANGE", "RSI is in constructive bullish range."));
            }
            else if (rsi > 70)
            {
                score += 2;
                warnings.Add(Factor("RSI_OVERBOUGHT", "RSI is overbought; bullish momentum may be extended."));
            }
            else if (rsi is >= 45 and < 55)
            {
                // near-neutral
            }
            else if (rsi >= 30)
            {
                score -= 8;
                negative.Add(Factor("RSI_WEAK", "RSI indicates weak momentum."));
            }
            else
            {
                score -= 2;
                warnings.Add(Factor("RSI_OVERSOLD", "RSI is oversold; downside momentum may be stretched."));
            }
        }

        if (metrics.MacdHistogram.HasValue)
        {
            hasAny = true;
            if (metrics.MacdHistogram.Value >= 0)
            {
                score += 12;
                positive.Add(Factor("MACD_HISTOGRAM_POSITIVE", "MACD histogram is positive."));
            }
            else
            {
                score -= 12;
                negative.Add(Factor("MACD_HISTOGRAM_NEGATIVE", "MACD histogram is negative."));
            }
        }

        if (metrics.Ema12.HasValue && metrics.Ema26.HasValue)
        {
            hasAny = true;
            if (metrics.Ema12 > metrics.Ema26)
            {
                score += 8;
                positive.Add(Factor("EMA_BULLISH", "EMA12 is above EMA26."));
            }
            else
            {
                score -= 8;
                negative.Add(Factor("EMA_BEARISH", "EMA12 is below or equal to EMA26."));
            }
        }

        if (!hasAny)
        {
            warnings.Add(Factor("MOMENTUM_MISSING", "Momentum score unavailable because RSI/MACD/EMA metrics are missing."));
            return null;
        }

        return Clamp(score, 0, 100);
    }

    private static double? ScoreReturns(
        string horizon,
        TechnicalAnalysisMetricsDto metrics,
        List<TechnicalAnalysisFactorDto> positive,
        List<TechnicalAnalysisFactorDto> negative,
        List<TechnicalAnalysisFactorDto> warnings)
    {
        var returnInputs = horizon switch
        {
            "ThreeMonths" => new[] { (Value: metrics.Return1Month, Weight: 0.4), (Value: metrics.Return3Months, Weight: 0.6) },
            "SixMonths" => new[] { (Value: metrics.Return3Months, Weight: 0.4), (Value: metrics.Return6Months, Weight: 0.6) },
            "OneYear" => new[] { (Value: metrics.Return6Months, Weight: 0.4), (Value: metrics.Return1Year, Weight: 0.6) },
            "TwoYears" => new[] { (Value: metrics.Return1Year, Weight: 1.0) },
            _ => Array.Empty<(double? Value, double Weight)>()
        };

        var available = returnInputs.Where(x => x.Value.HasValue).ToList();
        if (available.Count == 0)
        {
            warnings.Add(Factor("RETURNS_MISSING", "Returns score unavailable due to insufficient return windows."));
            return null;
        }

        var totalWeight = available.Sum(x => x.Weight);
        var weightedReturn = available.Sum(x => x.Value!.Value * x.Weight) / totalWeight;
        var boundedContribution = Clamp(weightedReturn, -25, 25);
        var score = 50d + boundedContribution;

        if (weightedReturn >= 0)
        {
            positive.Add(Factor("RETURN_POSITIVE", $"Horizon-aligned return is positive ({weightedReturn:F2}%)."));
        }
        else
        {
            negative.Add(Factor("RETURN_NEGATIVE", $"Horizon-aligned return is negative ({weightedReturn:F2}%)."));
        }

        return Clamp(score, 0, 100);
    }

    private static double? ScoreRisk(
        TechnicalAnalysisMetricsDto metrics,
        List<TechnicalAnalysisFactorDto> positive,
        List<TechnicalAnalysisFactorDto> negative,
        List<TechnicalAnalysisFactorDto> warnings)
    {
        var hasAny = false;
        var score = 60d;

        if (metrics.VolatilityAnnualized60.HasValue)
        {
            hasAny = true;
            var volPct = metrics.VolatilityAnnualized60.Value * 100d;
            if (volPct <= 20)
            {
                score += 12;
                positive.Add(Factor("VOLATILITY_MODERATE", "Annualized volatility is low-to-moderate."));
            }
            else if (volPct > 40)
            {
                score -= 20;
                negative.Add(Factor("VOLATILITY_ELEVATED", "Annualized volatility is elevated."));
            }
            else if (volPct > 30)
            {
                score -= 10;
                negative.Add(Factor("VOLATILITY_HIGH", "Annualized volatility is above moderate range."));
            }
        }

        if (metrics.MaxDrawdown.HasValue)
        {
            hasAny = true;
            var drawdown = metrics.MaxDrawdown.Value;
            if (drawdown >= -10)
            {
                score += 10;
                positive.Add(Factor("DRAWDOWN_CONTAINED", "Maximum drawdown is contained."));
            }
            else if (drawdown < -35)
            {
                score -= 20;
                negative.Add(Factor("DRAWDOWN_SEVERE", "Maximum drawdown is severe."));
            }
            else if (drawdown < -20)
            {
                score -= 10;
                negative.Add(Factor("DRAWDOWN_ELEVATED", "Maximum drawdown is elevated."));
            }
        }

        if (metrics.Atr14.HasValue && metrics.LatestPrice.HasValue && metrics.LatestPrice.Value > 0)
        {
            hasAny = true;
            var atrPct = metrics.Atr14.Value / metrics.LatestPrice.Value * 100d;
            if (atrPct <= 2)
            {
                score += 6;
                positive.Add(Factor("ATR_CONTAINED", "ATR14 is low relative to price."));
            }
            else if (atrPct > 5)
            {
                score -= 12;
                negative.Add(Factor("ATR_ELEVATED", "ATR14 is elevated relative to price."));
            }
        }

        if (!hasAny)
        {
            warnings.Add(Factor("RISK_MISSING", "Risk score unavailable because volatility/drawdown/ATR metrics are missing."));
            return null;
        }

        return Clamp(score, 0, 100);
    }

    private static double? ScoreFundamentals(
        string horizon,
        FundamentalsSnapshotInput? fundamentals,
        FundamentalPeriodRangeInput? fundamentalPeriods,
        DateTime utcNow,
        List<TechnicalAnalysisFactorDto> positive,
        List<TechnicalAnalysisFactorDto> negative,
        List<TechnicalAnalysisFactorDto> warnings)
    {
        if (horizon == "ThreeMonths")
        {
            return null;
        }

        if (fundamentals is null)
        {
            warnings.Add(Factor("FUNDAMENTALS_MISSING", "Persisted fundamentals snapshot is unavailable."));
            return null;
        }

        var ageDays = (utcNow - fundamentals.FetchedAtUtc).TotalDays;
        if (ageDays > 35)
        {
            warnings.Add(Factor("FUNDAMENTALS_STALE", "Persisted fundamentals snapshot is stale."));
        }

        if (horizon == "TwoYears")
        {
            var spanDays = fundamentalPeriods?.MinPeriodEndDate.HasValue == true && fundamentalPeriods.MaxPeriodEndDate.HasValue
                ? (fundamentalPeriods.MaxPeriodEndDate.Value - fundamentalPeriods.MinPeriodEndDate.Value).TotalDays
                : 0;

            if (fundamentalPeriods is null || fundamentalPeriods.PeriodCount < 8 || spanDays < 540)
            {
                warnings.Add(Factor("FUNDAMENTAL_HISTORY_INSUFFICIENT", "Insufficient historical fundamental data for multi-year trend confirmation."));
            }
        }

        var score = 50d;
        var appliedSignals = 0;

        if (fundamentals.NetIncomeTtm.HasValue)
        {
            appliedSignals++;
            if (fundamentals.NetIncomeTtm.Value > 0)
            {
                score += 8;
                positive.Add(Factor("NET_INCOME_POSITIVE", "Net income TTM is positive."));
            }
            else
            {
                score -= 8;
                negative.Add(Factor("NET_INCOME_NEGATIVE", "Net income TTM is negative."));
            }
        }

        if (fundamentals.FreeCashFlowTtm.HasValue)
        {
            appliedSignals++;
            if (fundamentals.FreeCashFlowTtm.Value > 0)
            {
                score += 7;
                positive.Add(Factor("FCF_POSITIVE", "Free cash flow TTM is positive."));
            }
            else
            {
                score -= 7;
                negative.Add(Factor("FCF_NEGATIVE", "Free cash flow TTM is negative."));
            }
        }

        if (fundamentals.TotalDebt.HasValue && fundamentals.EbitdaTtm.HasValue && fundamentals.EbitdaTtm.Value > 0)
        {
            appliedSignals++;
            var debtToEbitda = (double)(fundamentals.TotalDebt.Value / fundamentals.EbitdaTtm.Value);
            if (debtToEbitda < 2)
            {
                score += 8;
                positive.Add(Factor("LEVERAGE_LOW", "Debt-to-EBITDA appears conservative."));
            }
            else if (debtToEbitda > 6)
            {
                score -= 10;
                negative.Add(Factor("LEVERAGE_HIGH", "Debt-to-EBITDA appears elevated."));
            }
            else if (debtToEbitda > 4)
            {
                score -= 4;
                negative.Add(Factor("LEVERAGE_MODERATE_HIGH", "Debt-to-EBITDA is above moderate range."));
            }
        }

        if (fundamentals.PeRatio.HasValue && fundamentals.PeRatio.Value > 0)
        {
            appliedSignals++;
            var pe = (double)fundamentals.PeRatio.Value;
            if (pe is >= 5 and <= 35)
            {
                score += 5;
                positive.Add(Factor("PE_WITHIN_RANGE", "P/E is within a broad neutral-to-constructive range."));
            }
            else if (pe > 60)
            {
                score -= 6;
                negative.Add(Factor("PE_ELEVATED", "P/E is elevated relative to broad range."));
            }
        }

        if (fundamentals.PbRatio.HasValue && fundamentals.PbRatio.Value > 0)
        {
            appliedSignals++;
            var pb = (double)fundamentals.PbRatio.Value;
            if (pb is >= 0.5 and <= 8)
            {
                score += 3;
                positive.Add(Factor("PB_WITHIN_RANGE", "P/B is within a broad neutral range."));
            }
            else if (pb > 15)
            {
                score -= 6;
                negative.Add(Factor("PB_ELEVATED", "P/B is elevated relative to broad range."));
            }
        }

        if (fundamentals.DividendYield.HasValue && fundamentals.DividendYield.Value > 0)
        {
            appliedSignals++;
            var dy = (double)fundamentals.DividendYield.Value;
            if (dy is >= 1 and <= 6)
            {
                score += 3;
                positive.Add(Factor("DIVIDEND_YIELD_BALANCED", "Dividend yield is within a moderate range."));
            }
            else if (dy > 10)
            {
                score -= 3;
                warnings.Add(Factor("DIVIDEND_YIELD_EXTREME", "Dividend yield is unusually high and may be unstable."));
            }
        }

        if (appliedSignals == 0)
        {
            warnings.Add(Factor("FUNDAMENTALS_UNUSABLE", "Fundamentals snapshot is present but lacks usable scoring fields."));
            return null;
        }

        return Clamp(score, 0, 100);
    }

    private static double CalculateConfidence(
        string horizon,
        int candleCount,
        DateTime? asOf,
        TechnicalAnalysisMetricsDto metrics,
        FundamentalsSnapshotInput? fundamentals,
        FundamentalPeriodRangeInput? fundamentalPeriods,
        List<TechnicalAnalysisFactorDto> warnings,
        double availableComponentWeight,
        DateTime utcNow)
    {
        var required = horizon switch
        {
            "ThreeMonths" => TradingDays3Months,
            "SixMonths" => TradingDays6Months,
            "OneYear" => TradingDays1Year,
            "TwoYears" => TradingDays2Years,
            _ => TradingDays3Months
        };

        var coverage = Clamp(required == 0 ? 1 : candleCount / (double)required, 0, 1);

        var freshnessFactor = 0.2;
        if (asOf.HasValue)
        {
            var ageDays = (utcNow.Date - asOf.Value.Date).TotalDays;
            freshnessFactor = ageDays switch
            {
                <= 2 => 1.0,
                <= 5 => 0.8,
                <= 10 => 0.55,
                <= 20 => 0.35,
                _ => 0.2
            };
        }

        var adjustedCoverageFactor = 0.6 + 0.4 * Clamp(metrics.AdjustedCloseCoverage, 0, 1);
        var componentFactor = Clamp(availableComponentWeight, 0, 1);

        var confidence = coverage * 0.45
            + freshnessFactor * 0.20
            + adjustedCoverageFactor * 0.15
            + componentFactor * 0.20;

        if (horizon is "OneYear" or "TwoYears")
        {
            if (fundamentals is null)
            {
                confidence *= 0.7;
            }
            else
            {
                var ageDays = (utcNow - fundamentals.FetchedAtUtc).TotalDays;
                if (ageDays > 35)
                {
                    confidence *= 0.8;
                }
            }

            if (horizon == "TwoYears")
            {
                var spanDays = fundamentalPeriods?.MinPeriodEndDate.HasValue == true && fundamentalPeriods.MaxPeriodEndDate.HasValue
                    ? (fundamentalPeriods.MaxPeriodEndDate.Value - fundamentalPeriods.MinPeriodEndDate.Value).TotalDays
                    : 0;
                if (fundamentalPeriods is null || fundamentalPeriods.PeriodCount < 8 || spanDays < 540)
                {
                    confidence *= 0.8;
                }
            }
        }

        if (coverage < 1)
        {
            warnings.Add(Factor("HISTORY_INSUFFICIENT", $"History coverage for {horizon} is {coverage:P0} of the required daily window."));
        }

        if (metrics.AdjustedCloseCoverage < 1)
        {
            warnings.Add(Factor("ADJUSTED_CLOSE_FALLBACK", "AdjustedClose fallback to Close reduced confidence."));
        }

        return Clamp(confidence, 0, 1);
    }

    private static double? ReturnPercent(double[] closes, int lookback)
    {
        if (closes.Length <= lookback)
        {
            return null;
        }

        var past = closes[closes.Length - 1 - lookback];
        var latest = closes[^1];
        if (past <= 0 || latest <= 0)
        {
            return null;
        }

        return (latest / past - 1d) * 100d;
    }

    private static void AddComponent(ref double weightedScore, ref double availableWeight, double? componentScore, double weight)
    {
        if (!componentScore.HasValue || weight <= 0)
        {
            return;
        }

        weightedScore += componentScore.Value * weight;
        availableWeight += weight;
    }

    private static TechnicalAnalysisFactorDto Factor(string code, string message)
        => new() { Code = code, Message = message };

    private static List<TechnicalAnalysisFactorDto> SortFactors(IEnumerable<TechnicalAnalysisFactorDto> factors)
        => factors
            .GroupBy(x => new { x.Code, x.Message })
            .Select(g => g.First())
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Message, StringComparer.Ordinal)
            .ToList();

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(max, value));
}
