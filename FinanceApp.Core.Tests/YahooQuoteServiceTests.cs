using System.Net;
using System.Text;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class YahooQuoteServiceTests
{
    private const string ValidChartResponse = """
        {
          "chart": {
            "result": [{
              "meta": {
                "currency": "EUR",
                "financialCurrency": "EUR",
                "regularMarketPrice": 520.5,
                "chartPreviousClose": 514.0,
                "regularMarketChangePercent": 1.26,
                "marketState": "REGULAR"
              },
              "timestamp": [1720000000],
              "indicators": {
                "quote": [{ "close": [520.5], "open": [514.0], "high": [521.0], "low": [513.0], "volume": [100000] }]
              }
            }]
          }
        }
        """;

    [Fact]
    public async Task GetQuoteAsync_ReturnsQuoteWithCorrectValues()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(ValidChartResponse);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote);
        Assert.Equal("RHM.DE", result.Quote!.Symbol);
        Assert.Equal(520.5m, result.Quote.CurrentPrice);
        Assert.Equal(514.0m, result.Quote.PreviousClose);
        Assert.Equal(1.26m, result.Quote.PercentChange);
        Assert.Equal("EUR", result.Quote.Currency);
        Assert.Equal("EUR", result.Quote.EstimateCurrency);
        Assert.Equal("REGULAR", result.Quote.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_MarketStateClosed_ReturnsClosedState()
    {
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "EUR",
                    "regularMarketPrice": 520.5,
                    "chartPreviousClose": 514.0,
                    "regularMarketChangePercent": 1.26,
                    "marketState": "CLOSED"
                  }
                }]
              }
            }
            """;
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(response);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.Equal("CLOSED", result.Quote!.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_ZeroCurrentPrice_ReturnsBadGateway()
    {
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "EUR",
                    "regularMarketPrice": 0,
                    "chartPreviousClose": 514.0,
                    "marketState": "CLOSED"
                  }
                }]
              }
            }
            """;
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(response);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal("Quote provider returned an invalid current price.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_EmptyResponse_ReturnsBadGateway()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task GetQuoteAsync_Http429_ReturnsRateLimitError()
    {
        var handler = new StubHttpMessageHandler
        {
            // Factory creates a new response per call to avoid ObjectDisposedException on retries
            AlwaysRespondFactory = () => new HttpResponseMessage((HttpStatusCode)StatusCodes.Status429TooManyRequests)
        };

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal("Quote provider rate limit exceeded.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_HttpFailure_ReturnsBadGateway()
    {
        var handler = new StubHttpMessageHandler
        {
            AlwaysRespondFactory = () => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task GetQuoteAsync_Timeout_ReturnsGatewayTimeout()
    {
        var handler = new StubHttpMessageHandler
        {
            ExceptionFactory = _ => new TaskCanceledException("timeout")
        };

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, result.StatusCode);
        Assert.Equal("Quote provider request timed out.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_InvalidJson_ReturnsBadGateway()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("not-valid-json{{{");

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task GetQuoteAsync_MissingMetaField_ReturnsBadGateway()
    {
        var response = """{"chart":{"result":[{}]}}""";
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(response);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
    }

    // ── currentTradingPeriod fallback tests ───────────────────────────────────

    [Fact]
    public async Task GetQuoteAsync_NoMarketState_CurrentTimeInRegularPeriod_ReturnsRegular()
    {
        // now = 1000; regular = [900, 1100)
        var fakeTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1000));
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(BuildResponseWithTradingPeriod(
            regular: (900, 1100), pre: (700, 900), post: (1100, 1300)));

        var service = CreateService(handler, fakeTime);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.Equal("REGULAR", result.Quote!.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_NoMarketState_CurrentTimeInPrePeriod_ReturnsPre()
    {
        // now = 800; pre = [700, 900)
        var fakeTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(800));
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(BuildResponseWithTradingPeriod(
            regular: (900, 1100), pre: (700, 900), post: (1100, 1300)));

        var service = CreateService(handler, fakeTime);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.Equal("PRE", result.Quote!.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_NoMarketState_CurrentTimeInPostPeriod_ReturnsPost()
    {
        // now = 1200; post = [1100, 1300)
        var fakeTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1200));
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(BuildResponseWithTradingPeriod(
            regular: (900, 1100), pre: (700, 900), post: (1100, 1300)));

        var service = CreateService(handler, fakeTime);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.Equal("POST", result.Quote!.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_NoMarketState_CurrentTimeOutsideAllPeriods_ReturnsClosed()
    {
        // now = 500; all periods are in the future
        var fakeTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(500));
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(BuildResponseWithTradingPeriod(
            regular: (900, 1100), pre: (700, 900), post: (1100, 1300)));

        var service = CreateService(handler, fakeTime);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.Equal("CLOSED", result.Quote!.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_NoMarketState_NoPeriodData_ReturnsUnknown()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1000));
        var handler = new StubHttpMessageHandler();
        // Response has no marketState and no currentTradingPeriod
        handler.EnqueueJson("""
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "EUR",
                    "regularMarketPrice": 520.5,
                    "chartPreviousClose": 514.0
                  }
                }]
              }
            }
            """);

        var service = CreateService(handler, fakeTime);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.Equal("UNKNOWN", result.Quote!.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_RecognizedExplicitMarketState_IsAuthoritative()
    {
        // Even though the time is inside the pre period, the explicit REGULAR should win.
        var fakeTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(800));
        var handler = new StubHttpMessageHandler();
        // marketState explicitly says REGULAR
        handler.EnqueueJson(BuildResponseWithTradingPeriodAndExplicitState(
            "REGULAR", regular: (900, 1100), pre: (700, 900), post: (1100, 1300)));

        var service = CreateService(handler, fakeTime);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.Equal("REGULAR", result.Quote!.MarketState);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string BuildResponseWithTradingPeriod(
        (long start, long end) regular,
        (long start, long end) pre,
        (long start, long end) post) =>
        $$"""
        {
          "chart": {
            "result": [{
              "meta": {
                "currency": "EUR",
                "regularMarketPrice": 520.5,
                "chartPreviousClose": 514.0,
                "currentTradingPeriod": {
                  "pre":     { "start": {{pre.start}},     "end": {{pre.end}}     },
                  "regular": { "start": {{regular.start}}, "end": {{regular.end}} },
                  "post":    { "start": {{post.start}},    "end": {{post.end}}    }
                }
              }
            }]
          }
        }
        """;

    private static string BuildResponseWithTradingPeriodAndExplicitState(
        string marketState,
        (long start, long end) regular,
        (long start, long end) pre,
        (long start, long end) post) =>
        $$"""
        {
          "chart": {
            "result": [{
              "meta": {
                "currency": "EUR",
                "regularMarketPrice": 520.5,
                "chartPreviousClose": 514.0,
                "marketState": "{{marketState}}",
                "currentTradingPeriod": {
                  "pre":     { "start": {{pre.start}},     "end": {{pre.end}}     },
                  "regular": { "start": {{regular.start}}, "end": {{regular.end}} },
                  "post":    { "start": {{post.start}},    "end": {{post.end}}    }
                }
              }
            }]
          }
        }
        """;

    private static YahooQuoteService CreateService(HttpMessageHandler handler, TimeProvider? timeProvider = null)
    {
        var httpClientFactory = new StubHttpClientFactory(
            new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            });
        return new YahooQuoteService(httpClientFactory, NullLogger<YahooQuoteService>.Instance, timeProvider);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _queue = new();

        public Func<HttpRequestMessage, Exception>? ExceptionFactory { get; init; }

        /// <summary>Always return a fresh response from this factory, for testing retry exhaustion.</summary>
        public Func<HttpResponseMessage>? AlwaysRespondFactory { get; init; }

        public void EnqueueJson(string json)
        {
            EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        public void EnqueueResponse(HttpResponseMessage response) =>
            _queue.Enqueue(() => response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ExceptionFactory is not null)
            {
                throw ExceptionFactory(request);
            }

            if (AlwaysRespondFactory is not null)
            {
                return Task.FromResult(AlwaysRespondFactory());
            }

            if (_queue.Count > 0)
            {
                return Task.FromResult(_queue.Dequeue().Invoke());
            }

            throw new InvalidOperationException("No stubbed response configured.");
        }
    }
}
