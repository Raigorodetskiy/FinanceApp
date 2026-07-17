using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StockPriceController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;

    public StockPriceController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["Finnhub:ApiKey"] ?? "";
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetPrice(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !System.Text.RegularExpressions.Regex.IsMatch(symbol, @"^[A-Za-z0-9.\-]{1,20}$"))
            return BadRequest("Invalid symbol");

        var client = _httpClientFactory.CreateClient();
        var url = $"https://finnhub.io/api/v1/quote?symbol={symbol}&token={_apiKey}";
        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, $"Finnhub error: {(int)response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("c", out var cProp) ||
            !root.TryGetProperty("d", out var dProp) ||
            !root.TryGetProperty("dp", out var dpProp))
            return StatusCode(502, "Unexpected response from Finnhub");

        var currentPrice = cProp.GetDecimal();
        var change = dProp.GetDecimal();
        var percentChange = dpProp.GetDecimal();

        return Ok(new { symbol, currentPrice, change, percentChange });
    }
}
