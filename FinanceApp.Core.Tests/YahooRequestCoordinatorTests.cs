using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FinanceApp.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class YahooRequestCoordinatorTests
{
    [Fact]
    public async Task GetAsync_DifferentUrls_AreSerializedProcessWide()
    {
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CallbackHandler(async (_, callCount, _) =>
        {
            if (callCount == 1)
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
            }

            return Ok("""{"ok":true}""");
        });

        var coordinator = CreateCoordinator(handler, minRequestInterval: TimeSpan.Zero);

        var first = coordinator.GetAsync("https://query2.finance.yahoo.com/v8/finance/chart/AAPL?interval=1d&range=1d", "quote:AAPL", CreateExecutionOptions());
        await firstStarted.Task;

        var second = coordinator.GetAsync("https://query2.finance.yahoo.com/v8/finance/chart/MSFT?interval=1d&range=1d", "quote:MSFT", CreateExecutionOptions());
        await Task.Delay(50);

        Assert.Equal(1, handler.CallCount);

        releaseFirst.TrySetResult(true);
        await Task.WhenAll(first, second);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_EnforcesMinimumSpacingBetweenRequestStarts()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 14, 0, 0, TimeSpan.Zero));
        var handler = new TimestampRecordingHandler(timeProvider);
        var coordinator = CreateCoordinator(
            handler,
            minRequestInterval: TimeSpan.FromSeconds(2),
            timeProvider: timeProvider,
            delayAsync: (delay, _) =>
            {
                timeProvider.Advance(delay);
                return Task.CompletedTask;
            });

        await coordinator.GetAsync("https://query2.finance.yahoo.com/v8/finance/chart/AAPL?interval=1d&range=1d", "quote:AAPL", CreateExecutionOptions(maxAttempts: 1));
        await coordinator.GetAsync("https://query2.finance.yahoo.com/v8/finance/chart/MSFT?interval=1d&range=1d", "quote:MSFT", CreateExecutionOptions(maxAttempts: 1));

        Assert.Equal(2, handler.RequestStarts.Count);
        Assert.Equal(TimeSpan.FromSeconds(2), handler.RequestStarts[1] - handler.RequestStarts[0]);
    }

    [Fact]
    public async Task GetAsync_UsesRetryAfterForSharedCooldown_FailsFast_AndRecoversAfterExpiry()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 14, 0, 0, TimeSpan.Zero));
        var handler = new QueueHandler(
            TooManyRequests(TimeSpan.FromSeconds(5)),
            Ok("""{"ok":true}"""));
        var coordinator = CreateCoordinator(
            handler,
            minRequestInterval: TimeSpan.Zero,
            cooldownDuration: TimeSpan.FromMinutes(30),
            timeProvider: timeProvider,
            delayAsync: (delay, _) =>
            {
                timeProvider.Advance(delay);
                return Task.CompletedTask;
            });

        var quoteResult = await coordinator.GetAsync(
            "https://query2.finance.yahoo.com/v8/finance/chart/RHM.DE?interval=1d&range=1d",
            "quote:RHM.DE",
            CreateExecutionOptions());
        var historyDuringCooldown = await coordinator.GetAsync(
            "https://query2.finance.yahoo.com/v8/finance/chart/RHM.DE?interval=1h&range=7d",
            "history:1h:7d",
            CreateExecutionOptions());

        Assert.Equal(HttpStatusCode.TooManyRequests, quoteResult.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, historyDuringCooldown.StatusCode);
        Assert.True(historyDuringCooldown.IsCooldownHit);
        Assert.Equal(1, handler.CallCount);

        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var recovered = await coordinator.GetAsync(
            "https://query2.finance.yahoo.com/v8/finance/chart/RHM.DE?interval=1h&range=7d",
            "history:1h:7d",
            CreateExecutionOptions());

        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_DoesNotImmediatelyRetryHttp429()
    {
        var handler = new QueueHandler(TooManyRequests(TimeSpan.FromSeconds(30)));
        var coordinator = CreateCoordinator(handler, minRequestInterval: TimeSpan.Zero);

        var response = await coordinator.GetAsync(
            "https://query2.finance.yahoo.com/v8/finance/chart/AAPL?interval=1d&range=1d",
            "quote:AAPL",
            CreateExecutionOptions(maxAttempts: 3));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_CoalescesIdenticalConcurrentRequests()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CallbackHandler(async (_, _, _) =>
        {
            started.TrySetResult(true);
            await release.Task;
            return Ok("""{"shared":true}""");
        });
        var coordinator = CreateCoordinator(handler, minRequestInterval: TimeSpan.Zero);
        const string url = "https://query2.finance.yahoo.com/v8/finance/chart/AAPL?interval=1d&range=1d";

        var first = coordinator.GetAsync(url, "quote:AAPL", CreateExecutionOptions());
        await started.Task;
        var second = coordinator.GetAsync(url, "quote:AAPL", CreateExecutionOptions());

        release.TrySetResult(true);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, handler.CallCount);
        Assert.All(results, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.All(results, response => Assert.Equal("""{"shared":true}""", response.Content));
    }

    [Fact]
    public async Task GetAsync_RetriesTransientFailuresThroughSharedThrottle()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 14, 0, 0, TimeSpan.Zero));
        var handler = new TimestampQueueHandler(
            timeProvider,
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            Ok("""{"ok":true}"""));
        var coordinator = CreateCoordinator(
            handler,
            minRequestInterval: TimeSpan.FromSeconds(1),
            timeProvider: timeProvider,
            delayAsync: (delay, _) =>
            {
                timeProvider.Advance(delay);
                return Task.CompletedTask;
            });

        var response = await coordinator.GetAsync(
            "https://query2.finance.yahoo.com/v8/finance/chart/AAPL?interval=1d&range=1d",
            "quote:AAPL",
            CreateExecutionOptions(maxAttempts: 2, retryBaseDelay: TimeSpan.FromMilliseconds(500), retryMaxDelay: TimeSpan.FromMilliseconds(500)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(TimeSpan.FromSeconds(1), handler.RequestStarts[1] - handler.RequestStarts[0]);
    }

    [Fact]
    public async Task GetAsync_ServesShortTermCachedQuoteWithoutSecondHttpCall()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 14, 0, 0, TimeSpan.Zero));
        var handler = new QueueHandler(Ok("""{"cached":false}"""));
        var coordinator = CreateCoordinator(
            handler,
            minRequestInterval: TimeSpan.Zero,
            timeProvider: timeProvider,
            delayAsync: (delay, _) =>
            {
                timeProvider.Advance(delay);
                return Task.CompletedTask;
            });
        const string url = "https://query2.finance.yahoo.com/v8/finance/chart/AAPL?interval=1d&range=1d";

        var first = await coordinator.GetAsync(
            url,
            "quote:AAPL",
            CreateExecutionOptions(cacheDuration: TimeSpan.FromSeconds(10)));
        var second = await coordinator.GetAsync(
            url,
            "quote:AAPL",
            CreateExecutionOptions(cacheDuration: TimeSpan.FromSeconds(10)));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(second.IsFromCache);
        Assert.Equal(1, handler.CallCount);
    }

    private static YahooRequestCoordinator CreateCoordinator(
        HttpMessageHandler handler,
        TimeSpan minRequestInterval,
        TimeSpan? cooldownDuration = null,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        return new YahooRequestCoordinator(
            new StubHttpClientFactory(new HttpClient(handler)),
            NullLogger<YahooRequestCoordinator>.Instance,
            Options.Create(new YahooFinanceOptions
            {
                MinRequestInterval = minRequestInterval,
                CooldownDuration = cooldownDuration ?? TimeSpan.FromMinutes(30),
                QuoteCacheDuration = TimeSpan.FromSeconds(10),
                RequestTimeout = TimeSpan.FromSeconds(10)
            }),
            timeProvider,
            delayAsync);
    }

    private static YahooRequestExecutionOptions CreateExecutionOptions(
        int maxAttempts = 2,
        TimeSpan? retryBaseDelay = null,
        TimeSpan? retryMaxDelay = null,
        TimeSpan? cacheDuration = null) =>
        new(
            maxAttempts,
            retryBaseDelay ?? TimeSpan.FromMilliseconds(500),
            retryMaxDelay ?? TimeSpan.FromSeconds(20),
            cacheDuration);

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage TooManyRequests(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Too Many Requests", Encoding.UTF8, "text/plain")
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delay) => _utcNow = _utcNow.Add(delay);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        private int _callCount;

        public QueueHandler(params HttpResponseMessage[] responses) => _responses = new Queue<HttpResponseMessage>(responses);

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No response configured.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class TimestampRecordingHandler(ManualTimeProvider timeProvider) : HttpMessageHandler
    {
        public List<DateTimeOffset> RequestStarts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestStarts.Add(timeProvider.GetUtcNow());
            return Task.FromResult(Ok("""{"ok":true}"""));
        }
    }

    private sealed class TimestampQueueHandler : HttpMessageHandler
    {
        private readonly ManualTimeProvider _timeProvider;
        private readonly Queue<HttpResponseMessage> _responses;
        private int _callCount;

        public TimestampQueueHandler(ManualTimeProvider timeProvider, params HttpResponseMessage[] responses)
        {
            _timeProvider = timeProvider;
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<DateTimeOffset> RequestStarts { get; } = [];
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            RequestStarts.Add(_timeProvider.GetUtcNow());
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No response configured.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var callCount = Interlocked.Increment(ref _callCount);
            return callback(request, callCount, cancellationToken);
        }
    }
}
