using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StockPriceController : ControllerBase
{
    private readonly IFinnhubQuoteService _finnhubQuoteService;
    private readonly IYahooQuoteService _yahooQuoteService;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly IStockQuoteConversionService _stockQuoteConversionService;

    public StockPriceController(
        IFinnhubQuoteService finnhubQuoteService,
        IYahooQuoteService yahooQuoteService,
        IExchangeRateService exchangeRateService,
        IStockQuoteConversionService stockQuoteConversionService)
    {
        _finnhubQuoteService = finnhubQuoteService;
        _yahooQuoteService = yahooQuoteService;
        _exchangeRateService = exchangeRateService;
        _stockQuoteConversionService = stockQuoteConversionService;
    }

    [HttpGet("rate/eurusd")]
    public async Task<IActionResult> GetEurUsdRate(CancellationToken cancellationToken = default)
    {
        var rate = await _exchangeRateService.GetRateToEurAsync("USD", cancellationToken);
        if (rate.RateToEur is not { } usdToEur || usdToEur == 0m)
        {
            return StatusCode(502, rate.Error ?? "Could not get EUR/USD rate");
        }

        var eurUsd = 1m / usdToEur;

        return Ok(new { eurUsd });
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetPrice(
        string symbol,
        [FromQuery] string? exchange,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !System.Text.RegularExpressions.Regex.IsMatch(symbol, @"^[A-Za-z0-9.\-]{1,20}$"))
            return BadRequest("Invalid symbol");

        if (!StockExchanges.TryNormalize(exchange, out var normalizedExchange))
            return BadRequest("Unsupported exchange.");

        try
        {
            string? currency;
            string? estimateCurrency;
            decimal currentPrice;
            decimal previousClose;
            decimal percentChange;
            string marketState;

            if (normalizedExchange == StockExchanges.Frankfurt)
            {
                var quoteResult = await _yahooQuoteService.GetQuoteAsync(symbol, cancellationToken);
                if (!quoteResult.IsSuccess || quoteResult.Quote is null)
                {
                    return StatusCode(quoteResult.StatusCode, quoteResult.ErrorMessage ?? "Could not fetch current quote.");
                }

                var quote = quoteResult.Quote;
                currency = quote.Currency;
                estimateCurrency = quote.EstimateCurrency;
                currentPrice = quote.CurrentPrice;
                previousClose = quote.PreviousClose;
                percentChange = quote.PercentChange;
                marketState = quote.MarketState;
            }
            else
            {
                var quoteResult = await _finnhubQuoteService.GetQuoteAsync(symbol, cancellationToken);
                if (!quoteResult.IsSuccess || quoteResult.Quote is null)
                {
                    return StatusCode(quoteResult.StatusCode, quoteResult.ErrorMessage ?? "Could not fetch current quote.");
                }

                var quote = quoteResult.Quote;
                currency = quote.Currency;
                estimateCurrency = quote.EstimateCurrency;
                currentPrice = quote.CurrentPrice;
                previousClose = quote.PreviousClose;
                percentChange = quote.PercentChange;
                marketState = quote.MarketState;
            }

            var conversionContext = await _stockQuoteConversionService.GetConversionContextAsync(
                currency,
                estimateCurrency,
                cancellationToken);

            return Ok(_stockQuoteConversionService.BuildQuoteResponse(
                symbol,
                currentPrice,
                previousClose,
                percentChange,
                marketState,
                conversionContext));
        }
        catch (Exception)
        {
            return StatusCode(500, "Error fetching price.");
        }
    }
}
