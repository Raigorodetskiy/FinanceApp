using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StockPriceController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public StockPriceController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet("{symbol}")]
    public async Task<ActionResult<StockPriceResponse>> GetPrice(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequest(new { message = "Symbol is required." });
        }

        var apiKey = _configuration["Finnhub:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Finnhub API key is not configured." });
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var encodedSymbol = Uri.EscapeDataString(normalizedSymbol);
        var requestUri = $"https://finnhub.io/api/v1/quote?symbol={encodedSymbol}&token={Uri.EscapeDataString(apiKey)}";

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(requestUri);
        if (!response.IsSuccessStatusCode)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = $"Failed to fetch stock price from Finnhub. Status code: {(int)response.StatusCode}." });
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        if (!TryReadDecimal(root, "c", out var currentPrice) ||
            !TryReadDecimal(root, "d", out var change) ||
            !TryReadDecimal(root, "dp", out var percentChange))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Finnhub returned an invalid quote payload." });
        }

        return Ok(new StockPriceResponse(normalizedSymbol, currentPrice, change, percentChange));
    }

    private static bool TryReadDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        if (!element.TryGetProperty(propertyName, out var jsonValue))
        {
            return false;
        }

        if (jsonValue.ValueKind == JsonValueKind.Number && jsonValue.TryGetDecimal(out var number))
        {
            value = number;
            return true;
        }

        return false;
    }

    public record StockPriceResponse(string Symbol, decimal CurrentPrice, decimal Change, decimal PercentChange);
}
