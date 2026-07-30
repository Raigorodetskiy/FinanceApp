using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockPriceControllerTests
{
    [Fact]
    public async Task GetPrice_Nyse_UsesFinnhubAndPreservesResponseContract()
    {
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Success(new FinnhubQuoteData(
                "AAPL",
                105m,
                103m,
                100m,
                5m,
                1720000000,
                "USD",
                "USD",
                "US",
                "NASDAQ",
                "REGULAR")),
            yahooResult: YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Should not be called"));

        var actionResult = await controller.GetPrice("AAPL", StockExchanges.Nyse);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<StockQuoteResponse>(ok.Value);

        Assert.Equal("AAPL", response.Symbol);
        Assert.Equal(105m, response.RawCurrentPrice);
        Assert.Equal(100m, response.RawPreviousClose);
        Assert.Equal(5m, response.RawChange);
        Assert.Equal("USD", response.Currency);
        Assert.Equal("USD", response.FinancialCurrency);
        Assert.Equal("USD", response.NormalizedQuoteCurrency);
        Assert.Equal(1m, response.QuoteUnitMultiplier);
        Assert.Equal(105m, response.NormalizedCurrentPrice);
        Assert.Equal(100m, response.NormalizedPreviousClose);
        Assert.Equal(5m, response.NormalizedChange);
        Assert.Equal(95.55m, response.CurrentPriceEur);
        Assert.Equal(4.55m, response.ChangeEur);
        Assert.Equal(5m, response.PercentChange);
        Assert.Equal("REGULAR", response.MarketState);
        Assert.Equal("LAST", response.PriceSession);
        Assert.Equal(0.91m, response.RateToEur);
        Assert.Equal("stub", response.RateSource);
        Assert.Null(response.ConversionWarning);
    }

    [Fact]
    public async Task GetPrice_Nyse_RoutesFinnhubNotYahoo()
    {
        var yahooService = new TrackingYahooQuoteService(
            YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Should not be called"));
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Success(new FinnhubQuoteData(
                "AAPL", 105m, 103m, 100m, 5m, 0, "USD", "USD", "US", "NASDAQ", "REGULAR")),
            yahooService: yahooService);

        await controller.GetPrice("AAPL", StockExchanges.Nyse);

        Assert.Equal(0, yahooService.CallCount);
    }

    [Fact]
    public async Task GetPrice_Frankfurt_RoutesYahooNotFinnhub()
    {
        var finnhubService = new TrackingFinnhubQuoteService(
            FinnhubQuoteResult.Failure(StatusCodes.Status502BadGateway, "Should not be called"));
        var controller = CreateController(
            finnhubService: finnhubService,
            yahooResult: YahooQuoteResult.Success(new YahooQuoteData(
                "RHM.DE", 520m, 514m, 1.17m, "EUR", "EUR", "CLOSED")));

        var actionResult = await controller.GetPrice("RHM.DE", StockExchanges.Frankfurt);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<StockQuoteResponse>(ok.Value);
        Assert.Equal("RHM.DE", response.Symbol);
        Assert.Equal(520m, response.RawCurrentPrice);
        Assert.Equal(0, finnhubService.CallCount);
    }

    [Fact]
    public async Task GetPrice_NullExchange_DefaultsToNyseViaBehavior()
    {
        // Null/empty exchange defaults to NYSE (backward compatibility)
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Success(new FinnhubQuoteData(
                "AAPL", 105m, 103m, 100m, 5m, 0, "USD", "USD", "US", "NASDAQ", "REGULAR")),
            yahooResult: YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Should not be called"));

        var actionResult = await controller.GetPrice("AAPL", null);

        Assert.IsType<OkObjectResult>(actionResult);
    }

    [Fact]
    public async Task GetPrice_UnsupportedExchange_ReturnsBadRequest()
    {
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Failure(StatusCodes.Status502BadGateway, "Not called"),
            yahooResult: YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Not called"));

        var actionResult = await controller.GetPrice("AAPL", "UNKNOWN_EXCHANGE");

        var bad = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.Equal("Unsupported exchange.", bad.Value);
    }

    [Fact]
    public async Task GetPrice_FinnhubProviderError_ReturnsErrorStatus()
    {
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider request failed."),
            yahooResult: YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Should not be called"));

        var actionResult = await controller.GetPrice("AAPL", StockExchanges.Nyse);

        var result = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal("Quote provider request failed.", result.Value);
    }

    [Fact]
    public async Task GetPrice_YahooProviderError_ReturnsErrorStatus()
    {
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Failure(StatusCodes.Status502BadGateway, "Should not be called"),
            yahooResult: YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider request failed."));

        var actionResult = await controller.GetPrice("RHM.DE", StockExchanges.Frankfurt);

        var result = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal("Quote provider request failed.", result.Value);
    }

    [Fact]
    public async Task GetPrice_Frankfurt_PreservesConversionContract()
    {
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Failure(StatusCodes.Status502BadGateway, "Not called"),
            yahooResult: YahooQuoteResult.Success(new YahooQuoteData(
                "RHM.DE", 520m, 514m, 1.17m, "EUR", "EUR", "REGULAR")));

        var actionResult = await controller.GetPrice("RHM.DE", StockExchanges.Frankfurt);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<StockQuoteResponse>(ok.Value);
        Assert.Equal("RHM.DE", response.Symbol);
        Assert.Equal(520m, response.RawCurrentPrice);
        Assert.Equal(1.17m, response.PercentChange);
        Assert.Equal("EUR", response.Currency);
        Assert.Equal("REGULAR", response.MarketState);
        Assert.Equal("REGULAR", response.PriceSession);
        // EUR → EUR rate = 1.0, so CurrentPriceEur == CurrentPrice
        Assert.Equal(520m, response.CurrentPriceEur);
    }

    private static StockPriceController CreateController(
        FinnhubQuoteResult? finnhubResult = null,
        YahooQuoteResult? yahooResult = null,
        IFinnhubQuoteService? finnhubService = null,
        IYahooQuoteService? yahooService = null)
    {
        var exchangeRate = new StubExchangeRateService(("USD", 0.91m));
        return new StockPriceController(
            finnhubService ?? new StubFinnhubQuoteService(
                finnhubResult ?? FinnhubQuoteResult.Failure(StatusCodes.Status502BadGateway, "not configured")),
            yahooService ?? new StubYahooQuoteService(
                yahooResult ?? YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "not configured")),
            exchangeRate,
            new StockQuoteConversionService(exchangeRate))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class StubFinnhubQuoteService : IFinnhubQuoteService
    {
        private readonly FinnhubQuoteResult _result;

        public StubFinnhubQuoteService(FinnhubQuoteResult result) => _result = result;

        public Task<FinnhubQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class StubYahooQuoteService : IYahooQuoteService
    {
        private readonly YahooQuoteResult _result;

        public StubYahooQuoteService(YahooQuoteResult result) => _result = result;

        public Task<YahooQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class TrackingFinnhubQuoteService : IFinnhubQuoteService
    {
        private readonly FinnhubQuoteResult _result;
        public int CallCount { get; private set; }

        public TrackingFinnhubQuoteService(FinnhubQuoteResult result) => _result = result;

        public Task<FinnhubQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class TrackingYahooQuoteService : IYahooQuoteService
    {
        private readonly YahooQuoteResult _result;
        public int CallCount { get; private set; }

        public TrackingYahooQuoteService(YahooQuoteResult result) => _result = result;

        public Task<YahooQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubExchangeRateService : IExchangeRateService
    {
        private readonly Dictionary<string, decimal?> _rates;

        public StubExchangeRateService(params (string Currency, decimal? RateToEur)[] configuredRates)
        {
            _rates = configuredRates.ToDictionary(x => x.Currency, x => x.RateToEur, StringComparer.OrdinalIgnoreCase);
        }

        public Task<ExchangeRateResult> GetRateToEurAsync(string? sourceCurrency, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceCurrency))
            {
                return Task.FromResult(new ExchangeRateResult(null, null, null, "stub", "missing currency"));
            }

            if (string.Equals(sourceCurrency, "EUR", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ExchangeRateResult("EUR", 1m, DateTime.UtcNow, "stub", null));
            }

            if (_rates.TryGetValue(sourceCurrency, out var rateToEur) && rateToEur.HasValue)
            {
                return Task.FromResult(new ExchangeRateResult(sourceCurrency.ToUpperInvariant(), rateToEur.Value, DateTime.UtcNow, "stub", null));
            }

            return Task.FromResult(new ExchangeRateResult(sourceCurrency.ToUpperInvariant(), null, null, "stub", "rate unavailable"));
        }
    }
}
