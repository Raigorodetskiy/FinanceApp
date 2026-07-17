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

        var encodedSymbol = Uri.EscapeDataString(symbol.Trim().ToUpperInvariant());
        var requestUri = $"https://finnhub.io/api/v1/quote?symbol={encodedSymbol}&token={Uri.EscapeDataString(apiKey)}";

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(requestUri);
        if (!response.IsSuccessStatusCode)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Failed to fetch stock price." });
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        var currentPrice = ReadDecimal(root, "c");
        var change = ReadDecimal(root, "d");
        var percentChange = ReadDecimal(root, "dp");

        return Ok(new StockPriceResponse(encodedSymbol, currentPrice, change, percentChange));
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0m;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return 0m;
    }

    public record StockPriceResponse(string Symbol, decimal CurrentPrice, decimal Change, decimal PercentChange);
}
