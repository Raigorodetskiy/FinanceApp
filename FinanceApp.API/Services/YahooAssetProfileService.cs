using System.Net;
using System.Text.Json;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Http;

namespace FinanceApp.API.Services;

public sealed record YahooAssetProfileLookupResult(
    string? Sector,
    string? SectorKey,
    string? Industry,
    string? IndustryKey,
    bool RateLimited,
    bool Failed,
    string? Diagnostics,
    string Source,
    StockMetadataEnrichmentConfidence Confidence);

public interface IYahooAssetProfileService
{
    Task<YahooAssetProfileLookupResult> GetAssetProfileAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class YahooAssetProfileService : IYahooAssetProfileService
{
    private const string Modules = "assetProfile";
    private readonly IYahooRequestCoordinator _requestCoordinator;
    private readonly IYahooSessionService _sessionService;

    public YahooAssetProfileService(IYahooRequestCoordinator requestCoordinator, IYahooSessionService sessionService)
    {
        _requestCoordinator = requestCoordinator;
        _sessionService = sessionService;
    }

    public async Task<YahooAssetProfileLookupResult> GetAssetProfileAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var sessionResult = await _sessionService.GetSessionAsync(cancellationToken);
        if (!sessionResult.IsSuccess || sessionResult.Session is null)
        {
            return new YahooAssetProfileLookupResult(
                null, null, null, null,
                sessionResult.FailureCategory == YahooSessionFailureCategory.RateLimited,
                true,
                sessionResult.ErrorMessage,
                "Yahoo Finance",
                StockMetadataEnrichmentConfidence.None);
        }

        var url = BuildUrl(symbol, sessionResult.Session.Crumb);
        var response = await _requestCoordinator.GetAsync(
            url,
            $"asset-profile:{symbol}",
            new YahooRequestExecutionOptions(3, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(20), null, ContainsSensitiveQueryParameters: true),
            cancellationToken,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cookie"] = sessionResult.Session.CookieHeader
            });

        if (response.IsRateLimited)
        {
            return new YahooAssetProfileLookupResult(null, null, null, null, true, false, "Rate limited", "Yahoo Finance", StockMetadataEnrichmentConfidence.None);
        }

        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(response.Content))
        {
            return new YahooAssetProfileLookupResult(
                null, null, null, null,
                false,
                true,
                $"HTTP {(int)response.StatusCode}",
                "Yahoo Finance",
                StockMetadataEnrichmentConfidence.None);
        }

        try
        {
            using var json = JsonDocument.Parse(response.Content);
            if (!TryGetResultRoot(json.RootElement, out var resultRoot))
            {
                return new YahooAssetProfileLookupResult(null, null, null, null, false, true, "Malformed response", "Yahoo Finance", StockMetadataEnrichmentConfidence.None);
            }

            var assetProfile = GetModule(resultRoot, "assetProfile");
            var sector = ReadString(assetProfile, "sector");
            var sectorKey = ReadString(assetProfile, "sectorKey");
            var industry = ReadString(assetProfile, "industry");
            var industryKey = ReadString(assetProfile, "industryKey");

            return new YahooAssetProfileLookupResult(
                sector,
                sectorKey,
                industry,
                industryKey,
                false,
                false,
                null,
                "Yahoo Finance",
                string.IsNullOrWhiteSpace(industry) ? StockMetadataEnrichmentConfidence.Low : StockMetadataEnrichmentConfidence.Medium);
        }
        catch (JsonException)
        {
            return new YahooAssetProfileLookupResult(null, null, null, null, false, true, "Invalid JSON", "Yahoo Finance", StockMetadataEnrichmentConfidence.None);
        }
    }

    private static string BuildUrl(string symbol, string crumb) =>
        $"https://query2.finance.yahoo.com/v10/finance/quoteSummary/{Uri.EscapeDataString(symbol)}?modules={Uri.EscapeDataString(Modules)}&crumb={Uri.EscapeDataString(crumb)}";

    private static bool TryGetResultRoot(JsonElement root, out JsonElement resultRoot)
    {
        resultRoot = default;
        if (!root.TryGetProperty("quoteSummary", out var quoteSummary)
            || !quoteSummary.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array
            || result.GetArrayLength() == 0)
        {
            return false;
        }

        resultRoot = result[0];
        return true;
    }

    private static JsonElement GetModule(JsonElement root, string moduleName)
        => root.TryGetProperty(moduleName, out var module) && module.ValueKind == JsonValueKind.Object
            ? module
            : default;

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
    }
}
