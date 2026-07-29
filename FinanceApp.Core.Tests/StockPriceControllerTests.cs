using FinanceApp.API.Controllers;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockPriceControllerTests
{
    [Fact]
    public async Task GetPrice_UsesFinnhubQuoteAndPreservesResponseContract()
    {
        var controller = new StockPriceController(
            new StubFinnhubQuoteService(FinnhubQuoteResult.Success(new FinnhubQuoteData(
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
                "REGULAR"))),
            new StubExchangeRateService(("USD", 0.91m)),
            new StockQuoteConversionService(new StubExchangeRateService(("USD", 0.91m))))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var actionResult = await controller.GetPrice("AAPL");

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
        Assert.Equal(0.91m, response.RateToEur);
        Assert.Equal("stub", response.RateSource);
        Assert.Null(response.ConversionWarning);
    }

    private sealed class StubFinnhubQuoteService : IFinnhubQuoteService
    {
        private readonly FinnhubQuoteResult _result;

        public StubFinnhubQuoteService(FinnhubQuoteResult result)
        {
            _result = result;
        }

        public Task<FinnhubQuoteResult> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
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
