using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using FinanceApp.API.Models;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services;

public class StockHistoryService : IStockHistoryService
{
    private const int MaxYahooRequestAttempts = 5;
    private static readonly TimeSpan YahooRetryBaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan YahooRetryMaxDelay = TimeSpan.FromSeconds(20);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> StockRefreshLocks = new();

    private readonly AppDbContext _dbContext;
    private readonly IYahooRequestCoordinator _yahooRequestCoordinator;
    private readonly IStockQuoteConversionService _stockQuoteConversionService;
    private readonly TimeProvider _timeProvider;
    private readonly StockHistoryRefreshOptions _options;
    private readonly ILogger<StockHistoryService> _logger;

    public StockHistoryService(
        AppDbContext dbContext,
        IYahooRequestCoordinator yahooRequestCoordinator,
        IStockQuoteConversionService stockQuoteConversionService,
        TimeProvider timeProvider,
        IOptions<StockHistoryRefreshOptions> options,
        ILogger<StockHistoryService> logger)
    {
        _dbContext = dbContext;
        _yahooRequestCoordinator = yahooRequestCoordinator;
        _stockQuoteConversionService = stockQuoteConversionService;
        _timeProvider = timeProvider;
        _options = NormalizeOptions(options.Value);
        _logger = logger;
    }

    public async Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var stocks = await LoadDueAutomaticStocksAsync(nowUtc, cancellationToken);

        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var syncResult = await RefreshHistoryAsync(stock, StockHistoryRefreshTrigger.Automatic, cancellationToken);
                if (syncResult.RateLimited)
                {
                    _logger.LogInformation("Stopping Yahoo history refresh cycle early because a shared cooldown is active.");
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed syncing history for stock {StockId}", stock.Id);
            }
        }
    }

    public async Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
    {
        await RefreshHistoryAsync(stock, StockHistoryRefreshTrigger.Automatic, cancellationToken);
    }

    public async Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
    {
        return await RefreshHistoryAsync(stock, StockHistoryRefreshTrigger.Manual, cancellationToken);
    }

    public async Task<StockHistoryRefreshResponse> RefreshHistoryAsync(
        Stock stock,
        StockHistoryRefreshTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        if (stock is null)
        {
            throw new ArgumentNullException(nameof(stock));
        }

        if (string.IsNullOrWhiteSpace(stock.Ticker) || !StockExchanges.TryNormalize(stock.Exchange, out _))
        {
            throw new InvalidOperationException("Stock ticker and exchange must be valid before refreshing history.");
        }

        var stockLock = StockRefreshLocks.GetOrAdd(stock.Id, static _ => new SemaphoreSlim(1, 1));
        await stockLock.WaitAsync(cancellationToken);
        try
        {
            var persisted = await _dbContext.Stocks.FirstOrDefaultAsync(x => x.Id == stock.Id, cancellationToken);
            if (persisted is null)
            {
                return new StockHistoryRefreshResponse
                {
                    StockId = stock.Id,
                    DeletedPoints = 0,
                    ImportedPoints = 0,
                    RateLimited = false,
                    SkippedNotDue = true,
                };
            }

            if (!StockExchanges.TryNormalize(persisted.Exchange, out var normalizedExchange))
            {
                throw new InvalidOperationException("Stock ticker and exchange must be valid before refreshing history.");
            }

            persisted.Exchange = normalizedExchange;
            EnsureCadenceDefault(persisted);

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var hasHistory = await _dbContext.StockHistoricalPrices
                .AsNoTracking()
                .AnyAsync(x => x.StockId == persisted.Id && x.Interval == "1d", cancellationToken);

            var tier = ResolveTier(persisted, hasHistory, trigger, nowUtc);
            if (tier is null)
            {
                _logger.LogDebug("History refresh skipped as not due for stock {StockId}", persisted.Id);
                return new StockHistoryRefreshResponse
                {
                    StockId = persisted.Id,
                    DeletedPoints = 0,
                    ImportedPoints = 0,
                    RateLimited = false,
                    AppliedTier = null,
                    SkippedNotDue = true,
                    NextDueAtUtc = ComputeNextDueAtUtc(persisted, nowUtc),
                };
            }

            _logger.LogInformation(
                "History refresh due for stock {StockId}: tier={Tier} trigger={Trigger}",
                persisted.Id,
                tier.Value,
                trigger);

            var fetchResult = trigger == StockHistoryRefreshTrigger.Manual
                ? await FetchHistoryBatchesAsync(persisted, cancellationToken)
                : await FetchTierHistoryBatchesAsync(persisted, tier.Value, nowUtc, cancellationToken);
            if (fetchResult.WasRateLimited)
            {
                _logger.LogInformation("Skipping history replacement for stock {StockId} because Yahoo cooldown is active.", stock.Id);
                ScheduleRetry(persisted, tier.Value, nowUtc);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new StockHistoryRefreshResponse
                {
                    StockId = persisted.Id,
                    DeletedPoints = 0,
                    ImportedPoints = 0,
                    RateLimited = true,
                    AppliedTier = tier.Value.ToString(),
                    NextDueAtUtc = ComputeNextDueAtUtc(persisted, nowUtc),
                };
            }

            var batches = fetchResult.Batches;
            var importedPoints = batches.Sum(batch => batch.Batch.Candles.Count);
            await UpsertHistoryBatchesAsync(persisted.Id, batches, cancellationToken);
            MarkTierSuccess(persisted, tier.Value, nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new StockHistoryRefreshResponse
            {
                StockId = persisted.Id,
                DeletedPoints = 0,
                ImportedPoints = importedPoints,
                RateLimited = false,
                AppliedTier = tier.Value.ToString(),
                NextDueAtUtc = ComputeNextDueAtUtc(persisted, nowUtc),
            };
        }
        catch
        {
            var persisted = await _dbContext.Stocks.FirstOrDefaultAsync(x => x.Id == stock.Id, cancellationToken);
            if (persisted is not null)
            {
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var hasHistory = await _dbContext.StockHistoricalPrices
                    .AsNoTracking()
                    .AnyAsync(x => x.StockId == persisted.Id && x.Interval == "1d", cancellationToken);
                var tier = ResolveTier(persisted, hasHistory, trigger, nowUtc);
                if (tier is not null)
                {
                    ScheduleRetry(persisted, tier.Value, nowUtc);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            throw;
        }
        finally
        {
            stockLock.Release();
        }
    }

    public async Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
    {
        var normalizedRange = NormalizeRange(range);
        var interval = GetInterval(normalizedRange);
        var from = GetFromTimestamp(normalizedRange);

        var data = await LoadHistoryRowsAsync(stock.Id, interval, from, cancellationToken);
        if ((data.Count == 0 || data.Any(NeedsMetadataBackfill)) && !string.IsNullOrWhiteSpace(stock.Ticker))
        {
            try
            {
                await SyncHistoricalDataForStockAsync(stock, cancellationToken);
                data = await LoadHistoryRowsAsync(stock.Id, interval, from, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "On-demand stock history sync failed for stock {StockId}", stock.Id);
            }
        }

        var currencyMetadata = data.LastOrDefault();
        var conversionContext = await _stockQuoteConversionService.GetConversionContextAsync(
            currencyMetadata?.QuoteCurrency,
            currencyMetadata?.FinancialCurrency,
            cancellationToken);
        var volumeMetrics = BuildVolumeMetrics(data, interval, conversionContext);
        var asOfUtc = data.LastOrDefault()?.Timestamp;

        return new StockHistoryResponse
        {
            Range = normalizedRange,
            Interval = interval,
            Currency = conversionContext.Metadata.QuoteCurrency,
            FinancialCurrency = conversionContext.Metadata.FinancialCurrency,
            NormalizedQuoteCurrency = conversionContext.Metadata.NormalizedQuoteCurrency,
            QuoteUnitMultiplier = conversionContext.Metadata.QuoteUnitMultiplier,
            RateToEur = conversionContext.ExchangeRate.RateToEur,
            RateTimestampUtc = conversionContext.ExchangeRate.RateTimestampUtc,
            RateSource = conversionContext.ExchangeRate.Source,
            ConversionWarning = conversionContext.Warning,
            AsOfUtc = asOfUtc,
            IsPotentiallyStale = IsPotentiallyStale(interval, asOfUtc),
            VolumeMetrics = volumeMetrics,
            Points = data
                .Select(point => _stockQuoteConversionService.BuildHistoryPointResponse(point, conversionContext))
                .ToList()
        };
    }

    private async Task<HistoryBatchFetchResult> FetchHistoryBatchesAsync(Stock stock, CancellationToken cancellationToken)
    {
        var providerSymbol = StockExchanges.ResolveProviderSymbol(stock.Ticker, stock.Exchange);

        var monthly = await FetchCandlesAsync(providerSymbol, "1mo", "5y", cancellationToken);
        if (monthly.WasRateLimited) return HistoryBatchFetchResult.RateLimited();
        var weekly = await FetchCandlesAsync(providerSymbol, "1wk", "1y", cancellationToken);
        if (weekly.WasRateLimited) return HistoryBatchFetchResult.RateLimited();
        // Daily history uses a 2-year lookback (≈504 trading days) to support SMA200 (252 obs),
        // annualized volatility (60-day window), and 12-month return calculations.
        var daily = await FetchCandlesAsync(providerSymbol, "1d", "2y", cancellationToken);
        if (daily.WasRateLimited) return HistoryBatchFetchResult.RateLimited();
        var hourly = await FetchCandlesAsync(providerSymbol, "1h", "7d", cancellationToken);
        if (hourly.WasRateLimited) return HistoryBatchFetchResult.RateLimited();
        var fiveMinute = await FetchCandlesAsync(providerSymbol, "5m", "1d", cancellationToken);
        if (fiveMinute.WasRateLimited) return HistoryBatchFetchResult.RateLimited();
        var tenMinute = AggregateToTenMinute(fiveMinute);

        return HistoryBatchFetchResult.Success(
        [
            new IntervalBatch("1mo", monthly),
            new IntervalBatch("1wk", weekly),
            new IntervalBatch("1d", daily),
            new IntervalBatch("1h", hourly),
            new IntervalBatch("10m", tenMinute),
        ]);
    }

    private async Task<HistoryBatchFetchResult> FetchTierHistoryBatchesAsync(
        Stock stock,
        StockHistoryRefreshTier tier,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var providerSymbol = StockExchanges.ResolveProviderSymbol(stock.Ticker, stock.Exchange);
        var lookbackDays = tier switch
        {
            StockHistoryRefreshTier.Incremental => _options.IncrementalLookbackDays,
            StockHistoryRefreshTier.Reconciliation => _options.ReconciliationLookbackDays,
            _ => _options.FullBackfillLookbackDays,
        };

        var fromUtc = nowUtc.Date.AddDays(-lookbackDays);
        var daily = await FetchCandlesByDateRangeAsync(providerSymbol, "1d", fromUtc, nowUtc, cancellationToken);
        if (daily.WasRateLimited)
        {
            return HistoryBatchFetchResult.RateLimited();
        }

        return HistoryBatchFetchResult.Success([new IntervalBatch("1d", daily)]);
    }

    private async Task UpsertHistoryBatchesAsync(int stockId, IEnumerable<IntervalBatch> batches, CancellationToken cancellationToken)
    {
        foreach (var entry in batches)
        {
            await UpsertCandlesAsync(stockId, entry.Interval, entry.Batch, cancellationToken);
        }
    }

    private sealed record IntervalBatch(string Interval, CandleBatch Batch);

    private static List<StockHistoricalPrice> BuildReplacementRows(int stockId, IEnumerable<IntervalBatch> batches)
    {
        var replacementRows = new List<StockHistoricalPrice>();
        foreach (var entry in batches)
        {
            foreach (var candle in entry.Batch.Candles)
            {
                replacementRows.Add(new StockHistoricalPrice
                {
                    StockId = stockId,
                    Timestamp = candle.Timestamp,
                    Interval = entry.Interval,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    AdjustedClose = candle.AdjustedClose,
                    QuoteCurrency = entry.Batch.QuoteCurrency,
                    FinancialCurrency = entry.Batch.FinancialCurrency,
                    NormalizedQuoteCurrency = entry.Batch.NormalizedQuoteCurrency,
                    QuoteUnitMultiplier = entry.Batch.QuoteUnitMultiplier,
                    Volume = candle.Volume
                });
            }
        }

        return replacementRows;
    }

    private async Task<int> ReplaceHistoryAsync(
        int stockId,
        IReadOnlyCollection<StockHistoricalPrice> replacementRows,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await ReplaceHistoryWithoutTransactionAsync(stockId, replacementRows, cancellationToken);
        }

        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        var deletedPoints = 0;

        await executionStrategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var existingRows = await _dbContext.StockHistoricalPrices
                .Where(x => x.StockId == stockId)
                .ToListAsync(cancellationToken);
            deletedPoints = existingRows.Count;

            if (deletedPoints > 0)
            {
                _dbContext.StockHistoricalPrices.RemoveRange(existingRows);
            }

            _dbContext.StockHistoricalPrices.AddRange(CloneReplacementRows(replacementRows));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        _dbContext.ChangeTracker.Clear();
        return deletedPoints;
    }

    private async Task<int> ReplaceHistoryWithoutTransactionAsync(
        int stockId,
        IReadOnlyCollection<StockHistoricalPrice> replacementRows,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var existingRows = await _dbContext.StockHistoricalPrices
            .Where(x => x.StockId == stockId)
            .ToListAsync(cancellationToken);
        var deletedPoints = existingRows.Count;

        if (deletedPoints > 0)
        {
            _dbContext.StockHistoricalPrices.RemoveRange(existingRows);
        }

        _dbContext.StockHistoricalPrices.AddRange(CloneReplacementRows(replacementRows));
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();

        return deletedPoints;
    }

    private static IEnumerable<StockHistoricalPrice> CloneReplacementRows(IEnumerable<StockHistoricalPrice> replacementRows)
    {
        foreach (var row in replacementRows)
        {
            yield return new StockHistoricalPrice
            {
                StockId = row.StockId,
                Timestamp = row.Timestamp,
                Interval = row.Interval,
                Open = row.Open,
                High = row.High,
                Low = row.Low,
                Close = row.Close,
                AdjustedClose = row.AdjustedClose,
                QuoteCurrency = row.QuoteCurrency,
                FinancialCurrency = row.FinancialCurrency,
                NormalizedQuoteCurrency = row.NormalizedQuoteCurrency,
                QuoteUnitMultiplier = row.QuoteUnitMultiplier,
                Volume = row.Volume
            };
        }
    }

    private static string NormalizeRange(string range)
    {
        var value = (range ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "5y" => "5y",
            "3y" => "3y",
            "1y" => "1y",
            "6m" => "6m",
            "3m" => "3m",
            "1m" => "1m",
            "1w" => "1w",
            "24h" => "24h",
            "today" => "today",
            _ => "5y"
        };
    }

    private static DateTime GetFromTimestamp(string normalizedRange)
    {
        var now = DateTime.UtcNow;
        return normalizedRange switch
        {
            "5y" => now.AddYears(-5),
            "3y" => now.AddYears(-3),
            "1y" => now.AddYears(-1),
            "6m" => now.AddMonths(-6),
            "3m" => now.AddMonths(-3),
            "1m" => now.AddMonths(-1),
            "1w" => now.AddDays(-7),
            "24h" => now.AddHours(-24),
            "today" => now.Date,
            _ => now.AddYears(-5)
        };
    }

    private static string GetInterval(string normalizedRange) => normalizedRange switch
    {
        "5y" or "3y" => "1mo",
        "1y" => "1wk",
        "6m" or "3m" or "1m" => "1d",
        "1w" => "1h",
        "24h" or "today" => "10m",
        _ => "1mo"
    };

    private async Task<List<StockHistoricalPrice>> LoadHistoryRowsAsync(int stockId, string interval, DateTime from, CancellationToken cancellationToken)
    {
        return await _dbContext.StockHistoricalPrices
            .AsNoTracking()
            .Where(x => x.StockId == stockId && x.Interval == interval && x.Timestamp >= from)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(cancellationToken);
    }

    private static StockHistoryVolumeMetricsResponse BuildVolumeMetrics(
        IReadOnlyList<StockHistoricalPrice> data,
        string interval,
        CurrencyConversionContext conversionContext)
    {
        if (data.Count == 0)
        {
            return new StockHistoryVolumeMetricsResponse();
        }

        var latestContext = ResolveLatestMetricsPoint(data, interval);
        if (latestContext.Point is null)
        {
            return new StockHistoryVolumeMetricsResponse
            {
                UsesCompletedCandle = latestContext.UsesCompletedCandle
            };
        }

        var latestIndex = latestContext.Index;
        var averageVolume20 = TryCalculateAverageVolume(data, latestIndex, 20);
        var averageVolume50 = TryCalculateAverageVolume(data, latestIndex, 50);
        var latestVolume = latestContext.Point.Volume > 0 ? latestContext.Point.Volume : (long?)null;

        var closeNormalized = conversionContext.Normalize(latestContext.Point.Close);
        var closeForTurnover = conversionContext.ConvertNormalizedToEur(closeNormalized) ?? closeNormalized;
        var turnoverCurrency = conversionContext.ExchangeRate.RateToEur is not null
            ? "EUR"
            : conversionContext.Metadata.NormalizedQuoteCurrency ?? conversionContext.Metadata.QuoteCurrency;

        decimal? turnover = null;
        if (latestVolume is > 0 && closeForTurnover > 0m)
        {
            turnover = closeForTurnover * latestVolume.Value;
        }

        decimal? relativeVolume = null;
        if (latestVolume is > 0 && averageVolume20 is > 0m)
        {
            relativeVolume = latestVolume.Value / averageVolume20.Value;
        }

        return new StockHistoryVolumeMetricsResponse
        {
            AverageVolume20 = averageVolume20,
            AverageVolume50 = averageVolume50,
            RelativeVolume = relativeVolume,
            Turnover = turnover,
            TurnoverCurrency = turnoverCurrency,
            LatestMetricsTimestamp = latestContext.Point.Timestamp,
            UsesCompletedCandle = latestContext.UsesCompletedCandle
        };
    }

    private static decimal? TryCalculateAverageVolume(IReadOnlyList<StockHistoricalPrice> data, int latestIndex, int periods)
    {
        var startIndex = latestIndex - periods + 1;
        if (startIndex < 0)
        {
            return null;
        }

        decimal totalVolume = 0m;
        for (var i = startIndex; i <= latestIndex; i++)
        {
            totalVolume += data[i].Volume;
        }

        var average = totalVolume / periods;
        return average > 0m ? average : null;
    }

    private static LatestMetricsContext ResolveLatestMetricsPoint(IReadOnlyList<StockHistoricalPrice> data, string interval)
    {
        if (data.Count == 0)
        {
            return new LatestMetricsContext(null, -1, false);
        }

        if (TryGetIntradayIntervalDuration(interval, out var intervalDuration))
        {
            var now = DateTime.UtcNow;
            for (var i = data.Count - 1; i >= 0; i--)
            {
                if ((data[i].Timestamp + intervalDuration) <= now)
                {
                    return new LatestMetricsContext(data[i], i, true);
                }
            }

            return new LatestMetricsContext(null, -1, true);
        }

        if (string.Equals(interval, "1d", StringComparison.Ordinal))
        {
            var today = DateTime.UtcNow.Date;
            for (var i = data.Count - 1; i >= 0; i--)
            {
                if (data[i].Timestamp.Date < today)
                {
                    return new LatestMetricsContext(data[i], i, true);
                }
            }
        }

        var latestIndex = data.Count - 1;
        return new LatestMetricsContext(data[latestIndex], latestIndex, false);
    }

    private static bool TryGetIntradayIntervalDuration(string interval, out TimeSpan duration)
    {
        switch (interval)
        {
            case "10m":
                duration = TimeSpan.FromMinutes(10);
                return true;
            case "1h":
                duration = TimeSpan.FromHours(1);
                return true;
            default:
                duration = default;
                return false;
        }
    }

    private static bool NeedsMetadataBackfill(StockHistoricalPrice price) =>
        string.IsNullOrWhiteSpace(price.QuoteCurrency) ||
        string.IsNullOrWhiteSpace(price.NormalizedQuoteCurrency) ||
        price.QuoteUnitMultiplier <= 0m;

    private async Task<List<Stock>> LoadDueAutomaticStocksAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        return await _dbContext.Stocks
            .Where(s =>
                s.HistoryRefreshCadence != StockHistoryRefreshCadence.Disabled &&
                !string.IsNullOrWhiteSpace(s.Ticker) &&
                (
                    (s.NextFullHistoryBackfillAtUtc.HasValue && s.NextFullHistoryBackfillAtUtc <= nowUtc) ||
                    (s.NextHistoryReconciliationAtUtc.HasValue && s.NextHistoryReconciliationAtUtc <= nowUtc) ||
                    (s.NextIncrementalHistoryRefreshAtUtc.HasValue && s.NextIncrementalHistoryRefreshAtUtc <= nowUtc) ||
                    !_dbContext.StockHistoricalPrices.Any(p => p.StockId == s.Id && p.Interval == "1d")
                ))
            .OrderBy(s => s.Id)
            .Take(_options.MaxAutomaticStocksPerRun)
            .ToListAsync(cancellationToken);
    }

    private static bool IsPotentiallyStale(string interval, DateTime? asOfUtc)
    {
        if (asOfUtc is null)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        var threshold = interval switch
        {
            "10m" => TimeSpan.FromHours(2),
            "1h" => TimeSpan.FromHours(8),
            "1d" => TimeSpan.FromDays(4),
            "1wk" => TimeSpan.FromDays(14),
            _ => TimeSpan.FromDays(45),
        };

        return (now - asOfUtc.Value) > threshold;
    }

    private static void EnsureCadenceDefault(Stock stock)
    {
        if (Enum.IsDefined(stock.HistoryRefreshCadence))
        {
            return;
        }

        stock.HistoryRefreshCadence = stock.TrackingStatus == StockTrackingStatus.CatalogOnly
            ? StockHistoryRefreshCadence.Weekly
            : StockHistoryRefreshCadence.Daily;
    }

    private StockHistoryRefreshTier? ResolveTier(Stock stock, bool hasHistory, StockHistoryRefreshTrigger trigger, DateTime nowUtc)
    {
        if (trigger == StockHistoryRefreshTrigger.Manual)
        {
            return StockHistoryRefreshTier.FullBackfill;
        }

        if (stock.HistoryRefreshCadence == StockHistoryRefreshCadence.Disabled)
        {
            return null;
        }

        if (!hasHistory)
        {
            return StockHistoryRefreshTier.FullBackfill;
        }

        if (stock.NextFullHistoryBackfillAtUtc is null || stock.NextFullHistoryBackfillAtUtc <= nowUtc)
        {
            return StockHistoryRefreshTier.FullBackfill;
        }

        if (stock.NextHistoryReconciliationAtUtc is null || stock.NextHistoryReconciliationAtUtc <= nowUtc)
        {
            return StockHistoryRefreshTier.Reconciliation;
        }

        if (stock.NextIncrementalHistoryRefreshAtUtc is null || stock.NextIncrementalHistoryRefreshAtUtc <= nowUtc)
        {
            return StockHistoryRefreshTier.Incremental;
        }

        return null;
    }

    private void MarkTierSuccess(Stock stock, StockHistoryRefreshTier tier, DateTime nowUtc)
    {
        switch (tier)
        {
            case StockHistoryRefreshTier.Incremental:
                stock.LastIncrementalHistoryRefreshSucceededAtUtc = nowUtc;
                stock.NextIncrementalHistoryRefreshAtUtc = nowUtc + GetIncrementalCadence(stock);
                break;
            case StockHistoryRefreshTier.Reconciliation:
                stock.LastHistoryReconciliationSucceededAtUtc = nowUtc;
                stock.NextHistoryReconciliationAtUtc = nowUtc + GetReconciliationCadence(stock);
                if (stock.NextIncrementalHistoryRefreshAtUtc is null || stock.NextIncrementalHistoryRefreshAtUtc < nowUtc)
                {
                    stock.NextIncrementalHistoryRefreshAtUtc = nowUtc + GetIncrementalCadence(stock);
                }
                break;
            case StockHistoryRefreshTier.FullBackfill:
                stock.LastFullHistoryBackfillSucceededAtUtc = nowUtc;
                stock.NextFullHistoryBackfillAtUtc = nowUtc + GetFullBackfillCadence(stock);
                if (stock.NextHistoryReconciliationAtUtc is null || stock.NextHistoryReconciliationAtUtc < nowUtc)
                {
                    stock.NextHistoryReconciliationAtUtc = nowUtc + GetReconciliationCadence(stock);
                }

                if (stock.NextIncrementalHistoryRefreshAtUtc is null || stock.NextIncrementalHistoryRefreshAtUtc < nowUtc)
                {
                    stock.NextIncrementalHistoryRefreshAtUtc = nowUtc + GetIncrementalCadence(stock);
                }
                break;
        }
    }

    private void ScheduleRetry(Stock stock, StockHistoryRefreshTier tier, DateTime nowUtc)
    {
        var retryAtUtc = nowUtc + _options.TransientFailureRetryDelay;
        switch (tier)
        {
            case StockHistoryRefreshTier.Incremental:
                stock.NextIncrementalHistoryRefreshAtUtc = retryAtUtc;
                break;
            case StockHistoryRefreshTier.Reconciliation:
                stock.NextHistoryReconciliationAtUtc = retryAtUtc;
                break;
            case StockHistoryRefreshTier.FullBackfill:
                stock.NextFullHistoryBackfillAtUtc = retryAtUtc;
                break;
        }
    }

    private DateTime? ComputeNextDueAtUtc(Stock stock, DateTime nowUtc)
    {
        if (stock.HistoryRefreshCadence == StockHistoryRefreshCadence.Disabled)
        {
            return null;
        }

        var due = new[] {
            stock.NextIncrementalHistoryRefreshAtUtc,
            stock.NextHistoryReconciliationAtUtc,
            stock.NextFullHistoryBackfillAtUtc
        }
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .OrderBy(x => x)
        .FirstOrDefault();

        return due == default ? nowUtc : due;
    }

    private TimeSpan GetIncrementalCadence(Stock stock)
        => stock.HistoryRefreshCadence == StockHistoryRefreshCadence.Weekly
            ? _options.IncrementalWeeklyCadence
            : _options.IncrementalDailyCadence;

    private TimeSpan GetReconciliationCadence(Stock stock)
        => stock.TrackingStatus == StockTrackingStatus.CatalogOnly
            ? _options.ReconciliationCatalogCadence
            : _options.ReconciliationTrackedCadence;

    private TimeSpan GetFullBackfillCadence(Stock stock)
        => stock.TrackingStatus == StockTrackingStatus.CatalogOnly
            ? _options.FullBackfillCatalogCadence
            : _options.FullBackfillTrackedCadence;

    private async Task UpsertCandlesAsync(int stockId, string interval, CandleBatch candleBatch, CancellationToken cancellationToken)
    {
        var candles = candleBatch.Candles;
        if (candles.Count == 0)
        {
            return;
        }

        var minTimestamp = candles.Min(x => x.Timestamp);
        var maxTimestamp = candles.Max(x => x.Timestamp);

        var existing = await _dbContext.StockHistoricalPrices
            .Where(x =>
                x.StockId == stockId &&
                x.Interval == interval &&
                x.Timestamp >= minTimestamp &&
                x.Timestamp <= maxTimestamp)
            .ToListAsync(cancellationToken);

        var existingByTimestamp = existing.ToDictionary(x => x.Timestamp, x => x);

        foreach (var candle in candles)
        {
            if (existingByTimestamp.TryGetValue(candle.Timestamp, out var row))
            {
                row.Open = candle.Open;
                row.High = candle.High;
                row.Low = candle.Low;
                row.Close = candle.Close;
                row.AdjustedClose = candle.AdjustedClose;
                row.QuoteCurrency = candleBatch.QuoteCurrency;
                row.FinancialCurrency = candleBatch.FinancialCurrency;
                row.NormalizedQuoteCurrency = candleBatch.NormalizedQuoteCurrency;
                row.QuoteUnitMultiplier = candleBatch.QuoteUnitMultiplier;
                row.Volume = candle.Volume;
            }
            else
            {
                _dbContext.StockHistoricalPrices.Add(new StockHistoricalPrice
                {
                    StockId = stockId,
                    Timestamp = candle.Timestamp,
                    Interval = interval,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    AdjustedClose = candle.AdjustedClose,
                    QuoteCurrency = candleBatch.QuoteCurrency,
                    FinancialCurrency = candleBatch.FinancialCurrency,
                    NormalizedQuoteCurrency = candleBatch.NormalizedQuoteCurrency,
                    QuoteUnitMultiplier = candleBatch.QuoteUnitMultiplier,
                    Volume = candle.Volume
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CandleFetchResult> FetchCandlesByDateRangeAsync(
        string symbol,
        string interval,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var period1 = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        if (period2 <= period1)
        {
            period2 = period1 + 3600;
        }

        var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval={interval}&period1={period1}&period2={period2}";
        var requestLabel = $"history:{interval}:{period1}-{period2}";
        return await FetchCandlesInternalAsync(url, requestLabel, interval, $"{fromUtc:yyyy-MM-dd}..{toUtc:yyyy-MM-dd}", cancellationToken);
    }

    private async Task<CandleFetchResult> FetchCandlesAsync(string symbol, string interval, string range, CancellationToken cancellationToken)
    {
        var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval={interval}&range={range}";
        var requestLabel = $"history:{interval}:{range}";
        return await FetchCandlesInternalAsync(url, requestLabel, interval, range, cancellationToken);
    }

    private async Task<CandleFetchResult> FetchCandlesInternalAsync(
        string url,
        string requestLabel,
        string interval,
        string rangeLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _yahooRequestCoordinator.GetAsync(
                url,
                requestLabel,
                new YahooRequestExecutionOptions(
                    MaxYahooRequestAttempts,
                    YahooRetryBaseDelay,
                    YahooRetryMaxDelay),
                cancellationToken);

            if (response.IsRateLimited)
            {
                _logger.LogWarning(
                    "Yahoo history request rate limited for interval={Interval} range={Range}; cooldownUntilUtc={CooldownUntilUtc}",
                    interval,
                    rangeLabel,
                    response.CooldownUntilUtc);
                return CandleFetchResult.RateLimited();
            }

            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning(
                    "Yahoo history request failed for interval={Interval} range={Range}: {StatusCode}",
                    interval,
                    rangeLabel,
                    (int)response.StatusCode);
                return CandleFetchResult.Success(CandleBatch.Empty);
            }

            using var doc = JsonDocument.Parse(response.Content);
            return CandleFetchResult.Success(ParseCandles(doc.RootElement));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Yahoo history request timed out for interval={Interval} range={Range}", interval, rangeLabel);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Yahoo history request failed for interval={Interval} range={Range}", interval, rangeLabel);
            throw;
        }
    }

    private static CandleBatch ParseCandles(JsonElement root)
    {
        if (!root.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var resultArray) ||
            resultArray.GetArrayLength() == 0)
        {
            return CandleBatch.Empty;
        }

        var result = resultArray[0];
        var meta = result.TryGetProperty("meta", out var metaElement) ? metaElement : default;
        if (!result.TryGetProperty("timestamp", out var timestamps) ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quoteArray) ||
            quoteArray.GetArrayLength() == 0)
        {
            return CandleBatch.Empty;
        }

        var quote = quoteArray[0];
        // Yahoo quote.close remains the canonical raw/unadjusted close stored in StockHistoricalPrice.Close.
        // Where indicators.adjclose is present and aligned, we additionally persist it into
        // StockHistoricalPrice.AdjustedClose for split/dividend-aware close-based analytics.
        // Raw OHLC remains unchanged because Yahoo does not expose adjusted OHLC in this model.
        if (!quote.TryGetProperty("close", out var closeArray))
        {
            return CandleBatch.Empty;
        }

        JsonElement adjustedCloseArray = default;
        if (indicators.TryGetProperty("adjclose", out var adjCloseWrapperArray) &&
            adjCloseWrapperArray.ValueKind == JsonValueKind.Array &&
            adjCloseWrapperArray.GetArrayLength() > 0)
        {
            var adjustedCloseWrapper = adjCloseWrapperArray[0];
            if (adjustedCloseWrapper.ValueKind == JsonValueKind.Object &&
                adjustedCloseWrapper.TryGetProperty("adjclose", out var parsedAdjustedCloseArray) &&
                parsedAdjustedCloseArray.ValueKind == JsonValueKind.Array &&
                parsedAdjustedCloseArray.GetArrayLength() == timestamps.GetArrayLength())
            {
                adjustedCloseArray = parsedAdjustedCloseArray;
            }
        }

        var openArray = quote.TryGetProperty("open", out var openElement) ? openElement : default;
        var highArray = quote.TryGetProperty("high", out var highElement) ? highElement : default;
        var lowArray = quote.TryGetProperty("low", out var lowElement) ? lowElement : default;
        var volumeArray = quote.TryGetProperty("volume", out var volumeElement) ? volumeElement : default;

        var candles = new List<CandleData>();
        var pointsCount = timestamps.GetArrayLength();
        for (var i = 0; i < pointsCount; i++)
        {
            if (!TryGetInt64(timestamps, i, out var unixTimestamp))
            {
                continue;
            }

            if (!TryGetDecimal(closeArray, i, out var close))
            {
                continue;
            }

            var open = TryGetDecimal(openArray, i, out var parsedOpen) ? parsedOpen : close;
            var high = TryGetDecimal(highArray, i, out var parsedHigh) ? parsedHigh : close;
            var low = TryGetDecimal(lowArray, i, out var parsedLow) ? parsedLow : close;
            var volume = TryGetInt64(volumeArray, i, out var parsedVolume) ? parsedVolume : 0L;
            decimal? adjustedClose = TryGetAdjustedClose(adjustedCloseArray, i, out var parsedAdjustedClose)
                ? parsedAdjustedClose
                : null;

            candles.Add(new CandleData(
                DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime,
                open,
                high,
                low,
                close,
                adjustedClose,
                volume));
        }

        var quoteCurrency = meta.TryGetProperty("currency", out var currencyProp) ? currencyProp.GetString() : null;
        var financialCurrency = meta.TryGetProperty("financialCurrency", out var financialCurrencyProp) ? financialCurrencyProp.GetString() : null;
        var currencyMetadata = QuoteCurrencyMetadata.Parse(quoteCurrency, financialCurrency);

        return new CandleBatch(
            candles.OrderBy(x => x.Timestamp).ToList(),
            currencyMetadata.QuoteCurrency,
            currencyMetadata.FinancialCurrency,
            currencyMetadata.NormalizedQuoteCurrency,
            currencyMetadata.QuoteUnitMultiplier);
    }

    private static CandleBatch AggregateToTenMinute(CandleBatch fiveMinuteCandles)
    {
        var aggregatedCandles = fiveMinuteCandles.Candles
            .GroupBy(x => new DateTime(
                x.Timestamp.Year,
                x.Timestamp.Month,
                x.Timestamp.Day,
                x.Timestamp.Hour,
                (x.Timestamp.Minute / 10) * 10,
                0,
                DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.Timestamp).ToList();
                return new CandleData(
                    g.Key,
                    ordered.First().Open,
                    ordered.Max(x => x.High),
                    ordered.Min(x => x.Low),
                    ordered.Last().Close,
                    null,
                    ordered.Sum(x => x.Volume));
            })
            .ToList();

        return new CandleBatch(
            aggregatedCandles,
            fiveMinuteCandles.QuoteCurrency,
            fiveMinuteCandles.FinancialCurrency,
            fiveMinuteCandles.NormalizedQuoteCurrency,
            fiveMinuteCandles.QuoteUnitMultiplier);
    }

    private static bool TryGetDecimal(JsonElement arrayElement, int index, out decimal value)
    {
        value = 0m;
        if (arrayElement.ValueKind != JsonValueKind.Array || index < 0 || index >= arrayElement.GetArrayLength())
        {
            return false;
        }

        var element = arrayElement[index];
        if (element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        try
        {
            if (element.TryGetDecimal(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                value = Convert.ToDecimal(element.GetDouble(), CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryGetInt64(JsonElement arrayElement, int index, out long value)
    {
        value = 0L;
        if (arrayElement.ValueKind != JsonValueKind.Array || index < 0 || index >= arrayElement.GetArrayLength())
        {
            return false;
        }

        var element = arrayElement[index];
        if (element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        try
        {
            if (element.TryGetInt64(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                value = Convert.ToInt64(element.GetDouble());
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryGetAdjustedClose(JsonElement arrayElement, int index, out decimal value)
    {
        if (!TryGetDecimal(arrayElement, index, out value))
        {
            return false;
        }

        return value > 0m;
    }

    private static StockHistoryRefreshOptions NormalizeOptions(StockHistoryRefreshOptions raw)
    {
        return new StockHistoryRefreshOptions
        {
            IncrementalLookbackDays = raw.IncrementalLookbackDays > 0 ? raw.IncrementalLookbackDays : 10,
            ReconciliationLookbackDays = raw.ReconciliationLookbackDays > 0 ? raw.ReconciliationLookbackDays : 183,
            FullBackfillLookbackDays = raw.FullBackfillLookbackDays > 0 ? raw.FullBackfillLookbackDays : 730,
            IncrementalDailyCadence = raw.IncrementalDailyCadence > TimeSpan.Zero ? raw.IncrementalDailyCadence : TimeSpan.FromDays(1),
            IncrementalWeeklyCadence = raw.IncrementalWeeklyCadence > TimeSpan.Zero ? raw.IncrementalWeeklyCadence : TimeSpan.FromDays(7),
            ReconciliationTrackedCadence = raw.ReconciliationTrackedCadence > TimeSpan.Zero ? raw.ReconciliationTrackedCadence : TimeSpan.FromDays(7),
            ReconciliationCatalogCadence = raw.ReconciliationCatalogCadence > TimeSpan.Zero ? raw.ReconciliationCatalogCadence : TimeSpan.FromDays(30),
            FullBackfillTrackedCadence = raw.FullBackfillTrackedCadence > TimeSpan.Zero ? raw.FullBackfillTrackedCadence : TimeSpan.FromDays(30),
            FullBackfillCatalogCadence = raw.FullBackfillCatalogCadence > TimeSpan.Zero ? raw.FullBackfillCatalogCadence : TimeSpan.FromDays(30),
            TransientFailureRetryDelay = raw.TransientFailureRetryDelay > TimeSpan.Zero ? raw.TransientFailureRetryDelay : TimeSpan.FromHours(2),
            MaxAutomaticStocksPerRun = raw.MaxAutomaticStocksPerRun > 0 ? raw.MaxAutomaticStocksPerRun : 100,
        };
    }

    private sealed record CandleData(
        DateTime Timestamp,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal? AdjustedClose,
        long Volume);

    private sealed record CandleBatch(
        IReadOnlyList<CandleData> Candles,
        string? QuoteCurrency,
        string? FinancialCurrency,
        string? NormalizedQuoteCurrency,
        decimal QuoteUnitMultiplier)
    {
        public static CandleBatch Empty { get; } = new(Array.Empty<CandleData>(), null, null, null, 1m);
    }

    private sealed record CandleFetchResult(CandleBatch Batch, bool WasRateLimited)
    {
        public static CandleFetchResult Success(CandleBatch batch) => new(batch, false);
        public static CandleFetchResult RateLimited() => new(CandleBatch.Empty, true);

        public static implicit operator CandleBatch(CandleFetchResult result) => result.Batch;
    }

    private sealed record HistoryBatchFetchResult(IReadOnlyList<IntervalBatch> Batches, bool WasRateLimited)
    {
        public static HistoryBatchFetchResult Success(IReadOnlyList<IntervalBatch> batches) => new(batches, false);
        public static HistoryBatchFetchResult RateLimited() => new(Array.Empty<IntervalBatch>(), true);
    }

    private sealed record LatestMetricsContext(
        StockHistoricalPrice? Point,
        int Index,
        bool UsesCompletedCandle);
}
