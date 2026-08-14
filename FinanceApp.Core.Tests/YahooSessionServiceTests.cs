using System.Net;
using System.Text;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class YahooSessionServiceTests
{
    [Fact]
    public async Task GetSessionAsync_ObtainsCookieAndCrumb()
    {
        var handler = new QueueHandler(
            Response(HttpStatusCode.OK, "", setCookie: "A1=session-cookie; Path=/; Domain=.yahoo.com"),
            Response(HttpStatusCode.OK, "crumb-value"));
        var service = CreateService(handler);

        var result = await service.GetSessionAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Session);
        Assert.Equal("crumb-value", result.Session!.Crumb);
        Assert.Contains("A1=session-cookie", result.Session.CookieHeader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSessionAsync_ConcurrentRequestsUseSingleFlightInitialization()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var handler = new CallbackHandler(async (request, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                started.TrySetResult(true);
                await release.Task;
            }

            return request.RequestUri!.AbsoluteUri.Contains("getcrumb", StringComparison.Ordinal)
                ? Response(HttpStatusCode.OK, "crumb-value")
                : Response(HttpStatusCode.OK, "", setCookie: "A1=session-cookie; Path=/; Domain=.yahoo.com");
        });

        var service = CreateService(handler);
        var first = service.GetSessionAsync();
        await started.Task;
        var second = service.GetSessionAsync();
        var third = service.GetSessionAsync();
        release.TrySetResult(true);

        var results = await Task.WhenAll(first, second, third);

        Assert.All(results, x => Assert.True(x.IsSuccess));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetSessionAsync_RespectsTtlAndRefreshesAfterExpiration()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero));
        var handler = new QueueHandler(
            Response(HttpStatusCode.OK, "", setCookie: "A1=first; Path=/"),
            Response(HttpStatusCode.OK, "crumb-1"),
            Response(HttpStatusCode.OK, "", setCookie: "A1=second; Path=/"),
            Response(HttpStatusCode.OK, "crumb-2"));
        var service = CreateService(handler, timeProvider, sessionTtl: TimeSpan.FromMinutes(10));

        var first = await service.GetSessionAsync();
        var second = await service.GetSessionAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(11));
        var third = await service.GetSessionAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(third.IsSuccess);
        Assert.Equal("crumb-1", first.Session!.Crumb);
        Assert.Equal("crumb-1", second.Session!.Crumb);
        Assert.Equal("crumb-2", third.Session!.Crumb);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task GetSessionAsync_MalformedCrumb_ReturnsTypedFailure()
    {
        var handler = new QueueHandler(
            Response(HttpStatusCode.OK, "", setCookie: "A1=session-cookie; Path=/"),
            Response(HttpStatusCode.OK, "{}"));
        var service = CreateService(handler);

        var result = await service.GetSessionAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(YahooSessionFailureCategory.InvalidSessionPayload, result.FailureCategory);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task GetSessionAsync_ConsentRedirect_ReturnsConsentFailure()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        redirect.Headers.Location = new Uri("https://guce.yahoo.com/consent");
        redirect.Headers.TryAddWithoutValidation("Set-Cookie", "A1=session-cookie; Path=/");
        var handler = new QueueHandler(
            Response(HttpStatusCode.OK, "", setCookie: "A1=session-cookie; Path=/"),
            redirect,
            Response(HttpStatusCode.OK, "<html><body>consent required</body></html>"));
        var service = CreateService(handler);

        var result = await service.GetSessionAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(YahooSessionFailureCategory.ConsentFailure, result.FailureCategory);
    }

    [Fact]
    public async Task GetSessionAsync_Timeout_ReturnsTimeoutFailure()
    {
        var handler = new ThrowingHandler(new TaskCanceledException("timeout"));
        var service = CreateService(handler);

        var result = await service.GetSessionAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(YahooSessionFailureCategory.Timeout, result.FailureCategory);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, result.StatusCode);
    }

    private static YahooSessionService CreateService(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null,
        TimeSpan? sessionTtl = null)
    {
        var client = new HttpClient(handler);
        var factory = new StubHttpClientFactory(client);
        return new YahooSessionService(
            factory,
            Options.Create(new YahooFinanceOptions
            {
                SessionTtl = sessionTtl ?? TimeSpan.FromMinutes(20),
                SessionInitializationTimeout = TimeSpan.FromSeconds(3)
            }),
            timeProvider ?? TimeProvider.System,
            NullLogger<YahooSessionService>.Instance);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string content, string? setCookie = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/plain")
        };
        if (!string.IsNullOrWhiteSpace(setCookie))
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        }

        return response;
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        private int _callCount;

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

    private sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
