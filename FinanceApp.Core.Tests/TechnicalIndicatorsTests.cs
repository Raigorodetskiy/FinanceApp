using FinanceApp.Core.Services;
using Xunit;

namespace FinanceApp.Core.Tests;

public class TechnicalIndicatorsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TechnicalIndicators.DailyObservation Obs(
        DateTime date,
        decimal close,
        decimal open = 0,
        decimal high = 0,
        decimal low = 0,
        long volume = 0,
        decimal? adjustedClose = null)
    {
        if (open == 0) open = close;
        if (high == 0) high = close;
        if (low == 0) low = close;
        return new TechnicalIndicators.DailyObservation(date, open, high, low, close, volume, adjustedClose);
    }

    private static TechnicalIndicators.DailyObservation[] CloseSeries(params double[] closes)
    {
        var base_ = new DateTime(2020, 1, 1);
        return closes.Select((c, i) => Obs(base_.AddDays(i), (decimal)c)).ToArray();
    }

    // ── SMA ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SMA20_KnownSequence_Returns10_5()
    {
        // 1..20 => average = 10.5
        var closes = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();
        var result = TechnicalIndicators.CalcSma(closes, 20);
        Assert.NotNull(result);
        Assert.Equal(10.5, result!.Value, 6);
    }

    [Fact]
    public void SMA20_InsufficientData_ReturnsNull()
    {
        var closes = Enumerable.Range(1, 19).Select(i => (double)i).ToArray();
        Assert.Null(TechnicalIndicators.CalcSma(closes, 20));
    }

    [Fact]
    public void SMA20_ExactlyNObservations_ReturnsValue()
    {
        var closes = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();
        Assert.NotNull(TechnicalIndicators.CalcSma(closes, 20));
    }

    [Fact]
    public void SMA_NonPositivePrice_ReturnsNull()
    {
        var closes = new double[] { 1, 2, 0, 4, 5 };
        Assert.Null(TechnicalIndicators.CalcSma(closes, 5));
    }

    [Fact]
    public void SMA_UsesLast_N_Values()
    {
        // 100 observations, last 20 are all 10 → SMA20 = 10
        var closes = Enumerable.Range(1, 80).Select(i => (double)i)
            .Concat(Enumerable.Repeat(10.0, 20)).ToArray();
        var result = TechnicalIndicators.CalcSma(closes, 20);
        Assert.Equal(10.0, result!.Value, 6);
    }

    // ── EMA ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EMA12_InsufficientData_ReturnsNull()
    {
        var closes = Enumerable.Range(1, 11).Select(i => (double)i).ToArray();
        Assert.Null(TechnicalIndicators.CalcEma(closes, 12));
    }

    [Fact]
    public void EMA_ExactlyN_SeedEqualsAverage()
    {
        // With exactly N values, EMA = SMA (no recurrence steps)
        var closes = new double[] { 2, 4, 6, 8 };
        var result = TechnicalIndicators.CalcEma(closes, 4);
        Assert.NotNull(result);
        Assert.Equal(5.0, result!.Value, 6); // avg of 2,4,6,8 = 5
    }

    [Fact]
    public void EMA_NonPositivePrice_ReturnsNull()
    {
        var closes = new double[] { 1, 2, 3, 0, 5 };
        Assert.Null(TechnicalIndicators.CalcEma(closes, 4));
    }

    [Fact]
    public void EMA_KnownRecurrence()
    {
        // EMA(3) seed = avg(1,2,3) = 2, k = 2/(3+1) = 0.5
        // After price 4: ema = 4*0.5 + 2*0.5 = 3.0
        // After price 5: ema = 5*0.5 + 3*0.5 = 4.0
        var closes = new double[] { 1, 2, 3, 4, 5 };
        var result = TechnicalIndicators.CalcEma(closes, 3);
        Assert.NotNull(result);
        Assert.Equal(4.0, result!.Value, 6);
    }

    [Fact]
    public void EMA_AsOfDate_IsLatestObservation()
    {
        var obs = CloseSeries(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        var result = TechnicalIndicators.Calculate(obs);
        Assert.NotNull(result);
        Assert.Equal(obs[^1].Date, result!.AsOfDate);
    }

    // ── RSI ───────────────────────────────────────────────────────────────────

    [Fact]
    public void RSI14_InsufficientData_ReturnsNull()
    {
        var closes = Enumerable.Range(1, 14).Select(i => (double)i).ToArray();
        Assert.Null(TechnicalIndicators.CalcRsi14(closes));
    }

    [Fact]
    public void RSI14_AllGains_Returns100()
    {
        // Rising prices → all gains, no losses → RSI = 100
        var closes = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();
        var result = TechnicalIndicators.CalcRsi14(closes);
        Assert.Equal(100.0, result!.Value, 6);
    }

    [Fact]
    public void RSI14_AllLosses_Returns0()
    {
        // Falling prices → no gains, all losses → RSI = 0
        var closes = Enumerable.Range(1, 20).Select(i => 21.0 - i).ToArray();
        var result = TechnicalIndicators.CalcRsi14(closes);
        Assert.Equal(0.0, result!.Value, 6);
    }

    [Fact]
    public void RSI14_KnownReference()
    {
        // 15 observations: alternating +1/-1 changes starting from 10
        // 7 gains of 1, 7 losses of 1 → avg_gain = avg_loss → RS = 1 → RSI = 50
        var closes = new double[15];
        closes[0] = 10;
        for (int i = 1; i < 15; i++)
            closes[i] = closes[i - 1] + (i % 2 == 1 ? 1 : -1);
        var result = TechnicalIndicators.CalcRsi14(closes);
        Assert.NotNull(result);
        // RS = 1, RSI = 50
        Assert.Equal(50.0, result!.Value, 0);
    }

    [Fact]
    public void RSI14_NonPositivePrice_ReturnsNull()
    {
        var closes = new double[] { 10, 11, 12, 0, 10, 11, 12, 10, 11, 12, 10, 11, 12, 10, 11 };
        Assert.Null(TechnicalIndicators.CalcRsi14(closes));
    }

    [Fact]
    public void RSI14_ExactlyN1_ReturnsValue()
    {
        var closes = Enumerable.Range(1, 15).Select(i => (double)i).ToArray();
        Assert.NotNull(TechnicalIndicators.CalcRsi14(closes));
    }

    // ── MACD ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MACD_InsufficientData_ReturnsNull()
    {
        var closes = Enumerable.Range(1, 25).Select(i => (double)i).ToArray();
        Assert.Null(TechnicalIndicators.CalcMacd(closes));
    }

    [Fact]
    public void MACD_26Obs_ReturnsMacdLineNoSignal()
    {
        var closes = Enumerable.Range(1, 26).Select(i => (double)i).ToArray();
        var result = TechnicalIndicators.CalcMacd(closes);
        Assert.NotNull(result);
        Assert.Null(result!.SignalLine);
        Assert.Null(result.Histogram);
    }

    [Fact]
    public void MACD_34Obs_ReturnsFullResult()
    {
        var closes = Enumerable.Range(1, 34).Select(i => (double)i).ToArray();
        var result = TechnicalIndicators.CalcMacd(closes);
        Assert.NotNull(result);
        Assert.NotNull(result!.SignalLine);
        Assert.NotNull(result.Histogram);
    }

    [Fact]
    public void MACD_Histogram_EqualsMacdMinusSignal()
    {
        var closes = Enumerable.Range(1, 50).Select(i => (double)i).ToArray();
        var result = TechnicalIndicators.CalcMacd(closes);
        Assert.NotNull(result);
        Assert.NotNull(result!.SignalLine);
        Assert.Equal(result.MacdLine - result.SignalLine!.Value, result.Histogram!.Value, 10);
    }

    [Fact]
    public void MACD_NonPositivePrice_ReturnsNull()
    {
        var closes = Enumerable.Range(0, 34).Select(i => (double)i).ToArray(); // starts at 0
        Assert.Null(TechnicalIndicators.CalcMacd(closes));
    }

    // ── Volatility ────────────────────────────────────────────────────────────

    [Fact]
    public void Volatility_ConstantPrice_EqualsZero()
    {
        var closes = Enumerable.Repeat(100.0, 22).ToArray(); // 21 log-returns all 0
        var result = TechnicalIndicators.CalcVolatility(closes, 20);
        Assert.NotNull(result);
        Assert.Equal(0.0, result!.Value, 10);
    }

    [Fact]
    public void Volatility_InsufficientData_ReturnsNull()
    {
        var closes = Enumerable.Repeat(100.0, 20).ToArray(); // need 21 for window=20
        Assert.Null(TechnicalIndicators.CalcVolatility(closes, 20));
    }

    [Fact]
    public void Volatility_ExactlyWindowPlus1_ReturnsValue()
    {
        var closes = Enumerable.Range(1, 21).Select(i => (double)i).ToArray();
        Assert.NotNull(TechnicalIndicators.CalcVolatility(closes, 20));
    }

    [Fact]
    public void Volatility_NonPositivePrice_ReturnsNull()
    {
        var closes = new double[] { 100, 0, 100, 100, 100 };
        Assert.Null(TechnicalIndicators.CalcVolatility(closes, 3));
    }

    [Fact]
    public void Volatility_KnownLogReturnVector()
    {
        // 3 prices generating 2 log-returns: ln(2/1)=ln2, ln(4/2)=ln2
        // mean = ln2, variance = 0 (both equal mean), std_dev = 0, annualized = 0
        var closes = new double[] { 1.0, 2.0, 4.0 };
        // Wait - log(2/1) = log2 ≈ 0.6931, log(4/2) = log2 ≈ 0.6931
        // Both returns are equal so std_dev = 0
        var result = TechnicalIndicators.CalcVolatility(closes, 2);
        Assert.NotNull(result);
        Assert.Equal(0.0, result!.Value, 10);
    }

    // ── Drawdown ──────────────────────────────────────────────────────────────

    [Fact]
    public void Drawdown_RisingSeries_ReturnsZero()
    {
        // Rising series: latest = max → drawdown = 0
        var closes = Enumerable.Range(1, 252).Select(i => (double)i).ToArray();
        var result = TechnicalIndicators.CalcCurrentDrawdown(closes);
        Assert.NotNull(result);
        Assert.Equal(0.0, result!.Value, 10);
    }

    [Fact]
    public void Drawdown_KnownVector()
    {
        // max = 100, latest = 80 → drawdown = (80/100 - 1) * 100 = -20%
        var closes = Enumerable.Repeat(50.0, 10).Concat(new[] { 100.0 })
            .Concat(Enumerable.Repeat(80.0, 5)).ToArray();
        var result = TechnicalIndicators.CalcCurrentDrawdown(closes);
        Assert.NotNull(result);
        Assert.Equal(-20.0, result!.Value, 6);
    }

    [Fact]
    public void Drawdown_Only252WindowIsUsed()
    {
        // Ancient max > 252 bars ago should NOT affect calculation
        // The window is min(count, 252)
        var closes = new double[] { 1000.0 } // this is outside the 252 window
            .Concat(Enumerable.Range(1, 252).Select(i => (double)i))
            .ToArray();
        // closes has 253 elements; window = min(253, 252) = 252; skips the first 1000
        var result = TechnicalIndicators.CalcCurrentDrawdown(closes);
        Assert.NotNull(result);
        Assert.Equal(0.0, result!.Value, 6); // latest is max of last 252
    }

    [Fact]
    public void Drawdown_EmptyInput_ReturnsNull()
    {
        Assert.Null(TechnicalIndicators.CalcCurrentDrawdown(Array.Empty<double>()));
    }

    // ── ATR ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ATR14_InsufficientData_ReturnsNull()
    {
        var obs = Enumerable.Range(0, 14).Select(i => Obs(new DateTime(2020, 1, 1).AddDays(i), 100)).ToArray();
        Assert.Null(TechnicalIndicators.CalcAtr14(obs));
    }

    [Fact]
    public void ATR14_ConstantPrice_Returns0()
    {
        // H=L=C=100, prevC=100 → TR=0 for all → ATR=0
        var obs = Enumerable.Range(0, 20).Select(i => Obs(new DateTime(2020, 1, 1).AddDays(i), 100, 100, 100, 100)).ToArray();
        var result = TechnicalIndicators.CalcAtr14(obs);
        Assert.NotNull(result);
        Assert.Equal(0.0, result!.Value, 10);
    }

    [Fact]
    public void ATR14_KnownVector()
    {
        // 15 observations: daily candle with H=close+1, L=close-1, so H-L=2 always
        // and |H-prevC| and |L-prevC| also approximately 1-2 → TR = 2 for all
        // ATR seed = average of first 14 TRs = 2; subsequent = (2*13 + 2)/14 = 2
        var base_ = new DateTime(2020, 1, 1);
        var obs = Enumerable.Range(0, 15)
            .Select(i => Obs(base_.AddDays(i), 100, 100, 101, 99))
            .ToArray();
        var result = TechnicalIndicators.CalcAtr14(obs);
        Assert.NotNull(result);
        Assert.Equal(2.0, result!.Value, 6);
    }

    [Fact]
    public void ATR14_ExactlyN1Obs_ReturnsValue()
    {
        var base_ = new DateTime(2020, 1, 1);
        var obs = Enumerable.Range(0, 15).Select(i => Obs(base_.AddDays(i), 100, 100, 102, 98)).ToArray();
        Assert.NotNull(TechnicalIndicators.CalcAtr14(obs));
    }

    // ── Input handling ────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_UnsortedInput_SortsChronologically()
    {
        var base_ = new DateTime(2020, 1, 1);
        // Provide 20 observations in reverse order
        var obs = Enumerable.Range(0, 20)
            .Select(i => Obs(base_.AddDays(19 - i), i + 1))
            .ToArray();
        var result = TechnicalIndicators.Calculate(obs);
        Assert.NotNull(result);
        Assert.Equal(base_.AddDays(19), result!.AsOfDate);
    }

    [Fact]
    public void Calculate_DuplicateTimestamp_LastValueWins()
    {
        var base_ = new DateTime(2020, 1, 1);
        // Two observations on same date with different closes; last in sequence wins
        var obs = new[]
        {
            Obs(base_, 5),
            Obs(base_.AddDays(1), 6),
            Obs(base_.AddDays(1), 7), // duplicate; this should win
        };
        var result = TechnicalIndicators.Calculate(obs);
        Assert.NotNull(result);
        // AsOfDate = AddDays(1)
        Assert.Equal(base_.AddDays(1), result!.AsOfDate);
        // SMA2 of (5, 7) = 6
        Assert.Equal(6.0, TechnicalIndicators.CalcSma(new[] { 5.0, 7.0 }, 2)!.Value, 6);
    }

    [Fact]
    public void Calculate_EmptyInput_ReturnsNull()
    {
        Assert.Null(TechnicalIndicators.Calculate(Array.Empty<TechnicalIndicators.DailyObservation>()));
    }

    [Fact]
    public void Calculate_AsOfDate_IsLatestObservation()
    {
        var base_ = new DateTime(2020, 1, 1);
        var obs = Enumerable.Range(0, 25).Select(i => Obs(base_.AddDays(i), i + 1)).ToArray();
        var result = TechnicalIndicators.Calculate(obs);
        Assert.Equal(base_.AddDays(24), result!.AsOfDate);
    }

    // ── Returns ───────────────────────────────────────────────────────────────

    [Fact]
    public void Returns_InsufficientData_ReturnsNullForLongerHorizons()
    {
        var closes = Enumerable.Range(1, 10).Select(i => (double)i).ToArray();
        var result = TechnicalIndicators.CalcReturns(closes);
        Assert.NotNull(result);
        Assert.NotNull(result!.Return1Week);  // 6 obs required; 10 are available
        Assert.Null(result.Return1Month);
    }

    [Fact]
    public void Returns_KnownValues()
    {
        // 6 prices: first = 100, last = 110. Return1Week = (110/100 - 1) * 100 = 10%
        var closes = new double[] { 100, 101, 102, 103, 104, 110 }; // 6 obs, horizon 5
        var result = TechnicalIndicators.CalcReturns(closes);
        Assert.NotNull(result);
        Assert.NotNull(result!.Return1Week);
        Assert.Equal(10.0, result.Return1Week!.Value, 6);
    }

    [Fact]
    public void Calculate_PrefersAdjustedClose_WhenFullMetricWindowIsAvailable()
    {
        var base_ = new DateTime(2020, 1, 1);
        var obs = new[]
        {
            Obs(base_.AddDays(0), 100m, adjustedClose: 50m),
            Obs(base_.AddDays(1), 102m, adjustedClose: 51m),
            Obs(base_.AddDays(2), 104m, adjustedClose: 52m),
            Obs(base_.AddDays(3), 52m, adjustedClose: 52m),
            Obs(base_.AddDays(4), 53m, adjustedClose: 53m),
            Obs(base_.AddDays(5), 54m, adjustedClose: 54m),
        };

        var result = TechnicalIndicators.Calculate(obs);

        Assert.NotNull(result);
        Assert.Equal(8.0, result!.Returns!.Return1Week!.Value, 6);
        Assert.Equal(TechnicalIndicators.PriceBasis.Adjusted, result.PriceBasisByMetric["Returns.Return1Week"].Basis);
    }

    [Fact]
    public void Calculate_FallsBackToRawClose_WhenAdjustedWindowIsIncomplete()
    {
        var base_ = new DateTime(2020, 1, 1);
        var obs = Enumerable.Range(0, 20)
            .Select(i => Obs(
                base_.AddDays(i),
                close: 100 + i,
                adjustedClose: i == 10 ? null : 200 + i))
            .ToArray();

        var result = TechnicalIndicators.Calculate(obs);

        Assert.NotNull(result);
        Assert.Equal(109.5, result!.Sma20!.Value, 6);
        Assert.Equal(TechnicalIndicators.PriceBasis.RawFallback, result.PriceBasisByMetric["Sma20"].Basis);
    }

    [Fact]
    public void Calculate_AdjustedSeriesMayBeUsedForShortWindowEvenWhenLongerWindowFallsBack()
    {
        var base_ = new DateTime(2020, 1, 1);
        var obs = Enumerable.Range(0, 30)
            .Select(i => Obs(
                base_.AddDays(i),
                close: 100 + i,
                adjustedClose: i < 10 ? null : 200 + i))
            .ToArray();

        var result = TechnicalIndicators.Calculate(obs);

        Assert.NotNull(result);
        Assert.Equal(TechnicalIndicators.PriceBasis.Adjusted, result!.PriceBasisByMetric["Sma20"].Basis);
        Assert.Equal(TechnicalIndicators.PriceBasis.RawFallback, result.PriceBasisByMetric["Ema12"].Basis);
    }

    [Fact]
    public void Calculate_AtrRemainsRawPriceBased()
    {
        var base_ = new DateTime(2020, 1, 1);
        var obs = Enumerable.Range(0, 15)
            .Select(i =>
            {
                var close = i < 7 ? 100m : 50m;
                return Obs(base_.AddDays(i), close, open: close, high: close + 1m, low: close - 1m, adjustedClose: 100m + i);
            })
            .ToArray();

        var result = TechnicalIndicators.Calculate(obs);

        Assert.NotNull(result);
        Assert.NotNull(result!.Atr14);
        Assert.True(result.Atr14 > 2.0);
        Assert.Equal(TechnicalIndicators.PriceBasis.RawFallback, result.PriceBasisByMetric["Atr14"].Basis);
    }
}
