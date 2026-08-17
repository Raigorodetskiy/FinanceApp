using System.Threading.Channels;
using FinanceApp.API.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services;

public sealed class IndexConstituentsBatchQuoteRefreshJobOptions
{
    public int QueueCapacity { get; init; } = 32;
    public int RegistryCapacity { get; init; } = 128;
    public TimeSpan CompletedJobTtl { get; init; } = TimeSpan.FromMinutes(30);
    public int MaxErrorMessageLength { get; init; } = 240;
}

public interface IIndexConstituentsBatchQuoteRefreshJobService
{
    IndexConstituentsBatchQuoteRefreshJobEnqueueResult Enqueue(int marketIndexId);
    bool TryGetJob(int marketIndexId, string jobId, out IndexConstituentsBatchQuoteRefreshJobResponse? job);
}

public sealed class IndexConstituentsBatchQuoteRefreshJobService
    : BackgroundService, IIndexConstituentsBatchQuoteRefreshJobService
{
    private const string RateLimitMessage = "Поставщик временно ограничил запросы. Часть цен не обновлена.";
    private const string InterruptedMessage = "Обновление прервано из-за перезапуска приложения. Повторите попытку.";
    private const string GenericFailureMessage = "Не удалось завершить пакетное обновление цен. Попробуйте позже.";

    private readonly object _sync = new();
    private readonly Dictionary<string, JobEntry> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _activeJobsByIndexId = new();
    private readonly Channel<string> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IndexConstituentsBatchQuoteRefreshJobOptions _options;
    private readonly ILogger<IndexConstituentsBatchQuoteRefreshJobService> _logger;

    public IndexConstituentsBatchQuoteRefreshJobService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<IndexConstituentsBatchQuoteRefreshJobOptions> options,
        ILogger<IndexConstituentsBatchQuoteRefreshJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;

        var raw = options.Value;
        _options = new IndexConstituentsBatchQuoteRefreshJobOptions
        {
            QueueCapacity = raw.QueueCapacity > 0 ? raw.QueueCapacity : 32,
            RegistryCapacity = raw.RegistryCapacity > 0 ? raw.RegistryCapacity : 128,
            CompletedJobTtl = raw.CompletedJobTtl > TimeSpan.Zero ? raw.CompletedJobTtl : TimeSpan.FromMinutes(30),
            MaxErrorMessageLength = raw.MaxErrorMessageLength > 0 ? raw.MaxErrorMessageLength : 240,
        };

        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public IndexConstituentsBatchQuoteRefreshJobEnqueueResult Enqueue(int marketIndexId)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        JobEntry entry;
        lock (_sync)
        {
            CleanupLocked(now);

            if (_activeJobsByIndexId.TryGetValue(marketIndexId, out var activeId)
                && _jobs.TryGetValue(activeId, out var active)
                && active.State is IndexConstituentsBatchQuoteRefreshJobState.Queued
                    or IndexConstituentsBatchQuoteRefreshJobState.Running)
            {
                return new IndexConstituentsBatchQuoteRefreshJobEnqueueResult
                {
                    Status = IndexConstituentsBatchQuoteRefreshJobEnqueueStatus.ReusedActiveJob,
                    Job = ToResponse(active, reusedActiveJob: true),
                };
            }

            if (_jobs.Count >= _options.RegistryCapacity && !TryEvictOneTerminalJobLocked())
            {
                return new IndexConstituentsBatchQuoteRefreshJobEnqueueResult
                {
                    Status = IndexConstituentsBatchQuoteRefreshJobEnqueueStatus.QueueFull,
                };
            }

            entry = new JobEntry
            {
                JobId = Guid.NewGuid().ToString("N"),
                MarketIndexId = marketIndexId,
                State = IndexConstituentsBatchQuoteRefreshJobState.Queued,
                CreatedAtUtc = now,
            };
            _jobs[entry.JobId] = entry;
            _activeJobsByIndexId[marketIndexId] = entry.JobId;
        }

        if (!_queue.Writer.TryWrite(entry.JobId))
        {
            lock (_sync) { RemoveJobLocked(entry.JobId); }
            return new IndexConstituentsBatchQuoteRefreshJobEnqueueResult
            {
                Status = IndexConstituentsBatchQuoteRefreshJobEnqueueStatus.QueueFull,
            };
        }

        return new IndexConstituentsBatchQuoteRefreshJobEnqueueResult
        {
            Status = IndexConstituentsBatchQuoteRefreshJobEnqueueStatus.Enqueued,
            Job = ToResponse(entry, reusedActiveJob: false),
        };
    }

    public bool TryGetJob(int marketIndexId, string jobId, out IndexConstituentsBatchQuoteRefreshJobResponse? job)
    {
        job = null;
        if (string.IsNullOrWhiteSpace(jobId)) return false;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        lock (_sync)
        {
            CleanupLocked(now);
            if (!_jobs.TryGetValue(jobId, out var entry)) return false;
            if (entry.MarketIndexId != marketIndexId) return false;
            job = ToResponse(entry, reusedActiveJob: false);
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_queue.Reader.TryRead(out var jobId))
                {
                    await ProcessJobAsync(jobId, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            InterruptActiveJobs(InterruptedMessage);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        InterruptActiveJobs(InterruptedMessage);
        return base.StopAsync(cancellationToken);
    }

    private async Task ProcessJobAsync(string jobId, CancellationToken stoppingToken)
    {
        if (!TryMarkRunning(jobId)) return;

        int marketIndexId;
        lock (_sync)
        {
            if (!_jobs.TryGetValue(jobId, out var e)) return;
            marketIndexId = e.MarketIndexId;
        }

        _logger.LogInformation(
            "Batch quote refresh started: jobId={JobId} indexId={IndexId}", jobId, marketIndexId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quoteFetchService = scope.ServiceProvider.GetRequiredService<IStockQuoteFetchService>();

            var marketIndex = await context.MarketIndices
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == marketIndexId, stoppingToken);

            if (marketIndex is null)
            {
                MarkCompleted(jobId, IndexConstituentsBatchQuoteRefreshJobState.Failed, error: "Индекс не найден.");
                return;
            }

            if (marketIndex.IsArchived)
            {
                MarkCompleted(jobId, IndexConstituentsBatchQuoteRefreshJobState.Failed,
                    error: "Нельзя обновлять цены для архивного индекса.");
                return;
            }

            var stocks = await context.StockMarketIndices
                .Include(x => x.Stock)
                .Where(x => x.MarketIndexId == marketIndexId && x.EffectiveTo == null)
                .OrderBy(x => x.StockId)
                .Select(x => x.Stock!)
                .AsNoTracking()
                .ToListAsync(stoppingToken);

            var total = stocks.Count;
            SetTotal(jobId, total);

            _logger.LogInformation(
                "Batch quote refresh: jobId={JobId} indexId={IndexId} total={Total}",
                jobId, marketIndexId, total);

            var counters = new Counters();

            for (var i = 0; i < stocks.Count; i++)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var stock = stocks[i];
                if (string.IsNullOrWhiteSpace(stock.Ticker))
                {
                    counters.ProviderFailed++;
                    UpdateProgress(jobId, i + 1, counters);
                    continue;
                }

                StockQuoteFetchResult fetchResult;
                try
                {
                    fetchResult = await quoteFetchService.FetchAsync(
                        stock.Ticker,
                        stock.Exchange ?? string.Empty,
                        stock.FinanzenNetSlug,
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Batch quote fetch exception: jobId={JobId} stockId={StockId} ticker={Ticker}",
                        jobId, stock.Id, stock.Ticker);
                    counters.ProviderFailed++;
                    UpdateProgress(jobId, i + 1, counters);
                    continue;
                }

                if (fetchResult.IsRateLimited)
                {
                    counters.RateLimited++;
                    UpdateProgress(jobId, i + 1, counters);
                    _logger.LogWarning(
                        "Batch quote refresh rate-limited at stock {StockId}/{Ticker}: jobId={JobId} indexId={IndexId} processed={Processed}/{Total}",
                        stock.Id, stock.Ticker, jobId, marketIndexId, i + 1, total);
                    MarkCompleted(jobId, IndexConstituentsBatchQuoteRefreshJobState.RateLimited,
                        processed: i + 1, total: total, counters: counters, error: RateLimitMessage);
                    return;
                }

                if (!fetchResult.IsSuccess || fetchResult.Quote is null)
                {
                    _logger.LogDebug(
                        "Batch quote fetch failed: jobId={JobId} stockId={StockId} ticker={Ticker} status={Status}",
                        jobId, stock.Id, stock.Ticker, fetchResult.StatusCode);
                    counters.ProviderFailed++;
                    UpdateProgress(jobId, i + 1, counters);
                    continue;
                }

                var quote = fetchResult.Quote;

                if (quote.IsStale || !string.IsNullOrEmpty(quote.DelayWarning))
                {
                    counters.Delayed++;
                    UpdateProgress(jobId, i + 1, counters);
                    continue;
                }

                if (quote.CurrentPriceEur is null)
                {
                    counters.NoEurConversion++;
                    UpdateProgress(jobId, i + 1, counters);
                    continue;
                }

                // Build persistence patch
                var incomingPrice = Math.Round(quote.CurrentPriceEur.Value, 2);
                var incomingChange = quote.ChangeEur.HasValue ? Math.Round(quote.ChangeEur.Value, 4) : (decimal?)null;
                var incomingPercent = Math.Round(quote.PercentChange, 4);
                var incomingAt = quote.PriceTimestampUtc;

                try
                {
                    using var persistScope = _scopeFactory.CreateScope();
                    var persistContext = persistScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var existing = await persistContext.Stocks.FindAsync([stock.Id], stoppingToken);
                    if (existing is null)
                    {
                        counters.PersistFailed++;
                        UpdateProgress(jobId, i + 1, counters);
                        continue;
                    }

                    // Stale-rejection: incoming timestamp must not be older than what's stored
                    if (incomingAt.HasValue &&
                        existing.CurrentPriceAt.HasValue &&
                        incomingAt.Value < existing.CurrentPriceAt.Value)
                    {
                        _logger.LogDebug(
                            "Batch quote: stale timestamp rejected for stockId={StockId} ticker={Ticker} incoming={Incoming} stored={Stored}",
                            stock.Id, stock.Ticker, incomingAt.Value, existing.CurrentPriceAt.Value);
                        counters.StaleRejected++;
                        UpdateProgress(jobId, i + 1, counters);
                        continue;
                    }

                    existing.CurrentPrice = incomingPrice;
                    existing.CurrentPriceChange = incomingChange;
                    existing.CurrentPriceChangePercent = incomingPercent;
                    existing.CurrentPriceAt = incomingAt;
                    existing.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

                    await persistContext.SaveChangesAsync(stoppingToken);
                    counters.Succeeded++;

                    _logger.LogDebug(
                        "Batch quote: persisted stockId={StockId} ticker={Ticker} priceEur={Price}",
                        stock.Id, stock.Ticker, incomingPrice);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Batch quote persist failed: jobId={JobId} stockId={StockId} ticker={Ticker}",
                        jobId, stock.Id, stock.Ticker);
                    counters.PersistFailed++;
                }

                UpdateProgress(jobId, i + 1, counters);
            }

            MarkCompleted(jobId, IndexConstituentsBatchQuoteRefreshJobState.Succeeded,
                processed: total, total: total, counters: counters);

            _logger.LogInformation(
                "Batch quote refresh completed: jobId={JobId} indexId={IndexId} total={Total} succeeded={Succeeded} delayed={Delayed} noEur={NoEur} staleRejected={StaleRejected} providerFailed={ProviderFailed} persistFailed={PersistFailed} rateLimited={RateLimited}",
                jobId, marketIndexId, total, counters.Succeeded, counters.Delayed, counters.NoEurConversion,
                counters.StaleRejected, counters.ProviderFailed, counters.PersistFailed, counters.RateLimited);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            MarkCompleted(jobId, IndexConstituentsBatchQuoteRefreshJobState.Interrupted,
                error: InterruptedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected failure in batch quote refresh job {JobId} (indexId={IndexId})", jobId, marketIndexId);
            MarkCompleted(jobId, IndexConstituentsBatchQuoteRefreshJobState.Failed,
                error: GenericFailureMessage);
        }
    }

    private bool TryMarkRunning(string jobId)
    {
        lock (_sync)
        {
            if (!_jobs.TryGetValue(jobId, out var entry)) return false;
            if (entry.State != IndexConstituentsBatchQuoteRefreshJobState.Queued) return false;
            entry.State = IndexConstituentsBatchQuoteRefreshJobState.Running;
            entry.StartedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            return true;
        }
    }

    private void SetTotal(string jobId, int total)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(jobId, out var entry))
            {
                entry.Total = total;
            }
        }
    }

    private void UpdateProgress(string jobId, int processed, Counters counters)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(jobId, out var entry))
            {
                entry.Processed = processed;
                ApplyCounters(entry, counters);
            }
        }
    }

    private void MarkCompleted(
        string jobId,
        IndexConstituentsBatchQuoteRefreshJobState state,
        int processed = 0,
        int total = 0,
        Counters? counters = null,
        string? error = null)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        lock (_sync)
        {
            if (!_jobs.TryGetValue(jobId, out var entry)) return;

            entry.State = state;
            entry.Processed = processed > 0 ? processed : entry.Processed;
            if (total > 0) entry.Total = total;
            if (counters != null) ApplyCounters(entry, counters);
            entry.Error = ToSafeError(error);
            entry.CompletedAtUtc = now;
            entry.ExpiresAtUtc = now.Add(_options.CompletedJobTtl);

            if (_activeJobsByIndexId.TryGetValue(entry.MarketIndexId, out var currentJobId)
                && string.Equals(currentJobId, entry.JobId, StringComparison.Ordinal))
            {
                _activeJobsByIndexId.Remove(entry.MarketIndexId);
            }

            CleanupLocked(now);
        }
    }

    private static void ApplyCounters(JobEntry entry, Counters counters)
    {
        entry.Succeeded = counters.Succeeded;
        entry.Delayed = counters.Delayed;
        entry.NoEurConversion = counters.NoEurConversion;
        entry.StaleRejected = counters.StaleRejected;
        entry.ProviderFailed = counters.ProviderFailed;
        entry.PersistFailed = counters.PersistFailed;
        entry.RateLimited = counters.RateLimited;
    }

    private void InterruptActiveJobs(string error)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        lock (_sync)
        {
            foreach (var entry in _jobs.Values.Where(x =>
                         x.State is IndexConstituentsBatchQuoteRefreshJobState.Queued
                             or IndexConstituentsBatchQuoteRefreshJobState.Running))
            {
                entry.State = IndexConstituentsBatchQuoteRefreshJobState.Interrupted;
                entry.Error = ToSafeError(error);
                entry.CompletedAtUtc = now;
                entry.ExpiresAtUtc = now.Add(_options.CompletedJobTtl);
            }

            _activeJobsByIndexId.Clear();
            CleanupLocked(now);
        }
    }

    private void CleanupLocked(DateTime now)
    {
        foreach (var entry in _jobs.Values
                     .Where(x => x.ExpiresAtUtc.HasValue && x.ExpiresAtUtc.Value <= now)
                     .ToList())
        {
            RemoveJobLocked(entry.JobId);
        }

        while (_jobs.Count > _options.RegistryCapacity && TryEvictOneTerminalJobLocked())
        {
        }
    }

    private bool TryEvictOneTerminalJobLocked()
    {
        var candidate = _jobs.Values
            .Where(x => x.ExpiresAtUtc.HasValue)
            .OrderBy(x => x.CompletedAtUtc ?? x.CreatedAtUtc)
            .FirstOrDefault();
        if (candidate is null) return false;
        RemoveJobLocked(candidate.JobId);
        return true;
    }

    private void RemoveJobLocked(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var entry)) return;
        _jobs.Remove(jobId);
        if (_activeJobsByIndexId.TryGetValue(entry.MarketIndexId, out var cur)
            && string.Equals(cur, jobId, StringComparison.Ordinal))
        {
            _activeJobsByIndexId.Remove(entry.MarketIndexId);
        }
    }

    private string? ToSafeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;
        var t = error.Trim();
        return t.Length <= _options.MaxErrorMessageLength ? t : t[.._options.MaxErrorMessageLength];
    }

    private IndexConstituentsBatchQuoteRefreshJobResponse ToResponse(JobEntry entry, bool reusedActiveJob)
        => new()
        {
            JobId = entry.JobId,
            MarketIndexId = entry.MarketIndexId,
            State = entry.State,
            ReusedActiveJob = reusedActiveJob,
            CreatedAtUtc = entry.CreatedAtUtc,
            StartedAtUtc = entry.StartedAtUtc,
            CompletedAtUtc = entry.CompletedAtUtc,
            ExpiresAtUtc = entry.ExpiresAtUtc,
            Total = entry.Total,
            Processed = entry.Processed,
            Succeeded = entry.Succeeded,
            Delayed = entry.Delayed,
            NoEurConversion = entry.NoEurConversion,
            StaleRejected = entry.StaleRejected,
            ProviderFailed = entry.ProviderFailed,
            PersistFailed = entry.PersistFailed,
            RateLimited = entry.RateLimited,
            Error = entry.Error,
        };

    private sealed class JobEntry
    {
        public required string JobId { get; init; }
        public required int MarketIndexId { get; init; }
        public required DateTime CreatedAtUtc { get; init; }
        public IndexConstituentsBatchQuoteRefreshJobState State { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public int Total { get; set; }
        public int Processed { get; set; }
        public int Succeeded { get; set; }
        public int Delayed { get; set; }
        public int NoEurConversion { get; set; }
        public int StaleRejected { get; set; }
        public int ProviderFailed { get; set; }
        public int PersistFailed { get; set; }
        public int RateLimited { get; set; }
        public string? Error { get; set; }
    }

    private sealed class Counters
    {
        public int Succeeded { get; set; }
        public int Delayed { get; set; }
        public int NoEurConversion { get; set; }
        public int StaleRejected { get; set; }
        public int ProviderFailed { get; set; }
        public int PersistFailed { get; set; }
        public int RateLimited { get; set; }
    }
}
