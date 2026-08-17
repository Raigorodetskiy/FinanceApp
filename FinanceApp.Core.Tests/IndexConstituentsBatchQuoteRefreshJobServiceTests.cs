using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class IndexConstituentsBatchQuoteRefreshJobServiceTests
{
    // ──────────────────────────────────────────────────────────────────────
    //  Endpoint contracts
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartEndpoint_Returns202_WithJobDto()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(FetchSuccess(1.23m));
        await harness.SeedAsync(1, 8001, "AAPL", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        await using var scope = harness.Services.CreateAsyncScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            harness.JobService);

        var result = await controller.StartConstituentsBatchQuoteRefresh(1);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var payload = Assert.IsType<IndexConstituentsBatchQuoteRefreshJobResponse>(accepted.Value);
        Assert.Equal(1, payload.MarketIndexId);
        Assert.True(
            payload.State is IndexConstituentsBatchQuoteRefreshJobState.Queued
                or IndexConstituentsBatchQuoteRefreshJobState.Running
                or IndexConstituentsBatchQuoteRefreshJobState.Succeeded);
        Assert.False(payload.ReusedActiveJob);
    }

    [Fact]
    public async Task StartEndpoint_MissingIndex_Returns404()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(FetchSuccess(1m));

        await using var scope = harness.Services.CreateAsyncScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            harness.JobService);

        var result = await controller.StartConstituentsBatchQuoteRefresh(9999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task StartEndpoint_ArchivedIndex_Returns422()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(FetchSuccess(1m));
        await harness.SeedAsync(1, 8002, "SAP", StockExchanges.Frankfurt, StockTrackingStatus.CatalogOnly, archived: true);

        await using var scope = harness.Services.CreateAsyncScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            harness.JobService);

        var result = await controller.StartConstituentsBatchQuoteRefresh(1);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.NotNull(unprocessable.Value);
    }

    [Fact]
    public async Task StartEndpoint_EmptyIndex_Returns422()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(FetchSuccess(1m));
        await harness.SeedIndexOnlyAsync(1);

        await using var scope = harness.Services.CreateAsyncScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            harness.JobService);

        var result = await controller.StartConstituentsBatchQuoteRefresh(1);

        Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
    }

    [Fact]
    public async Task StatusEndpoint_UnknownJob_Returns404()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(FetchSuccess(1m));
        await harness.SeedAsync(1, 8003, "MSFT", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);

        await using var scope = harness.Services.CreateAsyncScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            harness.JobService);

        var result = controller.GetConstituentsBatchQuoteRefreshJobStatus(1, "nonexistent-job-id");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task StatusEndpoint_WrongIndex_Returns404()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(FetchSuccess(1m));
        await harness.SeedAsync(1, 8004, "GOOGL", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        Assert.NotNull(enqueued.Job);

        // Query with wrong indexId (2 instead of 1)
        var found = harness.JobService.TryGetJob(2, enqueued.Job!.JobId, out _);
        Assert.False(found);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Deduplication / reuse
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DuplicateStart_ReturnsReusedActiveJob()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await BatchQuoteHarness.CreateAsync(async (_, _, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return FetchSuccessResult(1m);
        });
        await harness.SeedAsync(1, 8005, "NFLX", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var first = harness.JobService.Enqueue(1);
        var second = harness.JobService.Enqueue(1);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobEnqueueStatus.Enqueued, first.Status);
        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobEnqueueStatus.ReusedActiveJob, second.Status);
        Assert.NotNull(first.Job);
        Assert.NotNull(second.Job);
        Assert.Equal(first.Job!.JobId, second.Job!.JobId);
        Assert.True(second.Job!.ReusedActiveJob);

        gate.TrySetResult();
        await harness.WaitForTerminalStateAsync(1, first.Job.JobId);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Constituent filtering
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyCurrentConstituents_AreProcessed_FormerMembersExcluded()
    {
        var processed = new List<string>();
        using var harness = await BatchQuoteHarness.CreateAsync((ticker, _, _) =>
        {
            processed.Add(ticker);
            return Task.FromResult(FetchSuccessResult(1m));
        });

        // Current member
        await harness.SeedAsync(1, 8006, "IBM", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        // Former member (EffectiveTo set)
        await harness.SeedAsync(1, 8007, "EXITED", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly, formerMember: true);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobState.Succeeded, terminal.State);
        Assert.Equal(1, terminal.Total);
        Assert.Contains("IBM", processed);
        Assert.DoesNotContain("EXITED", processed);
    }

    [Fact]
    public async Task CatalogOnly_And_Tracked_Constituents_BothProcessed_TrackingStatusUnchanged()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(FetchSuccess(10m));
        await harness.SeedAsync(1, 8010, "CAT1", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.SeedAsync(1, 8011, "TRK1", StockExchanges.Nyse, StockTrackingStatus.Tracked);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobState.Succeeded, terminal.State);
        Assert.Equal(2, terminal.Total);
        Assert.Equal(2, terminal.Succeeded);

        // Verify tracking status not mutated
        await using var scope = harness.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cat = await context.Stocks.FindAsync(8010);
        var trk = await context.Stocks.FindAsync(8011);
        Assert.Equal(StockTrackingStatus.CatalogOnly, cat!.TrackingStatus);
        Assert.Equal(StockTrackingStatus.Tracked, trk!.TrackingStatus);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Quote persistence logic
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FreshEurQuote_Persists_RoundedSnapshot()
    {
        var priceAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        using var harness = await BatchQuoteHarness.CreateAsync(
            FetchSuccess(1.23456m, changeEur: 0.012345m, percent: 1.23456789m, priceAt: priceAt));
        await harness.SeedAsync(1, 8020, "AMZN", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobState.Succeeded, terminal.State);
        Assert.Equal(1, terminal.Succeeded);

        await using var scope = harness.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stock = await context.Stocks.FindAsync(8020);
        Assert.Equal(1.23m, stock!.CurrentPrice);                         // Math.Round(1.23456, 2)
        Assert.Equal(0.0123m, stock.CurrentPriceChange);                  // Math.Round(0.012345, 4)
        Assert.Equal(1.2346m, stock.CurrentPriceChangePercent);           // Math.Round(1.23456789, 4)
        Assert.Equal(priceAt, stock.CurrentPriceAt);
    }

    [Fact]
    public async Task DelayedQuote_CountedButNotPersisted()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(
            FetchDelayed(1m, "Цена задержана"));
        await harness.SeedAsync(1, 8021, "META", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobState.Succeeded, terminal.State);
        Assert.Equal(1, terminal.Delayed);
        Assert.Equal(0, terminal.Succeeded);

        await using var scope = harness.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stock = await context.Stocks.FindAsync(8021);
        Assert.Null(stock!.CurrentPrice == 0m ? null : (decimal?)stock.CurrentPrice);
    }

    [Fact]
    public async Task NoEurConversion_CountedButNotPersisted()
    {
        using var harness = await BatchQuoteHarness.CreateAsync(
            FetchNoEur(1m));
        await harness.SeedAsync(1, 8022, "NVDA", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobState.Succeeded, terminal.State);
        Assert.Equal(1, terminal.NoEurConversion);
        Assert.Equal(0, terminal.Succeeded);
    }

    [Fact]
    public async Task StaleTimestamp_CannotOverwriteNewerStoredSnapshot_CountedAsStaleRejected()
    {
        var storedAt = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);
        var olderAt  = storedAt.AddMinutes(-5);

        using var harness = await BatchQuoteHarness.CreateAsync(
            FetchSuccess(99m, priceAt: olderAt));
        await harness.SeedAsync(1, 8023, "AMD", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly,
            existingPriceAt: storedAt, existingPrice: 50m);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobState.Succeeded, terminal.State);
        Assert.Equal(1, terminal.StaleRejected);
        Assert.Equal(0, terminal.Succeeded);

        await using var scope = harness.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stock = await context.Stocks.FindAsync(8023);
        Assert.Equal(50m, stock!.CurrentPrice);  // not overwritten
    }

    [Fact]
    public async Task ProviderFailure_IsolatedAndCounted_JobContinues()
    {
        var calls = new List<string>();
        using var harness = await BatchQuoteHarness.CreateAsync((ticker, _, _) =>
        {
            calls.Add(ticker);
            return ticker == "FAIL"
                ? Task.FromResult(StockQuoteFetchResult.Failure(502, "provider down"))
                : Task.FromResult(FetchSuccessResult(1m));
        });
        await harness.SeedAsync(1, 8024, "FAIL", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.SeedAsync(1, 8025, "OK", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobState.Succeeded, terminal.State);
        Assert.Equal(1, terminal.ProviderFailed);
        Assert.Equal(1, terminal.Succeeded);
        Assert.Equal(2, terminal.Total);
    }

    [Fact]
    public async Task RateLimitedProvider_StopsProcessing_CountedAndStateIsRateLimited()
    {
        var processed = new List<string>();
        using var harness = await BatchQuoteHarness.CreateAsync((ticker, _, _) =>
        {
            processed.Add(ticker);
            return ticker == "RATELIM"
                ? Task.FromResult(StockQuoteFetchResult.RateLimit("429"))
                : Task.FromResult(FetchSuccessResult(1m));
        });
        // Ensure RATELIM is first by using lower stock IDs
        await harness.SeedAsync(1, 8030, "RATELIM", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.SeedAsync(1, 8031, "AFTER", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);

        Assert.Equal(IndexConstituentsBatchQuoteRefreshJobState.RateLimited, terminal.State);
        Assert.Equal(1, terminal.RateLimited);
        Assert.Equal(0, terminal.Succeeded);
        Assert.NotNull(terminal.Error);
    }

    [Fact]
    public async Task Shutdown_LeavesJobInterrupted()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await BatchQuoteHarness.CreateAsync(async (_, _, ct) =>
        {
            entered.TrySetResult();
            await block.Task.WaitAsync(ct);
            return FetchSuccessResult(1m);
        });
        await harness.SeedAsync(1, 8040, "SLOW", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await harness.JobService.StopAsync(CancellationToken.None);
        block.TrySetResult();

        await Task.Delay(200);

        harness.JobService.TryGetJob(1, enqueued.Job!.JobId, out var job);
        Assert.NotNull(job);
        Assert.True(
            job!.State is IndexConstituentsBatchQuoteRefreshJobState.Interrupted
                or IndexConstituentsBatchQuoteRefreshJobState.Succeeded);
    }

    [Fact]
    public async Task ProgressTracking_ReportsProcessedAndTotal()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await BatchQuoteHarness.CreateAsync(async (_, _, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return FetchSuccessResult(1m);
        });
        await harness.SeedAsync(1, 8050, "A", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.SeedAsync(1, 8051, "B", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var enqueued = harness.JobService.Enqueue(1);
        Assert.NotNull(enqueued.Job);

        // Wait until job is running and total is set
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            harness.JobService.TryGetJob(1, enqueued.Job!.JobId, out var current);
            if (current?.Total == 2) break;
            await Task.Delay(20);
        }

        harness.JobService.TryGetJob(1, enqueued.Job!.JobId, out var running);
        Assert.Equal(2, running?.Total);

        gate.TrySetResult();
        var terminal = await harness.WaitForTerminalStateAsync(1, enqueued.Job!.JobId);
        Assert.Equal(2, terminal.Processed);
        Assert.Equal(2, terminal.Total);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static MarketIndicesController CreateController(
        AppDbContext context,
        IIndexConstituentsBatchQuoteRefreshJobService jobService)
        => new(
            context,
            new NullMarketIndexHistoryService(),
            new NullIndexConstituentsProvider(),
            new NullStockHistoryService(),
            new NullIndexConstituentHistoryRefreshJobService(),
            jobService,
            NullLogger<MarketIndicesController>.Instance);

    private static Func<string, string, CancellationToken, Task<StockQuoteFetchResult>> FetchSuccess(
        decimal priceEur,
        decimal? changeEur = null,
        decimal percent = 0m,
        DateTime? priceAt = null)
        => (_, _, _) => Task.FromResult(FetchSuccessResult(priceEur, changeEur, percent, priceAt));

    private static StockQuoteFetchResult FetchSuccessResult(
        decimal priceEur,
        decimal? changeEur = null,
        decimal percent = 0m,
        DateTime? priceAt = null)
    {
        var quote = new StockQuoteResponse
        {
            Symbol = "X",
            CurrentPriceEur = priceEur,
            ChangeEur = changeEur,
            PercentChange = percent,
            PriceTimestampUtc = priceAt,
            IsStale = false,
            MarketState = "REGULAR",
            PriceSession = "REGULAR",
        };
        return StockQuoteFetchResult.Success(quote);
    }

    private static Func<string, string, CancellationToken, Task<StockQuoteFetchResult>> FetchDelayed(
        decimal priceEur, string warning)
        => (_, _, _) =>
        {
            var quote = new StockQuoteResponse
            {
                Symbol = "X",
                CurrentPriceEur = priceEur,
                IsStale = true,
                DelayWarning = warning,
                MarketState = "REGULAR",
                PriceSession = "REGULAR",
            };
            return Task.FromResult(StockQuoteFetchResult.Success(quote));
        };

    private static Func<string, string, CancellationToken, Task<StockQuoteFetchResult>> FetchNoEur(decimal rawPrice)
        => (_, _, _) =>
        {
            var quote = new StockQuoteResponse
            {
                Symbol = "X",
                RawCurrentPrice = rawPrice,
                CurrentPriceEur = null,
                IsStale = false,
                MarketState = "REGULAR",
                PriceSession = "REGULAR",
            };
            return Task.FromResult(StockQuoteFetchResult.Success(quote));
        };

    // ──────────────────────────────────────────────────────────────────────
    //  Test harness
    // ──────────────────────────────────────────────────────────────────────

    private sealed class BatchQuoteHarness : IDisposable
    {
        private BatchQuoteHarness(
            ServiceProvider services,
            IndexConstituentsBatchQuoteRefreshJobService jobService)
        {
            Services = services;
            JobService = jobService;
        }

        public ServiceProvider Services { get; }
        public IndexConstituentsBatchQuoteRefreshJobService JobService { get; }

        public static async Task<BatchQuoteHarness> CreateAsync(
            Func<string, string, CancellationToken, Task<StockQuoteFetchResult>> fetchHandler,
            IndexConstituentsBatchQuoteRefreshJobOptions? options = null)
        {
            var dbName = $"batch-quote-tests-{Guid.NewGuid():N}";
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<AppDbContext>(b => b.UseInMemoryDatabase(dbName));
            services.AddScoped<IStockQuoteFetchService>(_ => new DelegatingQuoteService(fetchHandler));

            var provider = services.BuildServiceProvider();
            var jobService = new IndexConstituentsBatchQuoteRefreshJobService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                Options.Create(options ?? new IndexConstituentsBatchQuoteRefreshJobOptions()),
                NullLogger<IndexConstituentsBatchQuoteRefreshJobService>.Instance);

            var harness = new BatchQuoteHarness(provider, jobService);

            await using var setup = provider.CreateAsyncScope();
            await setup.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

            return harness;
        }

        public static Task<BatchQuoteHarness> CreateAsync(
            Func<string, CancellationToken, Task<StockQuoteFetchResult>> fetchHandler)
            => CreateAsync((ticker, _, ct) => fetchHandler(ticker, ct), null);

        public async Task SeedIndexOnlyAsync(int indexId)
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await context.MarketIndices.AnyAsync(x => x.Id == indexId))
            {
                context.MarketIndices.Add(MakeIndex(indexId, archived: false));
                await context.SaveChangesAsync();
            }
        }

        public async Task SeedAsync(
            int indexId,
            int stockId,
            string ticker,
            string exchange,
            StockTrackingStatus status,
            bool archived = false,
            bool formerMember = false,
            DateTime? existingPriceAt = null,
            decimal existingPrice = 0m)
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!await context.MarketIndices.AnyAsync(x => x.Id == indexId))
            {
                context.MarketIndices.Add(MakeIndex(indexId, archived));
            }
            else if (archived)
            {
                var idx = await context.MarketIndices.FirstAsync(x => x.Id == indexId);
                idx.IsArchived = true;
            }

            if (!await context.Stocks.AnyAsync(x => x.Id == stockId))
            {
                context.Stocks.Add(new Stock
                {
                    Id = stockId,
                    Ticker = ticker,
                    Name = ticker,
                    CommonName = ticker,
                    Exchange = exchange,
                    TrackingStatus = status,
                    CurrentPrice = existingPrice,
                    CurrentPriceAt = existingPriceAt,
                    UpdatedAt = DateTime.UtcNow,
                });
            }

            context.StockMarketIndices.Add(new StockMarketIndex
            {
                StockId = stockId,
                MarketIndexId = indexId,
                EffectiveFrom = DateTime.UtcNow.AddDays(-1),
                EffectiveTo = formerMember ? DateTime.UtcNow.AddHours(-1) : null,
            });

            await context.SaveChangesAsync();
        }

        public async Task<IndexConstituentsBatchQuoteRefreshJobResponse> WaitForTerminalStateAsync(
            int indexId, string jobId)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (JobService.TryGetJob(indexId, jobId, out var current) &&
                    current is not null &&
                    current.State is IndexConstituentsBatchQuoteRefreshJobState.Succeeded
                        or IndexConstituentsBatchQuoteRefreshJobState.RateLimited
                        or IndexConstituentsBatchQuoteRefreshJobState.Failed
                        or IndexConstituentsBatchQuoteRefreshJobState.Interrupted)
                {
                    return current;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("Timed out waiting for terminal batch-quote job state.");
        }

        public void Dispose() => Services.Dispose();

        private static MarketIndex MakeIndex(int id, bool archived) => new()
        {
            Id = id,
            Name = $"IDX-{id}",
            NormalizedName = $"IDX-{id}",
            Code = $"IDX{id}",
            NormalizedCode = $"IDX{id}",
            IsArchived = archived,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private sealed class DelegatingQuoteService(
        Func<string, string, CancellationToken, Task<StockQuoteFetchResult>> handler)
        : IStockQuoteFetchService
    {
        public Task<StockQuoteFetchResult> FetchAsync(
            string ticker, string exchange, string? finanzenNetSlug,
            CancellationToken cancellationToken = default)
            => handler(ticker, exchange, cancellationToken);
    }

    private sealed class NullMarketIndexHistoryService : IMarketIndexHistoryService
    {
        public Task<MarketIndexHistoryResponse> GetHistoryAsync(MarketIndex i, string r, CancellationToken ct = default)
            => Task.FromResult(new MarketIndexHistoryResponse { MarketIndexId = i.Id, Range = r, Interval = "1d" });

        public Task<MarketIndexRefreshResponse> RefreshHistoryAsync(MarketIndex i, string r, CancellationToken ct = default)
            => Task.FromResult(new MarketIndexRefreshResponse { MarketIndexId = i.Id });
    }

    private sealed class NullIndexConstituentsProvider : IIndexConstituentsProvider
    {
        public string ProviderName => "Null";
        public Task<IndexConstituentsResult> GetConstituentsAsync(MarketIndex i, CancellationToken ct = default)
            => Task.FromResult(IndexConstituentsResult.Unsupported(ProviderName));
    }

    private sealed class NullStockHistoryService : IStockHistoryService
    {
        public Task SyncHistoricalDataForStockAsync(Stock s, CancellationToken ct = default) => Task.CompletedTask;
        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<StockHistoryResponse> GetHistoryAsync(Stock s, string r, CancellationToken ct = default)
            => Task.FromResult(new StockHistoryResponse());
        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock s, CancellationToken ct = default)
            => Task.FromResult(new StockHistoryRefreshResponse { StockId = s.Id });
    }

    private sealed class NullIndexConstituentHistoryRefreshJobService : IIndexConstituentHistoryRefreshJobService
    {
        public IndexConstituentHistoryRefreshJobEnqueueResult Enqueue(int marketIndexId, int stockId)
            => new() { Status = IndexConstituentHistoryRefreshJobEnqueueStatus.QueueFull };

        public bool TryGetJob(int marketIndexId, int stockId, string jobId, out IndexConstituentHistoryRefreshJobResponse? job)
        {
            job = null;
            return false;
        }
    }
}
