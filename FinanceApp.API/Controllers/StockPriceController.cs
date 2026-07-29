using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FinanceApp.API.Services;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StockPriceController : ControllerBase
{
    private readonly IFinnhubQuoteService _finnhubQuoteService;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly IStockQuoteConversionService _stockQuoteConversionService;

    public StockPriceController(
        IFinnhubQuoteService finnhubQuoteService,
        IExchangeRateService exchangeRateService,
        IStockQuoteConversionService stockQuoteConversionService)
    {
        _finnhubQuoteService = finnhubQuoteService;
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
    public async Task<IActionResult> GetPrice(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !System.Text.RegularExpressions.Regex.IsMatch(symbol, @"^[A-Za-z0-9.\-]{1,20}$"))
            return BadRequest("Invalid symbol");

        try
        {
            var quoteResult = await _finnhubQuoteService.GetQuoteAsync(symbol, cancellationToken);
            if (!quoteResult.IsSuccess || quoteResult.Quote is null)
            {
                return StatusCode(quoteResult.StatusCode, quoteResult.ErrorMessage ?? "Could not fetch current quote.");
            }

            var quote = quoteResult.Quote;

            var conversionContext = await _stockQuoteConversionService.GetConversionContextAsync(
                quote.Currency,
                quote.EstimateCurrency,
                cancellationToken);

            return Ok(_stockQuoteConversionService.BuildQuoteResponse(
                symbol,
                quote.CurrentPrice,
                quote.PreviousClose,
                quote.PercentChange,
                quote.MarketState,
                conversionContext));
        }
        catch (Exception)
        {
            return StatusCode(500, "Error fetching price.");
        }
    }
}
