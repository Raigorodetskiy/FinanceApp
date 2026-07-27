using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using FinanceApp.API.Services;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StockPriceController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly IStockQuoteConversionService _stockQuoteConversionService;

    public StockPriceController(
        IHttpClientFactory httpClientFactory,
        IExchangeRateService exchangeRateService,
        IStockQuoteConversionService stockQuoteConversionService)
    {
        _httpClientFactory = httpClientFactory;
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
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

            var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{symbol}?interval=1m&range=1d";
            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, $"Yahoo Finance error: {(int)response.StatusCode}");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            var meta = root
                .GetProperty("chart")
                .GetProperty("result")[0]
                .GetProperty("meta");

            decimal currentPrice = meta.TryGetProperty("regularMarketPrice", out var rmp)
                ? rmp.GetDecimal()
                : meta.TryGetProperty("previousClose", out var pc0) ? pc0.GetDecimal() : 0m;

            if (currentPrice == 0m)
                return StatusCode(502, "Could not parse price from Yahoo Finance");

            decimal previousClose = meta.TryGetProperty("chartPreviousClose", out var cpc)
                ? cpc.GetDecimal()
                : meta.TryGetProperty("previousClose", out var pc2) ? pc2.GetDecimal() : currentPrice;

            var change = currentPrice - previousClose;
            var percentChange = previousClose != 0 ? (change / previousClose) * 100m : 0m;
            var quoteCurrency = meta.TryGetProperty("currency", out var currencyProp) ? currencyProp.GetString() : null;
            var financialCurrency = meta.TryGetProperty("financialCurrency", out var financialCurrencyProp) ? financialCurrencyProp.GetString() : null;

            var marketState = "CLOSED";
            if (meta.TryGetProperty("currentTradingPeriod", out var tradingPeriod))
            {
                var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                long preStart = 0, preEnd = 0, regStart = 0, regEnd = 0, postStart = 0, postEnd = 0;

                if (tradingPeriod.TryGetProperty("pre", out var pre))
                {
                    preStart = pre.GetProperty("start").GetInt64();
                    preEnd = pre.GetProperty("end").GetInt64();
                }
                if (tradingPeriod.TryGetProperty("regular", out var reg))
                {
                    regStart = reg.GetProperty("start").GetInt64();
                    regEnd = reg.GetProperty("end").GetInt64();
                }
                if (tradingPeriod.TryGetProperty("post", out var post))
                {
                    postStart = post.GetProperty("start").GetInt64();
                    postEnd = post.GetProperty("end").GetInt64();
                }

                if (nowUnix >= regStart && nowUnix < regEnd)
                    marketState = "REGULAR";
                else if (nowUnix >= preStart && nowUnix < preEnd)
                    marketState = "PRE";
                else if (nowUnix >= postStart && nowUnix < postEnd)
                    marketState = "POST";
                else
                    marketState = "CLOSED";
            }

            var conversionContext = await _stockQuoteConversionService.GetConversionContextAsync(
                quoteCurrency,
                financialCurrency,
                cancellationToken);

            return Ok(_stockQuoteConversionService.BuildQuoteResponse(
                symbol,
                currentPrice,
                previousClose,
                percentChange,
                marketState,
                conversionContext));
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error fetching price: {ex.Message}");
        }
    }
}
