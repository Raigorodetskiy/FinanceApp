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

        var actionResult = await controller.GetPrice("AAPL", StockExchanges.Nyse, null);

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

        await controller.GetPrice("AAPL", StockExchanges.Nyse, null);

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

        var actionResult = await controller.GetPrice("RHM.DE", StockExchanges.Frankfurt, null);

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

        var actionResult = await controller.GetPrice("AAPL", null, null);

        Assert.IsType<OkObjectResult>(actionResult);
    }

    [Fact]
    public async Task GetPrice_UnsupportedExchange_ReturnsBadRequest()
    {
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Failure(StatusCodes.Status502BadGateway, "Not called"),
            yahooResult: YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Not called"));

        var actionResult = await controller.GetPrice("AAPL", "UNKNOWN_EXCHANGE", null);

        var bad = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.Equal("Unsupported exchange.", bad.Value);
    }

    [Fact]
    public async Task GetPrice_FinnhubProviderError_ReturnsErrorStatus()
    {
        var controller = CreateController(
            finnhubResult: FinnhubQuoteResult.Failure(StatusCodes.Status502BadGateway, "Quote provider request failed."),
            yahooResult: YahooQuoteResult.Failure(StatusCodes.Status502BadGateway, "Should not be called"));

        var actionResult = await controller.GetPrice("AAPL", StockExchanges.Nyse, null);

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

        var actionResult = await controller.GetPrice("RHM.DE", StockExchanges.Frankfurt, null);

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

        var actionResult = await controller.GetPrice("RHM.DE", StockExchanges.Frankfurt, null);

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

    // ── Frankfurt provider-symbol resolution tests ────────────────────────────

    /// <summary>
    /// Requirement 6: Frankfurt current-quote path invokes Yahoo with the resolved .F symbol.
    /// Bare "AMZN" with exchange FRA must call Yahoo as "AMZN.F".
    /// </summary>
    [Fact]
    public async Task GetPrice_Frankfurt_BareAmznTicker_InvokesYahooWithDotFSymbol()
    {
        var yahooService = new CapturingYahooQuoteService(
            YahooQuoteResult.Success(new YahooQuoteData(
                "AMZN.F", 236.46m, 233.59m, 1.23m, "EUR", "EUR", "CLOSED")));

        await CreateController(yahooService: yahooService)
            .GetPrice("AMZN", StockExchanges.Frankfurt, null);

        Assert.Equal("AMZN.F", yahooService.LastRequestedSymbol);
    }

    /// <summary>
    /// The response Symbol field must reflect the resolved provider symbol (AMZN.F),
    /// not the bare stored ticker (AMZN), so clients can distinguish the venue.
    /// </summary>
    [Fact]
    public async Task GetPrice_Frankfurt_BareAmznTicker_ResponseSymbolIsResolvedDotF()
    {
        var yahooService = new CapturingYahooQuoteService(
            YahooQuoteResult.Success(new YahooQuoteData(
                "AMZN.F", 236.46m, 233.59m, 1.23m, "EUR", "EUR", "CLOSED")));

        var actionResult = await CreateController(yahooService: yahooService)
            .GetPrice("AMZN", StockExchanges.Frankfurt, null);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<StockQuoteResponse>(ok.Value);
        Assert.Equal("AMZN.F", response.Symbol);
    }

    /// <summary>
    /// Requirement 9 regression: the bare AMZN US/USD payload must not be selected
    /// for a stock stored as ticker=AMZN, exchange=FRA.  Before the fix, Yahoo was
    /// called with "AMZN" (US NASDAQ listing); after the fix it must be "AMZN.F".
    /// </summary>
    [Fact]
    public async Task GetPrice_Frankfurt_BareAmznTicker_DoesNotRequestUsListingFromYahoo()
    {
        var yahooService = new CapturingYahooQuoteService(
            YahooQuoteResult.Success(new YahooQuoteData(
                "AMZN.F", 236.46m, 233.59m, 1.23m, "EUR", "EUR", "CLOSED")));

        await CreateController(yahooService: yahooService)
            .GetPrice("AMZN", StockExchanges.Frankfurt, null);

        // Must NOT request bare "AMZN" (which resolves to US/NASDAQ USD data)
        Assert.NotEqual("AMZN", yahooService.LastRequestedSymbol);
        // Must request the Frankfurt-listed symbol
        Assert.Equal("AMZN.F", yahooService.LastRequestedSymbol);
    }

    /// <summary>
    /// When the caller already supplies the resolved symbol (AMZN.F), it must be used
    /// as-is – the resolver must not append ".F" a second time.
    /// </summary>
    [Fact]
    public async Task GetPrice_Frankfurt_AlreadyDotFTicker_SymbolPassedToYahooUnchanged()
    {
        var yahooService = new CapturingYahooQuoteService(
            YahooQuoteResult.Success(new YahooQuoteData(
                "AMZN.F", 236.46m, 233.59m, 1.23m, "EUR", "EUR", "CLOSED")));

        var actionResult = await CreateController(yahooService: yahooService)
            .GetPrice("AMZN.F", StockExchanges.Frankfurt, null);

        Assert.Equal("AMZN.F", yahooService.LastRequestedSymbol);
        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<StockQuoteResponse>(ok.Value);
        Assert.Equal("AMZN.F", response.Symbol);
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
            new DisabledFinanzenNetQuoteService(),
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

    private sealed class DisabledFinanzenNetQuoteService : IFinanzenNetQuoteService
    {
        public bool IsEnabled => false;

        public Task<FinanzenNetQuoteResult> GetPreMarketQuoteAsync(
            string slug,
            CancellationToken cancellationToken = default)
            => Task.FromResult(FinanzenNetQuoteResult.Failure(
                StatusCodes.Status503ServiceUnavailable, "Disabled"));
    }

    /// <summary>
    /// Yahoo quote service stub that captures the symbol argument passed to it,
    /// enabling assertions on which provider symbol was actually requested.
    /// </summary>
    private sealed class CapturingYahooQuoteService : IYahooQuoteService
    {
        private readonly YahooQuoteResult _result;

        public CapturingYahooQuoteService(YahooQuoteResult result) => _result = result;

        public string? LastRequestedSymbol { get; private set; }

        public Task<YahooQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            LastRequestedSymbol = symbol;
            return Task.FromResult(_result);
        }
    }
}
