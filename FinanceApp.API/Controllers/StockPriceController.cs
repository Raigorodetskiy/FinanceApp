using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StockPriceController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public StockPriceController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("rate/eurusd")]
    public async Task<IActionResult> GetEurUsdRate()
    {
        var client = _httpClientFactory.CreateClient();
        var url = "https://api.frankfurter.app/latest?from=USD&to=EUR";
        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, "Could not get EUR/USD rate");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("rates", out var rates) ||
            !rates.TryGetProperty("EUR", out var eurProp))
            return StatusCode(502, "Unexpected response from frankfurter.app");

        var usdToEur = eurProp.GetDecimal();
        var eurUsd = 1m / usdToEur;

        return Ok(new { eurUsd });
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetPrice(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !System.Text.RegularExpressions.Regex.IsMatch(symbol, @"^[A-Za-z0-9.\-]{1,20}$"))
            return BadRequest("Invalid symbol");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?interval=1d&range=1d";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, $"Yahoo Finance error: {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var meta = root
                .GetProperty("chart")
                .GetProperty("result")[0]
                .GetProperty("meta");

            decimal currentPrice;
            if (meta.TryGetProperty("regularMarketPrice", out var rmp))
                currentPrice = rmp.GetDecimal();
            else if (meta.TryGetProperty("previousClose", out var pc))
                currentPrice = pc.GetDecimal();
            else
                return StatusCode(502, "Could not parse price from Yahoo Finance");

            decimal previousClose = meta.TryGetProperty("chartPreviousClose", out var cpc)
                ? cpc.GetDecimal()
                : meta.TryGetProperty("previousClose", out var pc2) ? pc2.GetDecimal() : currentPrice;

            var change = currentPrice - previousClose;
            var percentChange = previousClose != 0 ? (change / previousClose) * 100m : 0m;

            return Ok(new { symbol, currentPrice, change, percentChange });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error fetching price: {ex.Message}");
        }
    }
}
