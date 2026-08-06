using System.Net;
using System.Text;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        Assert.Equal((520.5m - 514.0m) / 514.0m * 100m, result.Quote.PercentChange);
        Assert.Equal("EUR", result.Quote.Currency);
        Assert.Equal("EUR", result.Quote.EstimateCurrency);
        Assert.Equal("REGULAR", result.Quote.MarketState);
        // Yahoo always returns regularMarketPrice, so PriceSession must always be REGULAR
        Assert.Equal("REGULAR", result.Quote.PriceSession);
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

    // ── PriceSession / PriceTimestampUtc tests ────────────────────────────────

    [Fact]
    public async Task GetQuoteAsync_PreMarketState_PriceSessionIsStillRegular()
    {
        // Verifies that when marketState=PRE the price session is still labelled REGULAR
        // because Yahoo only returns regularMarketPrice, not a pre-market price.
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "EUR",
                    "regularMarketPrice": 390.54,
                    "chartPreviousClose": 388.0,
                    "regularMarketChangePercent": 0.65,
                    "marketState": "PRE"
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
        Assert.Equal("PRE", result.Quote!.MarketState);
        Assert.Equal("REGULAR", result.Quote.PriceSession);
    }

    [Fact]
    public async Task GetQuoteAsync_PostMarketState_PriceSessionIsStillRegular()
    {
        // Verifies that when marketState=POST the price session is still labelled REGULAR.
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "EUR",
                    "regularMarketPrice": 390.54,
                    "chartPreviousClose": 388.0,
                    "marketState": "POST"
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
        Assert.Equal("POST", result.Quote!.MarketState);
        Assert.Equal("REGULAR", result.Quote.PriceSession);
    }

    [Fact]
    public async Task GetQuoteAsync_RegularMarketTimePresent_IsPropagatedAsPriceTimestampUtc()
    {
        // regularMarketTime = 1720000000 → 2024-07-03T10:26:40Z
        var expectedUtc = DateTimeOffset.FromUnixTimeSeconds(1720000000).UtcDateTime;
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "EUR",
                    "regularMarketPrice": 520.5,
                    "chartPreviousClose": 514.0,
                    "regularMarketTime": 1720000000,
                    "marketState": "REGULAR"
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
        Assert.Equal(expectedUtc, result.Quote!.PriceTimestampUtc);
    }

    [Fact]
    public async Task GetQuoteAsync_NoRegularMarketTime_PriceTimestampUtcIsNull()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(ValidChartResponse); // ValidChartResponse has no regularMarketTime

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("RHM.DE");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Quote!.PriceTimestampUtc);
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

    // ── previous-close baseline precedence tests ──────────────────────────────

    [Fact]
    public async Task GetQuoteAsync_AmzFrankfurtRegression_UsesChartPreviousCloseAndCalculatedPercent()
    {
        const decimal currentPrice = 236.30m;
        const decimal chartPreviousClose = 230.60m;
        const decimal regularMarketChangePercent = 15.833333333333333333333333330m;

        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "EUR",
                    "regularMarketPrice": 236.30,
                    "regularMarketChange": 32.30,
                    "regularMarketChangePercent": 15.833333333333333333333333330,
                    "chartPreviousClose": 230.60,
                    "marketState": "CLOSED"
                  }
                }]
              }
            }
            """;
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(response);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("AMZ.F");

        Assert.True(result.IsSuccess);
        var quote = result.Quote!;
        Assert.Equal(currentPrice, quote.CurrentPrice);
        Assert.Equal(chartPreviousClose, quote.PreviousClose);
        Assert.NotEqual(currentPrice - 32.30m, quote.PreviousClose);
        Assert.Equal(5.70m, quote.CurrentPrice - quote.PreviousClose);

        var expectedPercentChange = (currentPrice - chartPreviousClose) / chartPreviousClose * 100m;
        Assert.Equal(expectedPercentChange, quote.PercentChange);
        Assert.NotEqual(regularMarketChangePercent, quote.PercentChange);
    }

    [Fact]
    public async Task GetQuoteAsync_ChartPreviousCloseTakesPrecedenceOverConflictingProviderFields()
    {
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "EUR",
                    "regularMarketPrice": 105.0,
                    "chartPreviousClose": 101.0,
                    "previousClose": 99.0,
                    "regularMarketChange": 9.0,
                    "regularMarketChangePercent": 9.375,
                    "marketState": "CLOSED"
                  }
                }]
              }
            }
            """;
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(response);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("TEST.F");

        Assert.True(result.IsSuccess);
        var quote = result.Quote!;
        Assert.Equal(101.0m, quote.PreviousClose);
        Assert.Equal((105.0m - 101.0m) / 101.0m * 100m, quote.PercentChange);
    }

    [Fact]
    public async Task GetQuoteAsync_PreviousCloseIsUsedWhenChartPreviousCloseIsMissingOrInvalid()
    {
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "USD",
                    "regularMarketPrice": 120.0,
                    "chartPreviousClose": 0,
                    "previousClose": 118.5,
                    "regularMarketChange": 20.0,
                    "marketState": "CLOSED"
                  }
                }]
              }
            }
            """;
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(response);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("TEST");

        Assert.True(result.IsSuccess);
        var quote = result.Quote!;
        Assert.Equal(118.5m, quote.PreviousClose);
        Assert.Equal((120.0m - 118.5m) / 118.5m * 100m, quote.PercentChange);
    }

    [Fact]
    public async Task GetQuoteAsync_RegularMarketChangeIsUsedOnlyWhenPreviousCloseFieldsAreUnavailable()
    {
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "USD",
                    "regularMarketPrice": 100.0,
                    "chartPreviousClose": -1.0,
                    "previousClose": 0,
                    "regularMarketChange": 5.0,
                    "marketState": "REGULAR"
                  }
                }]
              }
            }
            """;
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(response);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("TEST");

        Assert.True(result.IsSuccess);
        var quote = result.Quote!;
        Assert.Equal(95.0m, quote.PreviousClose);
        Assert.Equal((100.0m - 95.0m) / 95.0m * 100m, quote.PercentChange);
    }

    [Fact]
    public async Task GetQuoteAsync_InvalidDerivedBaseline_FallsBackToCurrentPrice()
    {
        var response = """
            {
              "chart": {
                "result": [{
                  "meta": {
                    "currency": "USD",
                    "regularMarketPrice": 100.0,
                    "chartPreviousClose": -1.0,
                    "previousClose": -2.0,
                    "regularMarketChange": 150.0,
                    "regularMarketChangePercent": -60.0,
                    "marketState": "CLOSED"
                  }
                }]
              }
            }
            """;
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(response);

        var service = CreateService(handler);
        var result = await service.GetQuoteAsync("TEST");

        Assert.True(result.IsSuccess);
        var quote = result.Quote!;
        Assert.Equal(100.0m, quote.PreviousClose);
        Assert.Equal(0m, quote.PercentChange);
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
        var httpClientFactory = new StubHttpClientFactory(new HttpClient(handler));
        var coordinator = new YahooRequestCoordinator(
            httpClientFactory,
            NullLogger<YahooRequestCoordinator>.Instance,
            Options.Create(new YahooFinanceOptions
            {
                MinRequestInterval = TimeSpan.Zero,
                CooldownDuration = TimeSpan.FromMinutes(30),
                QuoteCacheDuration = TimeSpan.Zero,
                RequestTimeout = TimeSpan.FromSeconds(10)
            }),
            timeProvider);
        return new YahooQuoteService(
            coordinator,
            NullLogger<YahooQuoteService>.Instance,
            Options.Create(new YahooFinanceOptions
            {
                QuoteCacheDuration = TimeSpan.Zero
            }),
            timeProvider);
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
