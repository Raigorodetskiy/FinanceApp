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

public class IndexConstituentHistoryRefreshJobServiceTests
{
    [Fact]
    public async Task StartEndpoint_ReturnsAcceptedQuickly_WithoutWaitingForRefreshCompletion()
    {
        using var harness = await JobHarness.CreateAsync(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new StockHistoryRefreshResponse { StockId = 7001 };
        });

        await harness.SeedAsync(1, 7001, "AAPL", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        await using var requestScope = harness.Services.CreateAsyncScope();
        var controller = CreateController(
            requestScope.ServiceProvider.GetRequiredService<AppDbContext>(),
            harness.JobService);

        var startedAt = DateTime.UtcNow;
        var response = await controller.RefreshConstituentHistory(1, 7001, cancellationToken: default);
        var elapsed = DateTime.UtcNow - startedAt;

        var accepted = Assert.IsType<AcceptedResult>(response.Result);
        var payload = Assert.IsType<IndexConstituentHistoryRefreshJobResponse>(accepted.Value);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(500), $"Elapsed: {elapsed.TotalMilliseconds} ms");
        Assert.True(
            payload.State is IndexConstituentHistoryRefreshJobState.Queued or IndexConstituentHistoryRefreshJobState.Running);
    }

    [Fact]
    public async Task DuplicateStarts_ReturnSameActiveJob_AndRunRefreshOnce()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await JobHarness.CreateAsync(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
            return new StockHistoryRefreshResponse { StockId = 7002, ImportedPoints = 3 };
        });

        await harness.SeedAsync(1, 7002, "MSFT", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var first = harness.JobService.Enqueue(1, 7002);
        var second = harness.JobService.Enqueue(1, 7002);
        Assert.Equal(IndexConstituentHistoryRefreshJobEnqueueStatus.Enqueued, first.Status);
        Assert.Equal(IndexConstituentHistoryRefreshJobEnqueueStatus.ReusedActiveJob, second.Status);
        Assert.NotNull(first.Job);
        Assert.NotNull(second.Job);
        Assert.Equal(first.Job!.JobId, second.Job!.JobId);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, harness.CallCount);
        gate.TrySetResult();

        var completed = await harness.WaitForTerminalStateAsync(1, 7002, first.Job.JobId);
        Assert.Equal(IndexConstituentHistoryRefreshJobState.Succeeded, completed.State);
        Assert.Equal(1, harness.CallCount);
    }

    [Fact]
    public async Task Worker_RevalidatesCurrentMembershipBeforeRefresh()
    {
        using var harness = await JobHarness.CreateAsync((stock, _) =>
            Task.FromResult(new StockHistoryRefreshResponse { StockId = stock.Id, ImportedPoints = 1 }));

        await harness.SeedAsync(1, 7003, "SAP", StockExchanges.Frankfurt, StockTrackingStatus.CatalogOnly);
        var queued = harness.JobService.Enqueue(1, 7003);
        Assert.NotNull(queued.Job);

        await harness.RemoveMembershipAsync(1, 7003);
        await harness.JobService.StartAsync(CancellationToken.None);

        var completed = await harness.WaitForTerminalStateAsync(1, 7003, queued.Job!.JobId);
        Assert.Equal(IndexConstituentHistoryRefreshJobState.Failed, completed.State);
        Assert.Equal("Акция не входит в текущий состав выбранного индекса.", completed.Error);
        Assert.Equal(0, harness.CallCount);
    }

    [Fact]
    public async Task Worker_MapsSuccessRateLimitAndFailure_AndContinuesAfterException()
    {
        using var harness = await JobHarness.CreateAsync((stock, _) =>
        {
            return stock.Id switch
            {
                7004 => throw new Exception("boom"),
                7005 => Task.FromResult(new StockHistoryRefreshResponse { StockId = stock.Id, RateLimited = true }),
                _ => Task.FromResult(new StockHistoryRefreshResponse { StockId = stock.Id, DeletedPoints = 1, ImportedPoints = 2 })
            };
        });

        await harness.SeedAsync(1, 7004, "AAA", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.SeedAsync(1, 7005, "BBB", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.SeedAsync(1, 7006, "CCC", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var a = harness.JobService.Enqueue(1, 7004).Job!;
        var b = harness.JobService.Enqueue(1, 7005).Job!;
        var c = harness.JobService.Enqueue(1, 7006).Job!;

        var aDone = await harness.WaitForTerminalStateAsync(1, 7004, a.JobId);
        var bDone = await harness.WaitForTerminalStateAsync(1, 7005, b.JobId);
        var cDone = await harness.WaitForTerminalStateAsync(1, 7006, c.JobId);

        Assert.Equal(IndexConstituentHistoryRefreshJobState.Failed, aDone.State);
        Assert.Equal(IndexConstituentHistoryRefreshJobState.RateLimited, bDone.State);
        Assert.Equal(IndexConstituentHistoryRefreshJobState.Succeeded, cDone.State);
        Assert.Equal(3, harness.CallCount);
    }

    [Fact]
    public async Task StopAsync_InterruptsActiveJobs()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await JobHarness.CreateAsync(async (_, cancellationToken) =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return new StockHistoryRefreshResponse { StockId = 7007 };
        });

        await harness.SeedAsync(1, 7007, "NFLX", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await harness.JobService.StartAsync(CancellationToken.None);

        var job = harness.JobService.Enqueue(1, 7007).Job!;
        await harness.WaitForNonQueuedStateAsync(1, 7007, job.JobId);
        await harness.JobService.StopAsync(CancellationToken.None);

        Assert.True(harness.JobService.TryGetJob(1, 7007, job.JobId, out var status));
        Assert.NotNull(status);
        Assert.Equal(IndexConstituentHistoryRefreshJobState.Interrupted, status!.State);
        gate.TrySetResult();
    }

    [Fact]
    public async Task QueueCapacityAndTtlCleanup_AreBounded()
    {
        using var queueOnlyHarness = await JobHarness.CreateAsync(
            (stock, _) => Task.FromResult(new StockHistoryRefreshResponse { StockId = stock.Id }),
            new IndexConstituentHistoryRefreshJobOptions
            {
                QueueCapacity = 1,
                RegistryCapacity = 4,
                CompletedJobTtl = TimeSpan.FromMilliseconds(50)
            });

        await queueOnlyHarness.SeedAsync(1, 7008, "ORCL", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        await queueOnlyHarness.SeedAsync(1, 7009, "INTC", StockExchanges.Nyse, StockTrackingStatus.CatalogOnly);
        var first = queueOnlyHarness.JobService.Enqueue(1, 7008);
        var second = queueOnlyHarness.JobService.Enqueue(1, 7009);
        Assert.Equal(IndexConstituentHistoryRefreshJobEnqueueStatus.Enqueued, first.Status);
        Assert.Equal(IndexConstituentHistoryRefreshJobEnqueueStatus.QueueFull, second.Status);

        await queueOnlyHarness.JobService.StartAsync(CancellationToken.None);
        var completed = await queueOnlyHarness.WaitForTerminalStateAsync(1, 7008, first.Job!.JobId);
        Assert.Equal(IndexConstituentHistoryRefreshJobState.Succeeded, completed.State);

        await Task.Delay(120);
        Assert.False(queueOnlyHarness.JobService.TryGetJob(1, 7008, first.Job.JobId, out _));
    }

    private static MarketIndicesController CreateController(
        AppDbContext context,
        IIndexConstituentHistoryRefreshJobService jobs)
        => new(
            context,
            new NullMarketIndexHistoryService(),
            new NullIndexConstituentsProvider(),
            new NullStockHistoryService(),
            jobs,
            NullLogger<MarketIndicesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private sealed class JobHarness : IDisposable
    {
        private readonly Func<Stock, CancellationToken, Task<StockHistoryRefreshResponse>> _refreshHandler;

        private JobHarness(
            ServiceProvider services,
            IndexConstituentHistoryRefreshJobService jobService,
            Func<Stock, CancellationToken, Task<StockHistoryRefreshResponse>> refreshHandler)
        {
            Services = services;
            JobService = jobService;
            _refreshHandler = refreshHandler;
        }

        public ServiceProvider Services { get; }
        public IndexConstituentHistoryRefreshJobService JobService { get; }
        public int CallCount { get; private set; }

        public static async Task<JobHarness> CreateAsync(
            Func<Stock, CancellationToken, Task<StockHistoryRefreshResponse>> refreshHandler,
            IndexConstituentHistoryRefreshJobOptions? options = null)
        {
            var dbName = $"job-tests-{Guid.NewGuid():N}";
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<AppDbContext>(builder => builder.UseInMemoryDatabase(dbName));

            var tracker = new CallTracker(refreshHandler);
            services.AddSingleton(tracker);
            services.AddScoped<IStockHistoryService>(sp => new DelegatingStockHistoryService(
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<CallTracker>()));

            var provider = services.BuildServiceProvider();
            var jobService = new IndexConstituentHistoryRefreshJobService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                Options.Create(options ?? new IndexConstituentHistoryRefreshJobOptions()),
                NullLogger<IndexConstituentHistoryRefreshJobService>.Instance);
            var harness = new JobHarness(provider, jobService, refreshHandler);
            tracker.OnCall = () => harness.CallCount++;

            await using var setupScope = provider.CreateAsyncScope();
            var context = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.EnsureCreatedAsync();

            return harness;
        }

        public async Task SeedAsync(int indexId, int stockId, string ticker, string exchange, StockTrackingStatus status)
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await context.MarketIndices.AnyAsync(x => x.Id == indexId))
            {
                context.MarketIndices.Add(new MarketIndex
                {
                    Id = indexId,
                    Name = $"IDX-{indexId}",
                    NormalizedName = $"IDX-{indexId}",
                    Code = $"IDX{indexId}",
                    NormalizedCode = $"IDX{indexId}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            context.Stocks.Add(new Stock
            {
                Id = stockId,
                Ticker = ticker,
                Name = ticker,
                CommonName = ticker,
                Exchange = exchange,
                TrackingStatus = status,
                UpdatedAt = DateTime.UtcNow
            });
            context.StockMarketIndices.Add(new StockMarketIndex
            {
                StockId = stockId,
                MarketIndexId = indexId,
                EffectiveFrom = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        public async Task RemoveMembershipAsync(int indexId, int stockId)
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = await context.StockMarketIndices.FirstAsync(x => x.MarketIndexId == indexId && x.StockId == stockId);
            membership.EffectiveTo = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        public async Task WaitForNonQueuedStateAsync(int indexId, int stockId, string jobId)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (JobService.TryGetJob(indexId, stockId, jobId, out var current)
                    && current is not null
                    && current.State != IndexConstituentHistoryRefreshJobState.Queued)
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("Job did not leave queued state.");
        }

        public async Task<IndexConstituentHistoryRefreshJobResponse> WaitForTerminalStateAsync(int indexId, int stockId, string jobId)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (JobService.TryGetJob(indexId, stockId, jobId, out var current)
                    && current is not null
                    && current.State is IndexConstituentHistoryRefreshJobState.Succeeded
                        or IndexConstituentHistoryRefreshJobState.RateLimited
                        or IndexConstituentHistoryRefreshJobState.Failed
                        or IndexConstituentHistoryRefreshJobState.Interrupted)
                {
                    return current;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("Timed out waiting for terminal job state.");
        }

        public void Dispose()
        {
            JobService.Dispose();
            Services.Dispose();
        }
    }

    private sealed class CallTracker(Func<Stock, CancellationToken, Task<StockHistoryRefreshResponse>> refreshHandler)
    {
        public Action? OnCall { get; set; }

        public Task<StockHistoryRefreshResponse> InvokeAsync(Stock stock, CancellationToken cancellationToken)
        {
            OnCall?.Invoke();
            return refreshHandler(stock, cancellationToken);
        }
    }

    private sealed class DelegatingStockHistoryService(
        AppDbContext context,
        CallTracker tracker) : IStockHistoryService
    {
        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());

        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
        {
            _ = context;
            return tracker.InvokeAsync(stock, cancellationToken);
        }
    }

    private sealed class NullStockHistoryService : IStockHistoryService
    {
        public Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryResponse());

        public Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default)
            => Task.FromResult(new StockHistoryRefreshResponse { StockId = stock.Id });
    }

    private sealed class NullMarketIndexHistoryService : IMarketIndexHistoryService
    {
        public Task<MarketIndexHistoryResponse> GetHistoryAsync(MarketIndex index, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketIndexHistoryResponse { MarketIndexId = index.Id, Range = range, Interval = "1d" });

        public Task<MarketIndexRefreshResponse> RefreshHistoryAsync(MarketIndex index, string range, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketIndexRefreshResponse { MarketIndexId = index.Id });
    }

    private sealed class NullIndexConstituentsProvider : IIndexConstituentsProvider
    {
        public string ProviderName => "Null";

        public Task<IndexConstituentsResult> GetConstituentsAsync(MarketIndex index, CancellationToken cancellationToken = default)
            => Task.FromResult(IndexConstituentsResult.Unsupported(ProviderName));
    }
}
