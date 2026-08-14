using System.Net;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class YahooFundamentalsServiceTests
{
    [Fact]
    public async Task GetFundamentalsAsync_UsesCookieAndUrlEncodedCrumbPair()
    {
        var coordinator = new StubYahooRequestCoordinator();
        coordinator.Enqueue(HttpStatusCode.OK, """{"quoteSummary":{"result":[{}]}}""");
        var sessions = new StubYahooSessionService(
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=cookie123; B=2", "ab+c/==", DateTimeOffset.UtcNow.AddMinutes(20))));
        var service = CreateService(coordinator, sessions);

        var result = await service.GetFundamentalsAsync("STX");

        Assert.True(result.IsSuccess);
        var request = Assert.Single(coordinator.Requests);
        Assert.Contains("crumb=ab%2Bc%2F%3D%3D", request.Url);
        Assert.Contains("/quoteSummary/STX?", request.Url, StringComparison.Ordinal);
        Assert.Equal("A1=cookie123; B=2", request.Headers["Cookie"]);
    }

    [Fact]
    public async Task GetFundamentalsAsync_InvalidCrumb_RefreshesSessionAndRetriesExactlyOnce()
    {
        var coordinator = new StubYahooRequestCoordinator();
        coordinator.Enqueue(HttpStatusCode.Unauthorized, """{"finance":{"result":null,"error":{"code":"Unauthorized","description":"Invalid Crumb"}}}""");
        coordinator.Enqueue(HttpStatusCode.OK, """{"quoteSummary":{"result":[{}]}}""");
        var sessions = new StubYahooSessionService(
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=oldCookie", "oldCrumb", DateTimeOffset.UtcNow.AddMinutes(20))),
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=newCookie", "newCrumb", DateTimeOffset.UtcNow.AddMinutes(20))));
        var service = CreateService(coordinator, sessions);

        var result = await service.GetFundamentalsAsync("STX");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, coordinator.Requests.Count);
        Assert.Equal(1, sessions.InvalidateCalls);
        Assert.Contains("crumb=oldCrumb", coordinator.Requests[0].Url);
        Assert.Contains("crumb=newCrumb", coordinator.Requests[1].Url);
    }

    [Fact]
    public async Task GetFundamentalsAsync_SecondUnauthorizedAfterRefresh_ReturnsFailureWithoutLoop()
    {
        var coordinator = new StubYahooRequestCoordinator();
        coordinator.Enqueue(HttpStatusCode.Unauthorized, """{"finance":{"result":null,"error":{"code":"Unauthorized","description":"Invalid Crumb"}}}""");
        coordinator.Enqueue(HttpStatusCode.Unauthorized, """{"finance":{"result":null,"error":{"code":"Unauthorized","description":"Invalid Crumb"}}}""");
        var sessions = new StubYahooSessionService(
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=firstCookie", "first", DateTimeOffset.UtcNow.AddMinutes(20))),
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=secondCookie", "second", DateTimeOffset.UtcNow.AddMinutes(20))));
        var service = CreateService(coordinator, sessions);

        var result = await service.GetFundamentalsAsync("STX");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal(YahooFundamentalsFailureCategory.ProviderUnauthorized, result.FailureCategory);
        Assert.Equal(2, coordinator.Requests.Count);
    }

    [Theory]
    [InlineData((int)HttpStatusCode.Forbidden, StatusCodes.Status502BadGateway, YahooFundamentalsFailureCategory.ProviderForbidden)]
    [InlineData((int)HttpStatusCode.NotFound, StatusCodes.Status502BadGateway, YahooFundamentalsFailureCategory.ProviderNotFound)]
    [InlineData((int)HttpStatusCode.TooManyRequests, StatusCodes.Status429TooManyRequests, YahooFundamentalsFailureCategory.ProviderRateLimited)]
    [InlineData((int)HttpStatusCode.InternalServerError, StatusCodes.Status502BadGateway, YahooFundamentalsFailureCategory.ProviderServerError)]
    public async Task GetFundamentalsAsync_MapsProviderStatusCodes(int providerStatus, int expectedStatus, YahooFundamentalsFailureCategory expectedCategory)
    {
        var coordinator = new StubYahooRequestCoordinator();
        coordinator.Enqueue((HttpStatusCode)providerStatus, "{}");
        var sessions = new StubYahooSessionService(
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=sessionCookie", "crumb", DateTimeOffset.UtcNow.AddMinutes(20))));
        var service = CreateService(coordinator, sessions);

        var result = await service.GetFundamentalsAsync("STX");

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal(expectedCategory, result.FailureCategory);
    }

    [Fact]
    public async Task GetFundamentalsAsync_InvalidJson_ReturnsTypedInvalidResponseFailure()
    {
        var coordinator = new StubYahooRequestCoordinator();
        coordinator.Enqueue(HttpStatusCode.OK, "not-valid-json");
        var sessions = new StubYahooSessionService(
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=sessionCookie", "crumb", DateTimeOffset.UtcNow.AddMinutes(20))));
        var service = CreateService(coordinator, sessions);

        var result = await service.GetFundamentalsAsync("STX");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal(YahooFundamentalsFailureCategory.InvalidProviderResponse, result.FailureCategory);
    }

    [Fact]
    public async Task GetFundamentalsAsync_SessionFailure_ReturnsTypedSessionFailure()
    {
        var coordinator = new StubYahooRequestCoordinator();
        var sessions = new StubYahooSessionService(
            YahooSessionAcquisitionResult.Failure(
                YahooSessionFailureCategory.ConsentFailure,
                StatusCodes.Status502BadGateway,
                "consent failed"));
        var service = CreateService(coordinator, sessions);

        var result = await service.GetFundamentalsAsync("STX");

        Assert.False(result.IsSuccess);
        Assert.Equal(YahooFundamentalsFailureCategory.ProviderConsentFailure, result.FailureCategory);
        Assert.Empty(coordinator.Requests);
    }

    [Fact]
    public async Task GetFundamentalsAsync_DoesNotLogCrumbOrCookieValues()
    {
        var logger = new ListLogger<YahooFundamentalsService>();
        var coordinator = new StubYahooRequestCoordinator();
        coordinator.Enqueue(HttpStatusCode.Unauthorized, """{"finance":{"result":null,"error":{"code":"Unauthorized","description":"Invalid Crumb"}}}""");
        coordinator.Enqueue(HttpStatusCode.Unauthorized, """{"finance":{"result":null,"error":{"code":"Unauthorized","description":"Invalid Crumb"}}}""");
        var sessions = new StubYahooSessionService(
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=superSecretCookie", "superSecretCrumb", DateTimeOffset.UtcNow.AddMinutes(20))),
            YahooSessionAcquisitionResult.Success(new YahooSession("A1=newSecretCookie", "newSecretCrumb", DateTimeOffset.UtcNow.AddMinutes(20))));
        var service = CreateService(coordinator, sessions, logger);

        _ = await service.GetFundamentalsAsync("STX");

        var joined = string.Join('\n', logger.Messages);
        Assert.DoesNotContain("superSecretCookie", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("superSecretCrumb", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("newSecretCookie", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("newSecretCrumb", joined, StringComparison.Ordinal);
    }

    private static YahooFundamentalsService CreateService(
        StubYahooRequestCoordinator coordinator,
        StubYahooSessionService sessionService,
        ILogger<YahooFundamentalsService>? logger = null)
    {
        return new YahooFundamentalsService(
            coordinator,
            sessionService,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<YahooFundamentalsService>.Instance,
            Options.Create(new YahooFinanceOptions
            {
                MinRequestInterval = TimeSpan.Zero,
                CooldownDuration = TimeSpan.FromMinutes(30),
                QuoteCacheDuration = TimeSpan.Zero,
                FundamentalsCacheDuration = TimeSpan.FromHours(24),
                EarningsCacheDuration = TimeSpan.FromHours(6),
                RequestTimeout = TimeSpan.FromSeconds(10)
            }),
            TimeProvider.System);
    }

    private sealed class StubYahooRequestCoordinator : IYahooRequestCoordinator
    {
        private readonly Queue<YahooHttpResponse> _responses = new();

        public List<YahooRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string content) =>
            _responses.Enqueue(new YahooHttpResponse(statusCode, content));

        public Task<YahooHttpResponse> GetAsync(
            string url,
            string requestLabel,
            YahooRequestExecutionOptions executionOptions,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
        {
            Requests.Add(new YahooRequest(
                url,
                requestLabel,
                additionalHeaders is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(additionalHeaders, StringComparer.OrdinalIgnoreCase)));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No response configured.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed record YahooRequest(string Url, string RequestLabel, IReadOnlyDictionary<string, string> Headers);

    private sealed class StubYahooSessionService(params YahooSessionAcquisitionResult[] sessionResults) : IYahooSessionService
    {
        private readonly Queue<YahooSessionAcquisitionResult> _results = new(sessionResults);

        public int InvalidateCalls { get; private set; }

        public Task<YahooSessionAcquisitionResult> GetSessionAsync(CancellationToken cancellationToken = default)
        {
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No session result configured.");
            }

            return Task.FromResult(_results.Dequeue());
        }

        public void InvalidateSession() => InvalidateCalls++;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
