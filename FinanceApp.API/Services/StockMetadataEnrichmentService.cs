using System.Security.Claims;
using System.Text.Json;
using FinanceApp.API.Models;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services;

public sealed class StockMetadataEnrichmentOptions
{
    public bool AutomaticSweepEnabled { get; set; } = true;
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(12);
    public int BatchSize { get; set; } = 50;
    public int MaxConcurrency { get; set; } = 1;
    public int MaxRetryCount { get; set; } = 3;
    public TimeSpan RetryCooldown { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan StaleInterval { get; set; } = TimeSpan.FromDays(30);
    public StockMetadataEnrichmentConfidence AutoApplyConfidenceThreshold { get; set; } = StockMetadataEnrichmentConfidence.High;
}

public interface IStockMetadataEnrichmentService
{
    Task<Guid> CreateJobAsync(CreateStockMetadataEnrichmentJobRequest request, string? initiatedByUserId, CancellationToken cancellationToken = default);
    Task<StockMetadataEnrichmentJobResponse?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<StockMetadataEnrichmentResultPageResponse?> GetResultsAsync(Guid jobId, int page, int pageSize, string? decision, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> ApplyAsync(Guid jobId, ApplyStockMetadataEnrichmentJobRequest request, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> ReviewAsync(Guid jobId, long resultId, ReviewStockMetadataEnrichmentResultRequest request, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<bool> RetryAsync(Guid jobId, RetryStockMetadataEnrichmentJobRequest request, CancellationToken cancellationToken = default);
    Task<Guid> EnqueueSelectedAsync(IEnumerable<int> stockIds, string? initiatedByUserId, CancellationToken cancellationToken = default);
}

public sealed class StockMetadataEnrichmentService : BackgroundService, IStockMetadataEnrichmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStockMetadataCuratedSnapshotService _curatedSnapshotService;
    private readonly IYahooAssetProfileService _yahooAssetProfileService;
    private readonly StockMetadataEnrichmentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StockMetadataEnrichmentService> _logger;

    public StockMetadataEnrichmentService(
        IServiceScopeFactory scopeFactory,
        IStockMetadataCuratedSnapshotService curatedSnapshotService,
        IYahooAssetProfileService yahooAssetProfileService,
        IOptions<StockMetadataEnrichmentOptions> options,
        TimeProvider timeProvider,
        ILogger<StockMetadataEnrichmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _curatedSnapshotService = curatedSnapshotService;
        _yahooAssetProfileService = yahooAssetProfileService;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options.Value;
        _options.BatchSize = Math.Max(10, _options.BatchSize);
        _options.MaxConcurrency = Math.Max(1, _options.MaxConcurrency);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextSweepAt = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (_options.AutomaticSweepEnabled && now >= nextSweepAt)
                {
                    await EnsureScheduledSweepJobAsync(stoppingToken);
                    nextSweepAt = now.Add(_options.SweepInterval);
                }

                var hasWork = await TryProcessOneJobAsync(stoppingToken);
                if (!hasWork)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled metadata enrichment worker failure.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    public async Task<Guid> CreateJobAsync(CreateStockMetadataEnrichmentJobRequest request, string? initiatedByUserId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (request.Scope == StockMetadataEnrichmentScope.Selected && (request.SelectedStockIds is null || request.SelectedStockIds.Count == 0))
        {
            throw new InvalidOperationException("Для режима Selected требуется непустой список stockIds.");
        }

        if (request.Scope != StockMetadataEnrichmentScope.Selected)
        {
            var hasActiveGlobal = await db.StockMetadataEnrichmentJobs
                .AnyAsync(x => x.Scope != StockMetadataEnrichmentScope.Selected
                    && (x.Status == StockMetadataEnrichmentJobStatus.Queued || x.Status == StockMetadataEnrichmentJobStatus.Running), cancellationToken);
            if (hasActiveGlobal)
            {
                throw new InvalidOperationException("Уже выполняется глобальная задача обогащения метаданных.");
            }
        }

        var job = new StockMetadataEnrichmentJob
        {
            Id = Guid.NewGuid(),
            Scope = request.Scope,
            IsDryRun = request.DryRun,
            SelectedStockIdsJson = request.Scope == StockMetadataEnrichmentScope.Selected
                ? JsonSerializer.Serialize(request.SelectedStockIds!.Distinct().OrderBy(x => x), JsonOptions)
                : null,
            InitiatedByUserId = string.IsNullOrWhiteSpace(initiatedByUserId) ? null : initiatedByUserId,
            MetadataStaleAfterUtc = request.MetadataStaleAfterUtc,
            Status = StockMetadataEnrichmentJobStatus.Queued,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.StockMetadataEnrichmentJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return job.Id;
    }

    public async Task<Guid> EnqueueSelectedAsync(IEnumerable<int> stockIds, string? initiatedByUserId, CancellationToken cancellationToken = default)
    {
        return await CreateJobAsync(new CreateStockMetadataEnrichmentJobRequest
        {
            Scope = StockMetadataEnrichmentScope.Selected,
            SelectedStockIds = stockIds.Distinct().ToList(),
            DryRun = false,
        }, initiatedByUserId, cancellationToken);
    }

    public async Task<StockMetadataEnrichmentJobResponse?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.StockMetadataEnrichmentJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        return job is null ? null : MapJob(job);
    }

    public async Task<StockMetadataEnrichmentResultPageResponse?> GetResultsAsync(Guid jobId, int page, int pageSize, string? decision, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = await db.StockMetadataEnrichmentJobs.AsNoTracking().AnyAsync(x => x.Id == jobId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var query = db.StockMetadataEnrichmentResults.AsNoTracking().Where(x => x.JobId == jobId);
        if (!string.IsNullOrWhiteSpace(decision) && Enum.TryParse<StockMetadataEnrichmentDecision>(decision, true, out var parsedDecision))
        {
            query = query.Where(x => x.IsinDecision == parsedDecision || x.WknDecision == parsedDecision || x.IndustryDecision == parsedDecision);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => MapResult(x))
            .ToListAsync(cancellationToken);

        return new StockMetadataEnrichmentResultPageResponse
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<(bool Success, string Message)> ApplyAsync(Guid jobId, ApplyStockMetadataEnrichmentJobRequest request, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.StockMetadataEnrichmentJobs.FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null)
        {
            return (false, "Задача не найдена.");
        }

        if (!job.IsDryRun)
        {
            return (false, "Применение поддерживается только для dry-run задач.");
        }

        if (job.Status is not (StockMetadataEnrichmentJobStatus.Completed or StockMetadataEnrichmentJobStatus.CompletedWithWarnings))
        {
            return (false, "Задача ещё не завершена.");
        }

        var results = await db.StockMetadataEnrichmentResults
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var stockIds = results.Select(x => x.StockId).Distinct().ToList();
        var stocks = await db.Stocks.Where(x => stockIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var result in results)
        {
            if (!stocks.TryGetValue(result.StockId, out var stock))
            {
                result.Diagnostics = AppendDiagnostics(result.Diagnostics, "Stock no longer exists.");
                result.IsinDecision = ForceTerminal(result.IsinDecision, StockMetadataEnrichmentDecision.Failed);
                result.WknDecision = ForceTerminal(result.WknDecision, StockMetadataEnrichmentDecision.Failed);
                result.IndustryDecision = ForceTerminal(result.IndustryDecision, StockMetadataEnrichmentDecision.Failed);
                result.UpdatedAtUtc = now;
                continue;
            }

            var canApply = !request.OnlyManuallyApproved || result.ManuallyApproved;
            if (!canApply || result.Rejected)
            {
                continue;
            }

            var stale = !string.Equals(StockIdentifiers.Normalize(stock.Isin), result.OldIsin, StringComparison.Ordinal)
                || !string.Equals(StockIdentifiers.Normalize(stock.Wkn), result.OldWkn, StringComparison.Ordinal)
                || stock.IndustryId != result.OldIndustryId;
            if (stale)
            {
                result.IsinDecision = MarkConflictIfPending(result.IsinDecision);
                result.WknDecision = MarkConflictIfPending(result.WknDecision);
                result.IndustryDecision = MarkConflictIfPending(result.IndustryDecision);
                result.Diagnostics = AppendDiagnostics(result.Diagnostics, "Skipped stale result due to changed stock metadata.");
                result.UpdatedAtUtc = now;
                continue;
            }

            if (result.IsinDecision == StockMetadataEnrichmentDecision.WouldApply && result.CandidateIsin is not null)
            {
                if (string.IsNullOrWhiteSpace(stock.Isin))
                {
                    stock.Isin = result.CandidateIsin;
                    result.IsinDecision = StockMetadataEnrichmentDecision.Applied;
                }
                else
                {
                    result.IsinDecision = StockMetadataEnrichmentDecision.Unchanged;
                }
            }

            if (result.WknDecision == StockMetadataEnrichmentDecision.WouldApply && result.CandidateWkn is not null)
            {
                if (string.IsNullOrWhiteSpace(stock.Wkn))
                {
                    stock.Wkn = result.CandidateWkn;
                    result.WknDecision = StockMetadataEnrichmentDecision.Applied;
                }
                else
                {
                    result.WknDecision = StockMetadataEnrichmentDecision.Unchanged;
                }
            }

            if (result.IndustryDecision == StockMetadataEnrichmentDecision.WouldApply && result.CandidateIndustryId.HasValue)
            {
                if (!stock.IndustryId.HasValue)
                {
                    stock.IndustryId = result.CandidateIndustryId;
                    result.IndustryDecision = StockMetadataEnrichmentDecision.Applied;
                }
                else if (stock.IndustryId == result.CandidateIndustryId)
                {
                    result.IndustryDecision = StockMetadataEnrichmentDecision.Unchanged;
                }
                else
                {
                    result.IndustryDecision = StockMetadataEnrichmentDecision.Conflict;
                }
            }

            result.AppliedAtUtc = now;
            result.UpdatedAtUtc = now;
            stock.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (true, "Применение завершено.");
    }

    public async Task<(bool Success, string Message)> ReviewAsync(Guid jobId, long resultId, ReviewStockMetadataEnrichmentResultRequest request, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await db.StockMetadataEnrichmentResults.FirstOrDefaultAsync(x => x.JobId == jobId && x.Id == resultId, cancellationToken);
        if (result is null)
        {
            return (false, "Результат не найден.");
        }

        if (!request.Approve)
        {
            result.Rejected = true;
            result.ManuallyApproved = false;
            result.IsinDecision = MarkRejectedIfPending(result.IsinDecision);
            result.WknDecision = MarkRejectedIfPending(result.WknDecision);
            result.IndustryDecision = MarkRejectedIfPending(result.IndustryDecision);
            result.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            return (true, "Результат отклонён.");
        }

        if (request.IndustryId.HasValue)
        {
            var industry = await db.Industries.Include(x => x.Sector).FirstOrDefaultAsync(x => x.Id == request.IndustryId.Value, cancellationToken);
            if (industry is null)
            {
                return (false, "Отрасль не найдена.");
            }

            if (industry.IsArchived || industry.Sector.IsArchived)
            {
                return (false, "Нельзя назначать архивную отрасль.");
            }

            result.CandidateIndustryId = industry.Id;
            result.IndustryDecision = result.OldIndustryId == industry.Id
                ? StockMetadataEnrichmentDecision.Unchanged
                : StockMetadataEnrichmentDecision.WouldApply;

            if (request.SaveMapping && (!string.IsNullOrWhiteSpace(result.RawProviderSector) || !string.IsNullOrWhiteSpace(result.RawProviderIndustry)))
            {
                var normalizedSector = NormalizeText(result.RawProviderSector ?? string.Empty);
                var normalizedIndustry = NormalizeText(result.RawProviderIndustry ?? string.Empty);
                var mapping = await db.StockMetadataIndustryMappings.FirstOrDefaultAsync(x =>
                    x.Provider == "Yahoo Finance" && x.NormalizedSector == normalizedSector && x.NormalizedIndustry == normalizedIndustry,
                    cancellationToken);
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (mapping is null)
                {
                    db.StockMetadataIndustryMappings.Add(new StockMetadataIndustryMapping
                    {
                        Provider = "Yahoo Finance",
                        NormalizedSector = normalizedSector,
                        NormalizedIndustry = normalizedIndustry,
                        IndustryId = industry.Id,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                    });
                }
                else
                {
                    mapping.IndustryId = industry.Id;
                    mapping.UpdatedAtUtc = now;
                }
            }
        }

        result.ManuallyApproved = true;
        result.Rejected = false;
        result.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return (true, "Результат подтверждён.");
    }

    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.StockMetadataEnrichmentJobs.FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null)
        {
            return false;
        }

        if (job.Status is StockMetadataEnrichmentJobStatus.Completed or StockMetadataEnrichmentJobStatus.CompletedWithWarnings or StockMetadataEnrichmentJobStatus.Failed)
        {
            return false;
        }

        job.Status = StockMetadataEnrichmentJobStatus.Cancelled;
        job.CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        job.UpdatedAtUtc = job.CompletedAtUtc.Value;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RetryAsync(Guid jobId, RetryStockMetadataEnrichmentJobRequest request, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.StockMetadataEnrichmentJobs.FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null)
        {
            return false;
        }

        if (job.Status is not (StockMetadataEnrichmentJobStatus.Failed or StockMetadataEnrichmentJobStatus.CompletedWithWarnings or StockMetadataEnrichmentJobStatus.Cancelled))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (request.ResetProgress)
        {
            db.StockMetadataEnrichmentResults.RemoveRange(db.StockMetadataEnrichmentResults.Where(x => x.JobId == jobId));
            job.LastProcessedStockId = 0;
            job.ProcessedStocks = 0;
        }

        job.Status = StockMetadataEnrichmentJobStatus.Queued;
        job.CompletedAtUtc = null;
        job.StartedAtUtc = null;
        job.RetryAfterUtc = now;
        job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryProcessOneJobAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var job = await db.StockMetadataEnrichmentJobs
            .Where(x => (x.Status == StockMetadataEnrichmentJobStatus.Queued || x.Status == StockMetadataEnrichmentJobStatus.Running)
                && (!x.RetryAfterUtc.HasValue || x.RetryAfterUtc <= now))
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        if (job.Status == StockMetadataEnrichmentJobStatus.Queued)
        {
            job.Status = StockMetadataEnrichmentJobStatus.Running;
            job.StartedAtUtc ??= now;
            job.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var processed = await ProcessJobBatchAsync(db, job, cancellationToken);
            if (!processed)
            {
                job.Status = job.FailedStocks > 0 || job.ConflictStocks > 0 || job.ReviewStocks > 0
                    ? StockMetadataEnrichmentJobStatus.CompletedWithWarnings
                    : StockMetadataEnrichmentJobStatus.Completed;
                job.CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
                job.UpdatedAtUtc = job.CompletedAtUtc.Value;
                await db.SaveChangesAsync(cancellationToken);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata enrichment job {JobId} failed.", job.Id);
            job.RetryCount += 1;
            job.DiagnosticSummary = ex.Message;
            job.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            if (job.RetryCount >= _options.MaxRetryCount)
            {
                job.Status = StockMetadataEnrichmentJobStatus.Failed;
                job.CompletedAtUtc = job.UpdatedAtUtc;
                job.FailedStocks += 1;
            }
            else
            {
                job.Status = StockMetadataEnrichmentJobStatus.Queued;
                job.RetryAfterUtc = job.UpdatedAtUtc.Add(_options.RetryCooldown);
            }

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }

    private async Task<bool> ProcessJobBatchAsync(AppDbContext db, StockMetadataEnrichmentJob job, CancellationToken cancellationToken)
    {
        var selectedIds = ParseSelectedIds(job.SelectedStockIdsJson);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var staleAfter = job.MetadataStaleAfterUtc ?? now.Subtract(_options.StaleInterval);

        var stocksQuery = db.Stocks.AsQueryable();
        if (job.Scope == StockMetadataEnrichmentScope.Selected)
        {
            stocksQuery = stocksQuery.Where(x => selectedIds.Contains(x.Id));
        }
        else if (job.Scope == StockMetadataEnrichmentScope.MissingOnly)
        {
            stocksQuery = stocksQuery.Where(x => string.IsNullOrWhiteSpace(x.Isin) || string.IsNullOrWhiteSpace(x.Wkn) || !x.IndustryId.HasValue);
        }
        else if (job.Scope == StockMetadataEnrichmentScope.RefreshStale)
        {
            stocksQuery = stocksQuery.Where(x => x.UpdatedAt <= staleAfter);
        }

        stocksQuery = stocksQuery.Where(x => x.Id > job.LastProcessedStockId);

        var batch = await stocksQuery
            .OrderBy(x => x.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (job.TotalStocks == 0)
        {
            var totalQuery = db.Stocks.AsQueryable();
            if (job.Scope == StockMetadataEnrichmentScope.Selected)
            {
                totalQuery = totalQuery.Where(x => selectedIds.Contains(x.Id));
            }
            else if (job.Scope == StockMetadataEnrichmentScope.MissingOnly)
            {
                totalQuery = totalQuery.Where(x => string.IsNullOrWhiteSpace(x.Isin) || string.IsNullOrWhiteSpace(x.Wkn) || !x.IndustryId.HasValue);
            }
            else if (job.Scope == StockMetadataEnrichmentScope.RefreshStale)
            {
                totalQuery = totalQuery.Where(x => x.UpdatedAt <= staleAfter);
            }
            job.TotalStocks = await totalQuery.CountAsync(cancellationToken);
        }

        if (batch.Count == 0)
        {
            return false;
        }

        var resultsByStockId = await db.StockMetadataEnrichmentResults
            .Where(x => x.JobId == job.Id && batch.Select(s => s.Id).Contains(x.StockId))
            .ToDictionaryAsync(x => x.StockId, cancellationToken);

        foreach (var stock in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = resultsByStockId.TryGetValue(stock.Id, out var existing)
                ? existing
                : new StockMetadataEnrichmentResult
                {
                    JobId = job.Id,
                    StockId = stock.Id,
                    CreatedAtUtc = now,
                };

            result.ProviderSymbol = stock.ProviderSymbol;
            result.Exchange = stock.Exchange;
            result.OldIsin = StockIdentifiers.Normalize(stock.Isin);
            result.OldWkn = StockIdentifiers.Normalize(stock.Wkn);
            result.OldIndustryId = stock.IndustryId;
            result.UpdatedAtUtc = now;

            EnrichIdentifiers(stock, result);
            await EnrichIndustryAsync(db, stock, result, cancellationToken);

            if (result.Id == 0)
            {
                db.StockMetadataEnrichmentResults.Add(result);
            }

            UpdateCounters(job, result);
            job.ProcessedStocks += 1;
            job.LastProcessedStockId = stock.Id;
        }

        job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void EnrichIdentifiers(Stock stock, StockMetadataEnrichmentResult result)
    {
        var providerSymbol = stock.ProviderSymbol;
        if (string.IsNullOrWhiteSpace(providerSymbol) && !string.IsNullOrWhiteSpace(stock.Ticker) && !string.IsNullOrWhiteSpace(stock.Exchange))
        {
            providerSymbol = StockExchanges.ResolveProviderSymbol(stock.Ticker, stock.Exchange);
        }

        CuratedIdentifierCandidate? curated = null;
        if (!string.IsNullOrWhiteSpace(providerSymbol) && !string.IsNullOrWhiteSpace(stock.Exchange))
        {
            curated = _curatedSnapshotService.FindByListingIdentity(providerSymbol!, stock.Exchange);
        }

        result.CandidateIsin = StockIdentifiers.Normalize(curated?.Isin);
        result.IsinSource = curated?.Source;
        result.IsinConfidence = curated?.Confidence ?? StockMetadataEnrichmentConfidence.None;
        result.IsinDecision = DetermineIdentifierDecision(result.OldIsin, result.CandidateIsin, StockIdentifiers.IsValidIsin);

        result.CandidateWkn = StockIdentifiers.Normalize(curated?.Wkn);
        result.WknSource = curated?.Source;
        result.WknConfidence = curated?.Confidence ?? StockMetadataEnrichmentConfidence.None;
        result.WknDecision = DetermineIdentifierDecision(result.OldWkn, result.CandidateWkn, StockIdentifiers.IsValidWkn);
    }

    private async Task EnrichIndustryAsync(AppDbContext db, Stock stock, StockMetadataEnrichmentResult result, CancellationToken cancellationToken)
    {
        var providerSymbol = stock.ProviderSymbol;
        if (string.IsNullOrWhiteSpace(providerSymbol) && !string.IsNullOrWhiteSpace(stock.Ticker) && !string.IsNullOrWhiteSpace(stock.Exchange))
        {
            providerSymbol = StockExchanges.ResolveProviderSymbol(stock.Ticker, stock.Exchange);
        }

        if (string.IsNullOrWhiteSpace(providerSymbol))
        {
            result.IndustryDecision = StockMetadataEnrichmentDecision.NotFound;
            result.IndustryConfidence = StockMetadataEnrichmentConfidence.None;
            return;
        }

        var profile = await _yahooAssetProfileService.GetAssetProfileAsync(providerSymbol!, cancellationToken);
        result.RawProviderSector = profile.SectorKey ?? profile.Sector;
        result.RawProviderIndustry = profile.IndustryKey ?? profile.Industry;
        result.IndustrySource = profile.Source;
        result.IndustryConfidence = profile.Confidence;

        if (profile.RateLimited)
        {
            result.IndustryDecision = StockMetadataEnrichmentDecision.RateLimited;
            return;
        }

        if (profile.Failed)
        {
            result.IndustryDecision = StockMetadataEnrichmentDecision.Failed;
            result.Diagnostics = AppendDiagnostics(result.Diagnostics, profile.Diagnostics);
            return;
        }

        var normalizedSector = NormalizeText(result.RawProviderSector);
        var normalizedIndustry = NormalizeText(result.RawProviderIndustry);

        var mapping = await db.StockMetadataIndustryMappings
            .Include(x => x.Industry)
            .ThenInclude(x => x.Sector)
            .FirstOrDefaultAsync(x =>
                x.Provider == profile.Source
                && x.NormalizedSector == normalizedSector
                && x.NormalizedIndustry == normalizedIndustry,
                cancellationToken);

        if (mapping is null)
        {
            result.IndustryDecision = string.IsNullOrWhiteSpace(normalizedIndustry)
                ? StockMetadataEnrichmentDecision.NotFound
                : StockMetadataEnrichmentDecision.NeedsReview;
            return;
        }

        if (mapping.Industry.IsArchived || mapping.Industry.Sector.IsArchived)
        {
            result.IndustryDecision = StockMetadataEnrichmentDecision.NeedsReview;
            result.Diagnostics = AppendDiagnostics(result.Diagnostics, "Mapping points to archived industry/sector.");
            return;
        }

        result.CandidateIndustryId = mapping.IndustryId;
        if (!result.OldIndustryId.HasValue)
        {
            result.IndustryDecision = StockMetadataEnrichmentDecision.WouldApply;
        }
        else if (result.OldIndustryId == mapping.IndustryId)
        {
            result.IndustryDecision = StockMetadataEnrichmentDecision.Unchanged;
        }
        else
        {
            result.IndustryDecision = StockMetadataEnrichmentDecision.Conflict;
        }
    }

    private static StockMetadataEnrichmentDecision DetermineIdentifierDecision(
        string? oldValue,
        string? candidate,
        Func<string, bool> validator)
    {
        if (candidate is null)
        {
            return oldValue is null ? StockMetadataEnrichmentDecision.NotFound : StockMetadataEnrichmentDecision.Unchanged;
        }

        if (!validator(candidate))
        {
            return StockMetadataEnrichmentDecision.Invalid;
        }

        if (oldValue is null)
        {
            return StockMetadataEnrichmentDecision.WouldApply;
        }

        return string.Equals(oldValue, candidate, StringComparison.Ordinal)
            ? StockMetadataEnrichmentDecision.Unchanged
            : StockMetadataEnrichmentDecision.Conflict;
    }

    private static HashSet<int> ParseSelectedIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<int>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? [];
            return list.ToHashSet();
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? AppendDiagnostics(string? source, string? append)
    {
        if (string.IsNullOrWhiteSpace(append))
        {
            return source;
        }

        var current = string.IsNullOrWhiteSpace(source) ? string.Empty : source + " | ";
        var combined = current + append.Trim();
        return combined.Length <= 1000 ? combined : combined[..1000];
    }

    private static StockMetadataEnrichmentDecision ForceTerminal(StockMetadataEnrichmentDecision current, StockMetadataEnrichmentDecision replacement)
        => current == StockMetadataEnrichmentDecision.Applied ? current : replacement;

    private static StockMetadataEnrichmentDecision MarkConflictIfPending(StockMetadataEnrichmentDecision current)
        => current is StockMetadataEnrichmentDecision.WouldApply or StockMetadataEnrichmentDecision.NeedsReview
            ? StockMetadataEnrichmentDecision.Conflict
            : current;

    private static StockMetadataEnrichmentDecision MarkRejectedIfPending(StockMetadataEnrichmentDecision current)
        => current is StockMetadataEnrichmentDecision.WouldApply or StockMetadataEnrichmentDecision.NeedsReview
            ? StockMetadataEnrichmentDecision.Rejected
            : current;

    private static void UpdateCounters(StockMetadataEnrichmentJob job, StockMetadataEnrichmentResult result)
    {
        var decisions = new[] { result.IsinDecision, result.WknDecision, result.IndustryDecision };
        if (decisions.Any(x => x == StockMetadataEnrichmentDecision.Failed))
        {
            job.FailedStocks += 1;
        }
        if (decisions.Any(x => x == StockMetadataEnrichmentDecision.RateLimited))
        {
            job.RateLimitedStocks += 1;
        }
        if (decisions.Any(x => x == StockMetadataEnrichmentDecision.Conflict))
        {
            job.ConflictStocks += 1;
        }
        if (decisions.Any(x => x == StockMetadataEnrichmentDecision.NotFound))
        {
            job.NotFoundStocks += 1;
        }
        if (decisions.Any(x => x == StockMetadataEnrichmentDecision.NeedsReview))
        {
            job.ReviewStocks += 1;
        }
        if (decisions.All(x => x is StockMetadataEnrichmentDecision.Unchanged or StockMetadataEnrichmentDecision.Applied or StockMetadataEnrichmentDecision.WouldApply))
        {
            job.SucceededStocks += 1;
        }
        else
        {
            job.PartialStocks += 1;
        }
    }

    private static StockMetadataEnrichmentJobResponse MapJob(StockMetadataEnrichmentJob x) => new()
    {
        JobId = x.Id,
        Scope = x.Scope,
        IsDryRun = x.IsDryRun,
        Status = x.Status,
        CreatedAtUtc = x.CreatedAtUtc,
        StartedAtUtc = x.StartedAtUtc,
        CompletedAtUtc = x.CompletedAtUtc,
        TotalStocks = x.TotalStocks,
        ProcessedStocks = x.ProcessedStocks,
        SucceededStocks = x.SucceededStocks,
        PartialStocks = x.PartialStocks,
        ReviewStocks = x.ReviewStocks,
        ConflictStocks = x.ConflictStocks,
        NotFoundStocks = x.NotFoundStocks,
        RateLimitedStocks = x.RateLimitedStocks,
        FailedStocks = x.FailedStocks,
        DiagnosticSummary = x.DiagnosticSummary,
    };

    private static StockMetadataEnrichmentResultResponse MapResult(StockMetadataEnrichmentResult x) => new()
    {
        Id = x.Id,
        StockId = x.StockId,
        ProviderSymbol = x.ProviderSymbol,
        Exchange = x.Exchange,
        OldIsin = x.OldIsin,
        CandidateIsin = x.CandidateIsin,
        OldWkn = x.OldWkn,
        CandidateWkn = x.CandidateWkn,
        OldIndustryId = x.OldIndustryId,
        CandidateIndustryId = x.CandidateIndustryId,
        RawProviderSector = x.RawProviderSector,
        RawProviderIndustry = x.RawProviderIndustry,
        IsinSource = x.IsinSource,
        WknSource = x.WknSource,
        IndustrySource = x.IndustrySource,
        IsinConfidence = x.IsinConfidence,
        WknConfidence = x.WknConfidence,
        IndustryConfidence = x.IndustryConfidence,
        IsinDecision = x.IsinDecision,
        WknDecision = x.WknDecision,
        IndustryDecision = x.IndustryDecision,
        Diagnostics = x.Diagnostics,
        ManuallyApproved = x.ManuallyApproved,
        Rejected = x.Rejected,
        CreatedAtUtc = x.CreatedAtUtc,
        UpdatedAtUtc = x.UpdatedAtUtc,
        AppliedAtUtc = x.AppliedAtUtc,
    };

    private async Task EnsureScheduledSweepJobAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasActiveGlobal = await db.StockMetadataEnrichmentJobs.AnyAsync(x =>
            x.Scope != StockMetadataEnrichmentScope.Selected
            && (x.Status == StockMetadataEnrichmentJobStatus.Queued || x.Status == StockMetadataEnrichmentJobStatus.Running), cancellationToken);
        if (hasActiveGlobal)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        db.StockMetadataEnrichmentJobs.Add(new StockMetadataEnrichmentJob
        {
            Id = Guid.NewGuid(),
            Scope = StockMetadataEnrichmentScope.MissingOnly,
            IsDryRun = false,
            Status = StockMetadataEnrichmentJobStatus.Queued,
            MetadataStaleAfterUtc = now.Subtract(_options.StaleInterval),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            InitiatedByUserId = "system",
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
