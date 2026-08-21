using FinanceApp.API.Models;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public readonly record struct StockPerformanceSubject(
    int StockId,
    string? Exchange,
    decimal CurrentPrice,
    decimal? CurrentPriceChange,
    decimal? CurrentPriceChangePercent,
    DateTime? CurrentPriceAtUtc);

public interface IStockPerformanceCalculationService
{
    bool IsSupportedRange(string normalizedRange);
    Task<IReadOnlyList<IndexConstituentPerformanceItemDto>> CalculateAsync(
        IReadOnlyList<StockPerformanceSubject> subjects,
        string normalizedRange,
        CancellationToken cancellationToken = default);
}

public sealed class StockPerformanceCalculationService(
    AppDbContext context,
    TimeProvider timeProvider) : IStockPerformanceCalculationService
{
    private static readonly TimeSpan CurrentQuoteMaxAge = TimeSpan.FromHours(24);
    private static readonly TimeZoneInfo DefaultBusinessTimeZone = ResolveDefaultBusinessTimeZone();

    public bool IsSupportedRange(string normalizedRange)
        => normalizedRange is "5y" or "3y" or "1y" or "6m" or "3m" or "1m" or "1w" or "24h" or "today";

    public async Task<IReadOnlyList<IndexConstituentPerformanceItemDto>> CalculateAsync(
        IReadOnlyList<StockPerformanceSubject> subjects,
        string normalizedRange,
        CancellationToken cancellationToken = default)
    {
        if (subjects.Count == 0)
        {
            return Array.Empty<IndexConstituentPerformanceItemDto>();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var interval = GetInterval(normalizedRange);
        var canUseCurrentSnapshotFallback = normalizedRange is "24h" or "today";

        var rangeByStockId = subjects.ToDictionary(
            x => x.StockId,
            x => BuildRangeSpec(normalizedRange, x.Exchange, nowUtc, interval));
        var minQueryFrom = rangeByStockId.Values.Min(x => x.QueryFromUtc);

        var historicalRows = await context.StockHistoricalPrices
            .AsNoTracking()
            .Where(x =>
                rangeByStockId.Keys.Contains(x.StockId) &&
                x.Interval == interval &&
                x.Timestamp >= minQueryFrom &&
                x.Timestamp <= nowUtc &&
                x.Close > 0m &&
                x.QuoteUnitMultiplier > 0m)
            .OrderBy(x => x.StockId)
            .ThenBy(x => x.Timestamp)
            .Select(x => new HistoricalPoint(
                x.StockId,
                x.Timestamp,
                x.Close,
                x.QuoteUnitMultiplier,
                x.NormalizedQuoteCurrency))
            .ToListAsync(cancellationToken);

        var pointsByStock = historicalRows
            .GroupBy(x => x.StockId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var results = new List<IndexConstituentPerformanceItemDto>(subjects.Count);
        foreach (var subject in subjects)
        {
            if (!pointsByStock.TryGetValue(subject.StockId, out var points) || points.Count == 0)
            {
                if (canUseCurrentSnapshotFallback &&
                    TryBuildCurrentSnapshotPerformanceItem(subject, out var fallbackItem))
                {
                    results.Add(fallbackItem);
                }
                else
                {
                    results.Add(new IndexConstituentPerformanceItemDto
                    {
                        StockId = subject.StockId,
                        DataStatus = ConstituentPerformanceDataStatus.InsufficientData,
                    });
                }

                continue;
            }

            var rangeSpec = rangeByStockId[subject.StockId];
            var latestHistoryPoint = points[^1];
            var baseline = ResolveBaseline(points, rangeSpec.BoundaryUtc);
            if (baseline is null)
            {
                if (canUseCurrentSnapshotFallback &&
                    TryBuildCurrentSnapshotPerformanceItem(subject, out var fallbackItem))
                {
                    results.Add(fallbackItem);
                }
                else
                {
                    results.Add(new IndexConstituentPerformanceItemDto
                    {
                        StockId = subject.StockId,
                        DataStatus = ConstituentPerformanceDataStatus.InsufficientData,
                    });
                }

                continue;
            }

            if (points.Count < 2 && canUseCurrentSnapshotFallback &&
                TryBuildCurrentSnapshotPerformanceItem(subject, out var sparseFallbackItem))
            {
                results.Add(sparseFallbackItem);
                continue;
            }

            var startNorm = baseline.Value.Close * baseline.Value.QuoteUnitMultiplier;
            var endNorm = latestHistoryPoint.Close * latestHistoryPoint.QuoteUnitMultiplier;
            var endAtUtc = latestHistoryPoint.Timestamp;

            if (TryBuildCurrentEndpoint(subject, nowUtc, latestHistoryPoint, out var currentEndpoint))
            {
                endNorm = currentEndpoint.EndPrice;
                endAtUtc = currentEndpoint.EndAtUtc;
            }

            if (startNorm <= 0m || endNorm <= 0m || baseline.Value.Timestamp >= endAtUtc)
            {
                if (canUseCurrentSnapshotFallback &&
                    TryBuildCurrentSnapshotPerformanceItem(subject, out var fallbackItem))
                {
                    results.Add(fallbackItem);
                    continue;
                }

                results.Add(new IndexConstituentPerformanceItemDto
                {
                    StockId = subject.StockId,
                    StartPrice = startNorm,
                    EndPrice = endNorm,
                    StartAtUtc = baseline.Value.Timestamp,
                    EndAtUtc = endAtUtc,
                    DataStatus = ConstituentPerformanceDataStatus.InsufficientData,
                });
                continue;
            }

            results.Add(new IndexConstituentPerformanceItemDto
            {
                StockId = subject.StockId,
                StartPrice = startNorm,
                EndPrice = endNorm,
                ChangePercent = (double)((endNorm - startNorm) / startNorm * 100m),
                StartAtUtc = baseline.Value.Timestamp,
                EndAtUtc = endAtUtc,
                DataStatus = ConstituentPerformanceDataStatus.Available,
            });
        }

        return results;
    }

    private static string GetInterval(string normalizedRange) => normalizedRange switch
    {
        "5y" or "3y" => "1mo",
        "1y" => "1wk",
        "6m" or "3m" or "1m" => "1d",
        "1w" => "1h",
        "24h" or "today" => "10m",
        _ => "1mo",
    };

    private static RangeSpec BuildRangeSpec(string normalizedRange, string? exchange, DateTime nowUtc, string interval)
    {
        var boundaryUtc = normalizedRange switch
        {
            "5y" => nowUtc.AddYears(-5),
            "3y" => nowUtc.AddYears(-3),
            "1y" => nowUtc.AddYears(-1),
            "6m" => nowUtc.AddMonths(-6),
            "3m" => nowUtc.AddMonths(-3),
            "1m" => nowUtc.AddMonths(-1),
            "1w" => nowUtc.AddDays(-7),
            "24h" => nowUtc.AddHours(-24),
            "today" => ResolveTodayBoundaryUtc(exchange, nowUtc),
            _ => nowUtc.AddYears(-5),
        };

        return new RangeSpec(boundaryUtc, GetQueryFromTimestamp(boundaryUtc, interval));
    }

    private static DateTime GetQueryFromTimestamp(DateTime boundaryUtc, string interval)
        => interval switch
        {
            "1h" => boundaryUtc.AddDays(-7),
            "1d" => boundaryUtc.AddDays(-14),
            "1wk" => boundaryUtc.AddDays(-31),
            "1mo" => boundaryUtc.AddDays(-62),
            "10m" => boundaryUtc.AddDays(-14),
            _ => boundaryUtc.AddDays(-14),
        };

    private static DateTime ResolveTodayBoundaryUtc(string? exchange, DateTime nowUtc)
    {
        if (TradingSessionCalendar.TryGetSessionSpec(exchange, out var sessionSpec))
        {
            var sessionTimeZone = TradingSessionCalendar.TryResolveTimeZone(sessionSpec);
            if (sessionTimeZone is not null)
            {
                var localNowDate = TradingSessionCalendar.ConvertUtcToLocalDate(nowUtc, sessionTimeZone);
                var currentSessionDate = TradingSessionCalendar.GetNextTradingDay(localNowDate, sessionSpec);
                return TradingSessionCalendar.BuildSessionWindow(currentSessionDate, sessionSpec, sessionTimeZone).SessionStartUtc;
            }
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, DefaultBusinessTimeZone);
        var localBoundary = DateOnly.FromDateTime(localNow).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localBoundary, DefaultBusinessTimeZone);
    }

    private static HistoricalPoint? ResolveBaseline(IReadOnlyList<HistoricalPoint> points, DateTime boundaryUtc)
    {
        for (var i = points.Count - 1; i >= 0; i--)
        {
            if (points[i].Timestamp <= boundaryUtc)
            {
                return points[i];
            }
        }

        return points[0];
    }

    private static bool TryBuildCurrentSnapshotPerformanceItem(
        StockPerformanceSubject subject,
        out IndexConstituentPerformanceItemDto item)
    {
        item = default!;

        if (subject.CurrentPrice is decimal endPrice
            && subject.CurrentPriceChange is decimal change
            && endPrice > 0m)
        {
            var startPrice = endPrice - change;
            if (startPrice > 0m)
            {
                item = new IndexConstituentPerformanceItemDto
                {
                    StockId = subject.StockId,
                    StartPrice = startPrice,
                    EndPrice = endPrice,
                    ChangePercent = (double)((endPrice - startPrice) / startPrice * 100m),
                    EndAtUtc = subject.CurrentPriceAtUtc,
                    DataStatus = ConstituentPerformanceDataStatus.Available,
                };
                return true;
            }
        }

        if (subject.CurrentPriceChangePercent is decimal percent)
        {
            item = new IndexConstituentPerformanceItemDto
            {
                StockId = subject.StockId,
                EndPrice = subject.CurrentPrice,
                ChangePercent = (double)percent,
                EndAtUtc = subject.CurrentPriceAtUtc,
                DataStatus = ConstituentPerformanceDataStatus.Available,
            };
            return true;
        }

        return false;
    }

    private static bool TryBuildCurrentEndpoint(
        StockPerformanceSubject subject,
        DateTime nowUtc,
        HistoricalPoint latestHistoryPoint,
        out Endpoint endpoint)
    {
        endpoint = default;
        if (subject.CurrentPrice <= 0m || !subject.CurrentPriceAtUtc.HasValue)
        {
            return false;
        }

        var currentAtUtc = subject.CurrentPriceAtUtc.Value;
        if (currentAtUtc <= latestHistoryPoint.Timestamp || currentAtUtc > nowUtc)
        {
            return false;
        }

        if ((nowUtc - currentAtUtc) > CurrentQuoteMaxAge)
        {
            return false;
        }

        var normalizedQuoteCurrency = latestHistoryPoint.NormalizedQuoteCurrency?.Trim();
        if (!string.Equals(normalizedQuoteCurrency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        endpoint = new Endpoint(subject.CurrentPrice, currentAtUtc);
        return true;
    }

    private static TimeZoneInfo ResolveDefaultBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private readonly record struct RangeSpec(DateTime BoundaryUtc, DateTime QueryFromUtc);
    private readonly record struct HistoricalPoint(
        int StockId,
        DateTime Timestamp,
        decimal Close,
        decimal QuoteUnitMultiplier,
        string? NormalizedQuoteCurrency);
    private readonly record struct Endpoint(decimal EndPrice, DateTime EndAtUtc);
}
