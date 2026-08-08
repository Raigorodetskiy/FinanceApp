using System.Net;
using System.Text;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class FinnhubQuoteServiceTests
{
    [Fact]
    public async Task GetQuoteAsync_ReturnsQuoteWithProfileAndMarketState()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("/quote", """{"c":150.25,"o":149.75,"pc":148.50,"dp":1.18,"t":1720000000}""");
        handler.EnqueueJson("/stock/profile2", """{"currency":"USD","estimateCurrency":"USD","country":"US","exchange":"NASDAQ"}""");
        handler.EnqueueJson("/stock/market-status", """{"isOpen":true,"session":"regular"}""");

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote);
        Assert.Equal("AAPL", result.Quote!.Symbol);
        Assert.Equal(150.25m, result.Quote.CurrentPrice);
        Assert.Equal(149.75m, result.Quote.OpenPrice);
        Assert.Equal(148.50m, result.Quote.PreviousClose);
        Assert.Equal(1.18m, result.Quote.PercentChange);
        Assert.Equal(1720000000L, result.Quote.QuoteTimestampUnix);
        Assert.Equal("USD", result.Quote.Currency);
        Assert.Equal("USD", result.Quote.EstimateCurrency);
        Assert.Equal("US", result.Quote.Country);
        Assert.Equal("NASDAQ", result.Quote.Exchange);
        Assert.Equal("REGULAR", result.Quote.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_CachesProfileAndMarketStatus()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("/quote", """{"c":150.25,"o":149.75,"pc":148.50,"dp":1.18,"t":1720000000}""");
        handler.EnqueueJson("/stock/profile2", """{"currency":"USD","estimateCurrency":"USD","country":"US","exchange":"NASDAQ"}""");
        handler.EnqueueJson("/stock/market-status", """{"isOpen":true,"session":"regular"}""");
        handler.EnqueueJson("/quote", """{"c":151.00,"o":150.50,"pc":148.50,"dp":1.68,"t":1720000300}""");

        var service = CreateService(handler, memoryCache);

        var first = await service.GetQuoteAsync("AAPL");
        var second = await service.GetQuoteAsync("AAPL");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, handler.GetCallCount("/quote"));
        Assert.Equal(1, handler.GetCallCount("/stock/profile2"));
        Assert.Equal(1, handler.GetCallCount("/stock/market-status"));
    }

    [Fact]
    public async Task GetQuoteAsync_MarketStatusFailure_FallsBackToUnknown()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("/quote", """{"c":150.25,"o":149.75,"pc":148.50,"dp":1.18,"t":1720000000}""");
        handler.EnqueueJson("/stock/profile2", """{"currency":"USD","estimateCurrency":"USD","country":"US","exchange":"NASDAQ"}""");
        handler.EnqueueResponse("/stock/market-status", new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote);
        Assert.Equal("UNKNOWN", result.Quote!.MarketState);
    }

    [Fact]
    public async Task GetQuoteAsync_EmptyQuoteResponse_ReturnsBadGateway()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse("/quote", new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal("Quote provider returned an empty response.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_ZeroCurrentPrice_ReturnsBadGateway()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("/quote", """{"c":0,"o":149.75,"pc":148.50,"dp":1.18,"t":1720000000}""");

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal("Quote provider returned an invalid current price.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_Http429_ReturnsRateLimitError()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse("/quote", new HttpResponseMessage((HttpStatusCode)StatusCodes.Status429TooManyRequests));

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal("Quote provider rate limit exceeded.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_HttpFailure_ReturnsBadGateway()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse("/quote", new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal("Quote provider request failed.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_Timeout_ReturnsGatewayTimeout()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler
        {
            ExceptionFactory = _ => new TaskCanceledException("timeout")
        };

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, result.StatusCode);
        Assert.Equal("Quote provider request timed out.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_ReturnsDayHighAndLow_WhenPresent()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("/quote", """{"c":150.25,"o":149.75,"pc":148.50,"dp":1.18,"t":1720000000,"h":152.00,"l":148.10}""");
        handler.EnqueueJson("/stock/profile2", """{"currency":"USD","estimateCurrency":"USD","country":"US","exchange":"NASDAQ"}""");
        handler.EnqueueJson("/stock/market-status", """{"isOpen":true,"session":"regular"}""");

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote);
        Assert.Equal(152.00m, result.Quote!.DayHigh);
        Assert.Equal(148.10m, result.Quote.DayLow);
    }

    [Fact]
    public async Task GetQuoteAsync_DayHighAndLow_NullWhenAbsent()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("/quote", """{"c":150.25,"o":149.75,"pc":148.50,"dp":1.18,"t":1720000000}""");
        handler.EnqueueJson("/stock/profile2", """{"currency":"USD","estimateCurrency":"USD","country":"US","exchange":"NASDAQ"}""");
        handler.EnqueueJson("/stock/market-status", """{"isOpen":true,"session":"regular"}""");

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote);
        Assert.Null(result.Quote!.DayHigh);
        Assert.Null(result.Quote.DayLow);
    }

    [Fact]
    public async Task GetQuoteAsync_DayHighAndLow_NullWhenZero()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("/quote", """{"c":150.25,"o":149.75,"pc":148.50,"dp":1.18,"t":1720000000,"h":0,"l":0}""");
        handler.EnqueueJson("/stock/profile2", """{"currency":"USD","estimateCurrency":"USD","country":"US","exchange":"NASDAQ"}""");
        handler.EnqueueJson("/stock/market-status", """{"isOpen":true,"session":"regular"}""");

        var service = CreateService(handler, memoryCache);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Quote);
        Assert.Null(result.Quote!.DayHigh);
        Assert.Null(result.Quote.DayLow);
    }

    [Fact]
    public async Task GetQuoteAsync_MissingApiKey_ReturnsConfigurationError()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHttpMessageHandler();
        var service = CreateService(handler, memoryCache, apiKey: null);

        var result = await service.GetQuoteAsync("AAPL");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal("Finnhub API key is not configured.", result.ErrorMessage);
        Assert.Equal(0, handler.TotalCalls);
    }

    private static FinnhubQuoteService CreateService(
        HttpMessageHandler handler,
        IMemoryCache memoryCache,
        string? apiKey = "test-key")
    {
        return new FinnhubQuoteService(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://finnhub.io/api/v1/"),
                Timeout = TimeSpan.FromSeconds(10)
            },
            memoryCache,
            Options.Create(new FinnhubOptions { ApiKey = apiKey }),
            NullLogger<FinnhubQuoteService>.Instance);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _callCounts = new(StringComparer.OrdinalIgnoreCase);

        public Func<HttpRequestMessage, Exception>? ExceptionFactory { get; init; }

        public int TotalCalls => _callCounts.Values.Sum();

        public void EnqueueJson(string path, string json)
        {
            EnqueueResponse(path, new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        public void EnqueueResponse(string path, HttpResponseMessage response)
        {
            if (!_responses.TryGetValue(path, out var queue))
            {
                queue = new Queue<Func<HttpResponseMessage>>();
                _responses[path] = queue;
            }

            queue.Enqueue(() => response);
        }

        public int GetCallCount(string path) => _callCounts.TryGetValue(path, out var count) ? count : 0;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ExceptionFactory is not null)
            {
                throw ExceptionFactory(request);
            }

            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            {
                path = path["/api/v1".Length..];
            }

            _callCounts[path] = GetCallCount(path) + 1;

            if (_responses.TryGetValue(path, out var queue) && queue.Count > 0)
            {
                return Task.FromResult(queue.Dequeue().Invoke());
            }

            throw new InvalidOperationException($"No stubbed response configured for path '{path}'.");
        }
    }
}
