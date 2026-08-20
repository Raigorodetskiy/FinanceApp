using System;
using System.Collections.Generic;
using System.Linq;

namespace FinanceApp.Core.Services;

/// <summary>
/// Pure, deterministic technical indicator calculations over timestamped daily OHLCV observations.
///
/// <para><b>Price-adjustment semantics:</b> Raw OHLC always remains available for auditability and
/// ATR. When <see cref="DailyObservation.AdjustedClose"/> is present and valid, close-based
/// indicators prefer an adjusted-close-only series for their required lookback window or full
/// recursive sequence. If that adjusted series is incomplete, calculations explicitly fall back to
/// raw close for the affected metric rather than silently mixing raw and adjusted points inside one
/// calculation window.</para>
///
/// <para><b>Warm-up requirements:</b>
/// <list type="bullet">
///   <item>SMA(N): N observations.</item>
///   <item>EMA(N): N observations (seed = SMA of first N, then Wilder-style recurrence).</item>
///   <item>RSI(14): 15 observations (14 changes require 15 prices); uses Wilder smoothing (α = 1/14).</item>
///   <item>MACD(12,26,9): 26 + 9 - 1 = 34 observations for signal; MACD line available after 26.</item>
///   <item>Volatility(N): N+1 observations (N log-returns from N+1 prices).</item>
///   <item>ATR(14): 15 observations (seed = SMA of first 14 true ranges; uses Wilder smoothing α = 1/14).</item>
///   <item>Drawdown: up to 252 observations (uses all available up to 252).</item>
///   <item>Returns: require exactly 5/21/63/126/252 observations back (trading-day-based, not calendar).</item>
/// </list>
/// </para>
///
/// <para><b>Deduplication policy:</b> When multiple observations share the same timestamp,
/// the last one in the input sequence is kept (stable, deterministic).</para>
///
/// <para><b>Non-positive prices:</b> Observations where Close ≤ 0 are excluded from
/// calculations that require logarithms or ratios (returns, volatility, RSI, MACD, EMA).
/// For SMA/ATR/drawdown, non-positive Close values are also excluded.</para>
/// </summary>
public static class TechnicalIndicators
{
    // ─── Trading-day horizon mappings ──────────────────────────────────────────
    // These are approximate trading-day counts, NOT calendar days.
    // Using standard market convention: 5 td/wk, 21 td/mo, 252 td/yr.
    /// <summary>1-week return uses 5 trading-day observations back.</summary>
    public const int TradingDaysPerWeek = 5;
    /// <summary>1-month return uses 21 trading-day observations back.</summary>
    public const int TradingDaysPerMonth = 21;
    /// <summary>3-month return uses 63 trading-day observations back.</summary>
    public const int TradingDaysPer3Months = 63;
    /// <summary>6-month return uses 126 trading-day observations back.</summary>
    public const int TradingDaysPer6Months = 126;
    /// <summary>12-month (52-week / 1-year) return uses 252 trading-day observations back.</summary>
    public const int TradingDaysPer12Months = 252;

    public enum PriceBasis
    {
        Adjusted,
        RawFallback,
        Unavailable
    }

    public sealed record PriceSeriesSelection(PriceBasis Basis, string Reason);

    // ─── Result types ──────────────────────────────────────────────────────────

    /// <summary>
    /// Holds the full set of Phase 1 technical indicator values for a single stock.
    /// Null fields indicate insufficient warm-up data.
    /// </summary>
    public sealed record TechnicalIndicatorResult(
        DateTime AsOfDate,
        double? Sma20,
        double? Sma50,
        double? Sma200,
        double? Ema12,
        double? Ema26,
        double? Rsi14,
        MACDResult? Macd,
        ReturnResult? Returns,
        double? Volatility20,
        double? Volatility60,
        double? CurrentDrawdown,
        double? Atr14,
        IReadOnlyDictionary<string, PriceSeriesSelection> PriceBasisByMetric);

    /// <summary>MACD line, signal line, and histogram. Null when insufficient data.</summary>
    public sealed record MACDResult(double MacdLine, double? SignalLine, double? Histogram);

    /// <summary>
    /// Percentage returns (not decimal) for observation-based trading-day horizons.
    /// Null when there are insufficient observations.
    /// </summary>
    public sealed record ReturnResult(
        double? Return1Week,
        double? Return1Month,
        double? Return3Months,
        double? Return6Months,
        double? Return12Months);

    // ─── Input type ───────────────────────────────────────────────────────────

    /// <summary>A single daily OHLCV observation.</summary>
    public sealed record DailyObservation(
        DateTime Date,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume,
        decimal? AdjustedClose = null);

    // ─── Main entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Calculate all Phase 1 technical indicators from a sequence of daily observations.
    /// The input need not be sorted; duplicate dates are deduplicated by retaining the last entry.
    /// Returns null when the input contains zero valid observations.
    /// </summary>
    public static TechnicalIndicatorResult? Calculate(IEnumerable<DailyObservation> observations)
    {
        if (observations is null) throw new ArgumentNullException(nameof(observations));

        // Sort chronologically and deduplicate (last-wins on same date).
        var sorted = observations
            .GroupBy(o => o.Date)
            .OrderBy(g => g.Key)
            .Select(g => g.Last())
            .ToList();

        if (sorted.Count == 0) return null;

        var asOfDate = sorted[^1].Date;
        var priceBasisByMetric = new Dictionary<string, PriceSeriesSelection>(StringComparer.Ordinal);

        var sma20Series = SelectWindowSeries(sorted, 20, "Sma20");
        var sma50Series = SelectWindowSeries(sorted, 50, "Sma50");
        var sma200Series = SelectWindowSeries(sorted, 200, "Sma200");
        var ema12Series = SelectFullSeries(sorted, 12, "Ema12");
        var ema26Series = SelectFullSeries(sorted, 26, "Ema26");
        var rsi14Series = SelectFullSeries(sorted, 15, "Rsi14");
        var macdSeries = SelectFullSeries(sorted, 26, "Macd");
        var returns1WeekSeries = SelectWindowSeries(sorted, TradingDaysPerWeek + 1, "Returns.Return1Week");
        var returns1MonthSeries = SelectWindowSeries(sorted, TradingDaysPerMonth + 1, "Returns.Return1Month");
        var returns3MonthsSeries = SelectWindowSeries(sorted, TradingDaysPer3Months + 1, "Returns.Return3Months");
        var returns6MonthsSeries = SelectWindowSeries(sorted, TradingDaysPer6Months + 1, "Returns.Return6Months");
        var returns12MonthsSeries = SelectWindowSeries(sorted, TradingDaysPer12Months + 1, "Returns.Return12Months");
        var volatility20Series = SelectWindowSeries(sorted, 21, "Volatility20");
        var volatility60Series = SelectWindowSeries(sorted, 61, "Volatility60");
        var drawdownSeries = SelectWindowSeries(sorted, Math.Min(sorted.Count, TradingDaysPer12Months), "CurrentDrawdown");

        AddSelection(priceBasisByMetric, "Sma20", sma20Series);
        AddSelection(priceBasisByMetric, "Sma50", sma50Series);
        AddSelection(priceBasisByMetric, "Sma200", sma200Series);
        AddSelection(priceBasisByMetric, "Ema12", ema12Series);
        AddSelection(priceBasisByMetric, "Ema26", ema26Series);
        AddSelection(priceBasisByMetric, "Rsi14", rsi14Series);
        AddSelection(priceBasisByMetric, "Macd", macdSeries);
        AddSelection(priceBasisByMetric, "Returns.Return1Week", returns1WeekSeries);
        AddSelection(priceBasisByMetric, "Returns.Return1Month", returns1MonthSeries);
        AddSelection(priceBasisByMetric, "Returns.Return3Months", returns3MonthsSeries);
        AddSelection(priceBasisByMetric, "Returns.Return6Months", returns6MonthsSeries);
        AddSelection(priceBasisByMetric, "Returns.Return12Months", returns12MonthsSeries);
        AddSelection(priceBasisByMetric, "Volatility20", volatility20Series);
        AddSelection(priceBasisByMetric, "Volatility60", volatility60Series);
        AddSelection(priceBasisByMetric, "CurrentDrawdown", drawdownSeries);
        priceBasisByMetric["Atr14"] = new PriceSeriesSelection(
            PriceBasis.RawFallback,
            "ATR uses raw OHLC because adjusted OHLC is not available in the current model.");

        return new TechnicalIndicatorResult(
            AsOfDate: asOfDate,
            Sma20: CalcSma(sma20Series.Values, 20),
            Sma50: CalcSma(sma50Series.Values, 50),
            Sma200: CalcSma(sma200Series.Values, 200),
            Ema12: CalcEma(ema12Series.Values, 12),
            Ema26: CalcEma(ema26Series.Values, 26),
            Rsi14: CalcRsi14(rsi14Series.Values),
            Macd: CalcMacd(macdSeries.Values),
            Returns: BuildReturnsResult(
                returns1WeekSeries,
                returns1MonthSeries,
                returns3MonthsSeries,
                returns6MonthsSeries,
                returns12MonthsSeries),
            Volatility20: CalcVolatility(volatility20Series.Values, 20),
            Volatility60: CalcVolatility(volatility60Series.Values, 60),
            CurrentDrawdown: CalcCurrentDrawdown(drawdownSeries.Values),
            Atr14: CalcAtr14(sorted),
            PriceBasisByMetric: priceBasisByMetric);
    }

    private static void AddSelection(
        IDictionary<string, PriceSeriesSelection> target,
        string key,
        SelectedSeries selection)
        => target[key] = selection.Selection;

    private static ReturnResult BuildReturnsResult(
        SelectedSeries return1Week,
        SelectedSeries return1Month,
        SelectedSeries return3Months,
        SelectedSeries return6Months,
        SelectedSeries return12Months)
        => new(
            TryReturn(return1Week.Values, TradingDaysPerWeek),
            TryReturn(return1Month.Values, TradingDaysPerMonth),
            TryReturn(return3Months.Values, TradingDaysPer3Months),
            TryReturn(return6Months.Values, TradingDaysPer6Months),
            TryReturn(return12Months.Values, TradingDaysPer12Months));

    private static SelectedSeries SelectFullSeries(
        IReadOnlyList<DailyObservation> observations,
        int minimumRequiredPoints,
        string metricName)
        => SelectSeries(observations, observations.Count, minimumRequiredPoints, metricName);

    private static SelectedSeries SelectWindowSeries(
        IReadOnlyList<DailyObservation> observations,
        int requiredPoints,
        string metricName)
        => SelectSeries(observations, requiredPoints, requiredPoints, metricName);

    private static SelectedSeries SelectSeries(
        IReadOnlyList<DailyObservation> observations,
        int pointsToUse,
        int minimumRequiredPoints,
        string metricName)
    {
        if (observations.Count < minimumRequiredPoints)
        {
            return new SelectedSeries(
                Array.Empty<double>(),
                new PriceSeriesSelection(
                    PriceBasis.Unavailable,
                    $"{metricName} requires at least {minimumRequiredPoints} observations."));
        }

        if (pointsToUse <= 0)
        {
            return new SelectedSeries(
                Array.Empty<double>(),
                new PriceSeriesSelection(
                    PriceBasis.Unavailable,
                    $"{metricName} has no usable observation window."));
        }

        var window = observations.Skip(observations.Count - pointsToUse).ToList();

        if (TryBuildAdjustedSeries(window, out var adjustedValues))
        {
            return new SelectedSeries(
                adjustedValues,
                new PriceSeriesSelection(
                    PriceBasis.Adjusted,
                    $"{metricName} used adjusted close for the full required sequence."));
        }

        if (TryBuildRawSeries(window, out var rawValues))
        {
            return new SelectedSeries(
                rawValues,
                new PriceSeriesSelection(
                    PriceBasis.RawFallback,
                    $"{metricName} fell back to raw close because adjusted close was unavailable or invalid within the required sequence."));
        }

        return new SelectedSeries(
            Array.Empty<double>(),
            new PriceSeriesSelection(
                PriceBasis.Unavailable,
                $"{metricName} is unavailable because the required sequence contains no fully valid raw or adjusted close series."));
    }

    private static bool TryBuildAdjustedSeries(IReadOnlyList<DailyObservation> observations, out double[] values)
    {
        values = new double[observations.Count];
        for (int i = 0; i < observations.Count; i++)
        {
            var adjusted = observations[i].AdjustedClose;
            if (!adjusted.HasValue || adjusted.Value <= 0m)
            {
                values = Array.Empty<double>();
                return false;
            }

            values[i] = (double)adjusted.Value;
        }

        return true;
    }

    private static bool TryBuildRawSeries(IReadOnlyList<DailyObservation> observations, out double[] values)
    {
        values = new double[observations.Count];
        for (int i = 0; i < observations.Count; i++)
        {
            if (observations[i].Close <= 0m)
            {
                values = Array.Empty<double>();
                return false;
            }

            values[i] = (double)observations[i].Close;
        }

        return true;
    }

    private sealed record SelectedSeries(double[] Values, PriceSeriesSelection Selection);

    // ─── SMA ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simple Moving Average over the last <paramref name="period"/> closes.
    /// Returns null when fewer than <paramref name="period"/> positive-valued observations are available.
    /// </summary>
    public static double? CalcSma(double[] closes, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        if (closes is null || closes.Length < period) return null;

        // Use the last 'period' values
        double sum = 0;
        int start = closes.Length - period;
        for (int i = start; i < closes.Length; i++)
        {
            if (closes[i] <= 0) return null;
            sum += closes[i];
        }
        return sum / period;
    }

    // ─── EMA ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exponential Moving Average.
    /// Seed rule: EMA is seeded with the SMA of the first <paramref name="period"/> values.
    /// Multiplier: k = 2 / (period + 1).
    /// Returns null when fewer than <paramref name="period"/> observations are present.
    /// Non-positive prices cause null to be returned.
    /// </summary>
    public static double? CalcEma(double[] closes, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        if (closes is null || closes.Length < period) return null;

        // Check for any non-positive values in the entire series used
        for (int i = 0; i < closes.Length; i++)
            if (closes[i] <= 0) return null;

        // Seed = SMA of first 'period' values
        double ema = 0;
        for (int i = 0; i < period; i++)
            ema += closes[i];
        ema /= period;

        double k = 2.0 / (period + 1);
        for (int i = period; i < closes.Length; i++)
            ema = closes[i] * k + ema * (1 - k);

        return ema;
    }

    // ─── RSI ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// RSI 14 using Wilder smoothing (α = 1/14).
    /// Requires at least 15 observations (14 changes).
    /// Seed: average of first 14 gains/losses.
    /// Non-positive closes cause null to be returned.
    /// </summary>
    public static double? CalcRsi14(double[] closes)
    {
        const int period = 14;
        if (closes is null || closes.Length < period + 1) return null;

        for (int i = 0; i < closes.Length; i++)
            if (closes[i] <= 0) return null;

        // Seed: average gain/loss over first 14 changes
        double avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            double chg = closes[i] - closes[i - 1];
            if (chg > 0) avgGain += chg;
            else avgLoss += -chg;
        }
        avgGain /= period;
        avgLoss /= period;

        // Wilder smoothing for remaining observations
        for (int i = period + 1; i < closes.Length; i++)
        {
            double chg = closes[i] - closes[i - 1];
            double gain = chg > 0 ? chg : 0;
            double loss = chg < 0 ? -chg : 0;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0) return 100.0;
        double rs = avgGain / avgLoss;
        return 100.0 - 100.0 / (1 + rs);
    }

    // ─── MACD ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// MACD 12/26/9.
    /// MACD line = EMA(12) - EMA(26). Requires ≥ 26 observations.
    /// Signal line = EMA(9) of MACD values. Requires ≥ 26 + 9 - 1 = 34 observations.
    /// Histogram = MACD line - signal line.
    /// Signal/histogram are null when fewer than 34 observations are available.
    /// Non-positive prices cause null to be returned.
    /// </summary>
    public static MACDResult? CalcMacd(double[] closes)
    {
        if (closes is null || closes.Length < 26) return null;

        for (int i = 0; i < closes.Length; i++)
            if (closes[i] <= 0) return null;

        // Calculate MACD line at each point starting at index 25 (0-based, 26th obs)
        // We need the EMA(26) over whole series, and EMA(12) over whole series at each step
        // Build the full MACD line series for signal computation
        int n = closes.Length;
        double k12 = 2.0 / (12 + 1);
        double k26 = 2.0 / (26 + 1);
        double k9 = 2.0 / (9 + 1);

        // Seed EMA12 = SMA of first 12 closes
        double ema12 = closes.Take(12).Sum() / 12;
        // Seed EMA26 = SMA of first 26 closes
        double ema26 = closes.Take(26).Sum() / 26;

        // Advance EMA12 to index 25 (same point as EMA26 seed)
        for (int i = 12; i < 26; i++)
            ema12 = closes[i] * k12 + ema12 * (1 - k12);

        // Now both EMAs are at index 25; build MACD line series
        var macdValues = new List<double>();
        macdValues.Add(ema12 - ema26);

        for (int i = 26; i < n; i++)
        {
            ema12 = closes[i] * k12 + ema12 * (1 - k12);
            ema26 = closes[i] * k26 + ema26 * (1 - k26);
            macdValues.Add(ema12 - ema26);
        }

        double macdLine = macdValues[^1];

        if (macdValues.Count < 9)
        {
            // Not enough MACD values to compute signal
            return new MACDResult(macdLine, null, null);
        }

        // Seed signal EMA = SMA of first 9 MACD values
        double[] mv = macdValues.ToArray();
        double signal = mv.Take(9).Sum() / 9;
        for (int i = 9; i < mv.Length; i++)
            signal = mv[i] * k9 + signal * (1 - k9);

        double histogram = macdLine - signal;
        return new MACDResult(macdLine, signal, histogram);
    }

    // ─── Returns ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Percentage returns (not fractional) for trading-day-based horizons.
    /// Mapping: 1w=5, 1m=21, 3m=63, 6m=126, 12m=252 trading-day observations.
    /// Formula: (close_latest / close_N_ago - 1) * 100.
    /// Null when fewer than N+1 observations are available or any price ≤ 0.
    /// </summary>
    public static ReturnResult? CalcReturns(double[] closes)
    {
        if (closes is null || closes.Length == 0) return null;

        double? Ret(int n) => TryReturn(closes, n);
        return new ReturnResult(
            Ret(TradingDaysPerWeek),
            Ret(TradingDaysPerMonth),
            Ret(TradingDaysPer3Months),
            Ret(TradingDaysPer6Months),
            Ret(TradingDaysPer12Months));
    }

    private static double? TryReturn(double[] closes, int n)
    {
        if (closes.Length <= n) return null;
        var past = closes[^(n + 1)];
        var latest = closes[^1];
        if (past <= 0 || latest <= 0) return null;
        return (latest / past - 1) * 100.0;
    }

    // ─── Volatility ───────────────────────────────────────────────────────────

    /// <summary>
    /// Annualized historical volatility using log returns over the last <paramref name="window"/> trading days.
    /// Requires window+1 observations (to compute window log-returns).
    /// Formula: std_dev(log returns) * sqrt(252), where std_dev uses population std dev (N denominator).
    /// Non-positive or zero closes result in null.
    /// Constant-price series (zero log-return variance) correctly returns 0.
    /// </summary>
    public static double? CalcVolatility(double[] closes, int window)
    {
        if (window <= 0) throw new ArgumentOutOfRangeException(nameof(window));
        if (closes is null || closes.Length < window + 1) return null;

        int start = closes.Length - window - 1;
        var logReturns = new double[window];
        for (int i = 0; i < window; i++)
        {
            var p0 = closes[start + i];
            var p1 = closes[start + i + 1];
            if (p0 <= 0 || p1 <= 0) return null;
            logReturns[i] = Math.Log(p1 / p0);
        }

        double mean = logReturns.Average();
        double variance = logReturns.Select(r => (r - mean) * (r - mean)).Average(); // population variance
        return Math.Sqrt(variance) * Math.Sqrt(252.0);
    }

    // ─── Drawdown ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Current drawdown from the maximum close over the last 252 observations (or all if fewer).
    /// Formula: (close_latest / max_close_in_window - 1) * 100 (will be ≤ 0 unless latest == max).
    /// Returns 0 when the latest close equals the maximum (no drawdown / at all-time high in window).
    /// Non-positive closes result in null.
    /// </summary>
    public static double? CalcCurrentDrawdown(double[] closes)
    {
        if (closes is null || closes.Length == 0) return null;

        int windowSize = Math.Min(closes.Length, TradingDaysPer12Months);
        int start = closes.Length - windowSize;
        double maxClose = double.MinValue;
        for (int i = start; i < closes.Length; i++)
        {
            if (closes[i] <= 0) return null;
            if (closes[i] > maxClose) maxClose = closes[i];
        }

        if (maxClose <= 0) return null;
        var latest = closes[^1];
        if (latest <= 0) return null;
        return (latest / maxClose - 1.0) * 100.0;
    }

    // ─── ATR ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Average True Range (ATR) 14 using Wilder smoothing (α = 1/14).
    /// True Range = max(High - Low, |High - PrevClose|, |Low - PrevClose|).
    /// Requires at least 15 observations (14 true ranges; first TR uses only H-L if no prev).
    /// Seed: SMA of first 14 true ranges.
    /// </summary>
    public static double? CalcAtr14(IReadOnlyList<DailyObservation> observations)
    {
        const int period = 14;
        if (observations is null || observations.Count < period + 1) return null;

        // Compute true ranges
        var trs = new List<double>(observations.Count - 1);
        for (int i = 1; i < observations.Count; i++)
        {
            var curr = observations[i];
            var prev = observations[i - 1];
            double hi = (double)curr.High;
            double lo = (double)curr.Low;
            double pc = (double)prev.Close;
            double tr = Math.Max(hi - lo, Math.Max(Math.Abs(hi - pc), Math.Abs(lo - pc)));
            trs.Add(tr);
        }

        if (trs.Count < period) return null;

        // Seed = SMA of first 14 true ranges
        double atr = trs.Take(period).Average();

        // Wilder smoothing
        for (int i = period; i < trs.Count; i++)
            atr = (atr * (period - 1) + trs[i]) / period;

        return atr;
    }
}
