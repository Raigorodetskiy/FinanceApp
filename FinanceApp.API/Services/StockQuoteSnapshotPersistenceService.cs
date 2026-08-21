using System.Collections.Concurrent;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public sealed class PersistStockQuoteSnapshotRequest
{
    public decimal CurrentPrice { get; init; }
    public decimal? CurrentPriceChange { get; init; }
    public decimal? CurrentPriceChangePercent { get; init; }
    public DateTime? CurrentPriceAt { get; init; }
    public bool CurrentPriceIsDelayed { get; init; }
    public string? CurrentPriceDelayWarning { get; init; }
    public string? QuoteCurrency { get; init; } = "EUR";
    public string? FinancialCurrency { get; init; } = "EUR";
    public string? NormalizedQuoteCurrency { get; init; } = "EUR";
    public decimal QuoteUnitMultiplier { get; init; } = 1m;
}

public sealed class PersistStockQuoteSnapshotResult
{
    public bool StockFound { get; init; }
    public bool Applied { get; init; }
    public string? Reason { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal? CurrentPriceChange { get; init; }
    public decimal? CurrentPriceChangePercent { get; init; }
    public DateTime? CurrentPriceAt { get; init; }
    public bool CurrentPriceIsDelayed { get; init; }
    public string? CurrentPriceDelayWarning { get; init; }
}

public sealed class StockQuoteSnapshotPersistenceService
{
    internal const int DelayWarningMaxLength = 300;

    // The application refreshes a finite stock catalog in a single process; keeping one
    // per-stock gate for the process lifetime prevents concurrent refresh paths from
    // interleaving stale/newer writes for the same row without adding cross-service
    // plumbing. The dictionary therefore grows only with distinct stock ids seen by the
    // process, which is an acceptable bounded trade-off for this deployment model.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> StockLocks = new();

    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StockQuoteSnapshotPersistenceService> _logger;

    public StockQuoteSnapshotPersistenceService(
        AppDbContext context,
        TimeProvider timeProvider,
        ILogger<StockQuoteSnapshotPersistenceService> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PersistStockQuoteSnapshotResult> ApplyAsync(
        int stockId,
        PersistStockQuoteSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = StockLocks.GetOrAdd(stockId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            var stock = await _context.Stocks.FirstOrDefaultAsync(x => x.Id == stockId, cancellationToken);
            if (stock is null)
            {
                return new PersistStockQuoteSnapshotResult
                {
                    StockFound = false,
                    Applied = false,
                    Reason = "Stock record not found."
                };
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var incomingSnapshot = NormalizeIncomingSnapshot(request);
            var decision = Decide(stock, incomingSnapshot, nowUtc);

            if (!decision.ShouldApply)
            {
                _logger.LogDebug(
                    "Rejected stock quote snapshot for stockId={StockId}. Reason={Reason}",
                    stockId,
                    decision.Reason);

                return BuildResult(stock, applied: false, decision.Reason);
            }

            stock.CurrentPrice = incomingSnapshot.CurrentPrice;
            stock.CurrentPriceChange = incomingSnapshot.CurrentPriceChange;
            stock.CurrentPriceChangePercent = incomingSnapshot.CurrentPriceChangePercent;
            stock.CurrentPriceAt = incomingSnapshot.CurrentPriceAt;
            stock.CurrentPriceIsDelayed = incomingSnapshot.CurrentPriceIsDelayed;
            stock.CurrentPriceDelayWarning = incomingSnapshot.CurrentPriceDelayWarning;
            stock.UpdatedAt = nowUtc;
            await UpsertIntradayQuoteObservationAsync(stock.Id, incomingSnapshot, request, nowUtc, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            return BuildResult(stock, applied: true, reason: null);
        }
        finally
        {
            gate.Release();
        }
    }

    private static PersistStockQuoteSnapshotResult BuildResult(Stock stock, bool applied, string? reason)
        => new()
        {
            StockFound = true,
            Applied = applied,
            Reason = reason,
            CurrentPrice = stock.CurrentPrice,
            CurrentPriceChange = stock.CurrentPriceChange,
            CurrentPriceChangePercent = stock.CurrentPriceChangePercent,
            CurrentPriceAt = stock.CurrentPriceAt,
            CurrentPriceIsDelayed = stock.CurrentPriceIsDelayed,
            CurrentPriceDelayWarning = stock.CurrentPriceDelayWarning,
        };

    private static NormalizedSnapshot NormalizeIncomingSnapshot(PersistStockQuoteSnapshotRequest request)
    {
        var warning = NormalizeWarning(request.CurrentPriceDelayWarning);
        var isDelayed = request.CurrentPriceIsDelayed || warning is not null;

        return new NormalizedSnapshot(
            request.CurrentPrice,
            request.CurrentPriceChange,
            request.CurrentPriceChangePercent,
            NormalizeTimestamp(request.CurrentPriceAt),
            isDelayed,
            isDelayed ? warning : null);
    }

    private static Decision Decide(Stock stock, NormalizedSnapshot incomingSnapshot, DateTime nowUtc)
    {
        var storedSnapshot = new NormalizedSnapshot(
            stock.CurrentPrice,
            stock.CurrentPriceChange,
            stock.CurrentPriceChangePercent,
            NormalizeTimestamp(stock.CurrentPriceAt),
            stock.CurrentPriceIsDelayed,
            NormalizeWarning(stock.CurrentPriceDelayWarning));

        var incomingTimestampIsValid = IsValidSnapshotTimestamp(incomingSnapshot.CurrentPriceAt, nowUtc);
        var storedTimestampIsValid = IsValidSnapshotTimestamp(storedSnapshot.CurrentPriceAt, nowUtc);

        if (incomingTimestampIsValid && storedTimestampIsValid)
        {
            if (incomingSnapshot.CurrentPriceAt!.Value > storedSnapshot.CurrentPriceAt!.Value)
            {
                return Decision.Apply();
            }

            if (incomingSnapshot.CurrentPriceAt.Value < storedSnapshot.CurrentPriceAt.Value)
            {
                return Decision.Skip("Quote timestamp is older than the stored snapshot.");
            }

            if (incomingSnapshot.CurrentPriceIsDelayed != storedSnapshot.CurrentPriceIsDelayed)
            {
                return incomingSnapshot.CurrentPriceIsDelayed
                    ? Decision.Skip("Equal timestamp keeps the non-delayed snapshot.")
                    : Decision.Apply();
            }

            return SnapshotsEquivalent(storedSnapshot, incomingSnapshot)
                ? Decision.Skip("Equivalent snapshot already persisted.")
                : Decision.Skip("Equal timestamp keeps the existing snapshot.");
        }

        if (incomingTimestampIsValid)
        {
            return Decision.Apply();
        }

        if (storedTimestampIsValid)
        {
            return Decision.Skip("Incoming quote timestamp is missing, invalid, or in the future.");
        }

        if (SnapshotsEquivalent(storedSnapshot, incomingSnapshot))
        {
            return Decision.Skip("Equivalent untimestamped snapshot already persisted.");
        }

        return HasStoredProviderSnapshot(stock)
            ? Decision.Skip("Incoming quote timestamp is missing, invalid, or in the future.")
            : Decision.Apply();
    }

    private static bool HasStoredProviderSnapshot(Stock stock)
        => stock.CurrentPriceAt.HasValue
           || stock.CurrentPriceChange.HasValue
           || stock.CurrentPriceChangePercent.HasValue
           || stock.CurrentPriceIsDelayed
           || !string.IsNullOrWhiteSpace(stock.CurrentPriceDelayWarning);

    private static bool SnapshotsEquivalent(NormalizedSnapshot left, NormalizedSnapshot right)
        => left.CurrentPrice == right.CurrentPrice
           && left.CurrentPriceChange == right.CurrentPriceChange
           && left.CurrentPriceChangePercent == right.CurrentPriceChangePercent
           && left.CurrentPriceAt == right.CurrentPriceAt
           && left.CurrentPriceIsDelayed == right.CurrentPriceIsDelayed
           && string.Equals(left.CurrentPriceDelayWarning, right.CurrentPriceDelayWarning, StringComparison.Ordinal);

    private static bool IsValidSnapshotTimestamp(DateTime? timestampUtc, DateTime nowUtc)
        => timestampUtc.HasValue && timestampUtc.Value <= nowUtc;

    private static DateTime? NormalizeTimestamp(DateTime? timestampUtc)
    {
        if (!timestampUtc.HasValue)
        {
            return null;
        }

        var value = timestampUtc.Value;
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string? NormalizeWarning(string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return null;
        }

        var trimmed = warning.Trim();
        return trimmed.Length <= DelayWarningMaxLength
            ? trimmed
            : trimmed[..DelayWarningMaxLength];
    }

    private async Task UpsertIntradayQuoteObservationAsync(
        int stockId,
        NormalizedSnapshot incomingSnapshot,
        PersistStockQuoteSnapshotRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!IsValidSnapshotTimestamp(incomingSnapshot.CurrentPriceAt, nowUtc))
        {
            return;
        }

        var providerTimestampUtc = incomingSnapshot.CurrentPriceAt!.Value;
        var bucketTimestampUtc = FloorToTenMinute(providerTimestampUtc);

        var latestTimestampUtc = await _context.StockHistoricalPrices
            .Where(x => x.StockId == stockId && x.Interval == "10m")
            .OrderByDescending(x => x.Timestamp)
            .Select(x => (DateTime?)x.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestTimestampUtc.HasValue && bucketTimestampUtc < latestTimestampUtc.Value)
        {
            return;
        }

        var rowAtBucket = await _context.StockHistoricalPrices
            .FirstOrDefaultAsync(
                x => x.StockId == stockId && x.Interval == "10m" && x.Timestamp == bucketTimestampUtc,
                cancellationToken);

        if (rowAtBucket is not null)
        {
            if (!rowAtBucket.IsQuoteDerived)
            {
                return;
            }

            rowAtBucket.Open = rowAtBucket.Open > 0m ? rowAtBucket.Open : incomingSnapshot.CurrentPrice;
            rowAtBucket.High = rowAtBucket.High > 0m
                ? Math.Max(rowAtBucket.High, incomingSnapshot.CurrentPrice)
                : incomingSnapshot.CurrentPrice;
            rowAtBucket.Low = rowAtBucket.Low > 0m
                ? Math.Min(rowAtBucket.Low, incomingSnapshot.CurrentPrice)
                : incomingSnapshot.CurrentPrice;
            rowAtBucket.Close = incomingSnapshot.CurrentPrice;
            rowAtBucket.AdjustedClose = null;
            rowAtBucket.QuoteCurrency = NormalizeCurrencyOrFallback(request.QuoteCurrency, "EUR");
            rowAtBucket.FinancialCurrency = NormalizeCurrencyOrFallback(request.FinancialCurrency, rowAtBucket.QuoteCurrency);
            rowAtBucket.NormalizedQuoteCurrency = NormalizeCurrencyOrFallback(request.NormalizedQuoteCurrency, rowAtBucket.QuoteCurrency);
            rowAtBucket.QuoteUnitMultiplier = request.QuoteUnitMultiplier > 0m ? request.QuoteUnitMultiplier : 1m;
            rowAtBucket.Volume = 0;
            rowAtBucket.IsQuoteDerived = true;
            return;
        }

        _context.StockHistoricalPrices.Add(new StockHistoricalPrice
        {
            StockId = stockId,
            Timestamp = bucketTimestampUtc,
            Interval = "10m",
            Open = incomingSnapshot.CurrentPrice,
            High = incomingSnapshot.CurrentPrice,
            Low = incomingSnapshot.CurrentPrice,
            Close = incomingSnapshot.CurrentPrice,
            AdjustedClose = null,
            QuoteCurrency = NormalizeCurrencyOrFallback(request.QuoteCurrency, "EUR"),
            FinancialCurrency = NormalizeCurrencyOrFallback(request.FinancialCurrency, NormalizeCurrencyOrFallback(request.QuoteCurrency, "EUR")),
            NormalizedQuoteCurrency = NormalizeCurrencyOrFallback(request.NormalizedQuoteCurrency, NormalizeCurrencyOrFallback(request.QuoteCurrency, "EUR")),
            QuoteUnitMultiplier = request.QuoteUnitMultiplier > 0m ? request.QuoteUnitMultiplier : 1m,
            Volume = 0,
            IsQuoteDerived = true
        });
    }

    private static DateTime FloorToTenMinute(DateTime valueUtc)
    {
        var utc = valueUtc.Kind == DateTimeKind.Utc ? valueUtc : valueUtc.ToUniversalTime();
        var minutes = (utc.Minute / 10) * 10;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, minutes, 0, DateTimeKind.Utc);
    }

    private static string NormalizeCurrencyOrFallback(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private readonly record struct NormalizedSnapshot(
        decimal CurrentPrice,
        decimal? CurrentPriceChange,
        decimal? CurrentPriceChangePercent,
        DateTime? CurrentPriceAt,
        bool CurrentPriceIsDelayed,
        string? CurrentPriceDelayWarning);

    private readonly record struct Decision(bool ShouldApply, string? Reason)
    {
        public static Decision Apply() => new(true, null);
        public static Decision Skip(string reason) => new(false, reason);
    }
}
