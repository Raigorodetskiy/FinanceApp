using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Provides DJIA constituents from a versioned curated snapshot bundled with the repository.
/// Used because the official index owner does not provide a free, stable public runtime endpoint.
/// </summary>
public sealed class DowJonesIndustrialAverageConstituentsProvider : IIndexConstituentsProvider
{
    public const string CuratedProviderName = "S&P Dow Jones Indices (curated snapshot)";
    private const string SnapshotRelativePath = "Data/index-constituents/djia.curated.snapshot.json";

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DowJonesIndustrialAverageConstituentsProvider> _logger;

    public DowJonesIndustrialAverageConstituentsProvider(
        IWebHostEnvironment environment,
        ILogger<DowJonesIndustrialAverageConstituentsProvider> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public string ProviderName => CuratedProviderName;

    public Task<IndexConstituentsResult> GetConstituentsAsync(
        MarketIndex index,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshotPath = Path.Combine(_environment.ContentRootPath, SnapshotRelativePath);
            if (!File.Exists(snapshotPath))
            {
                return Task.FromResult(IndexConstituentsResult.Failure(
                    ProviderName,
                    "Curated snapshot DJIA не найден в приложении."));
            }

            var json = File.ReadAllText(snapshotPath);
            var snapshot = JsonSerializer.Deserialize<DjiaCuratedSnapshot>(json);
            if (snapshot is null)
            {
                return Task.FromResult(IndexConstituentsResult.Failure(
                    ProviderName,
                    "Curated snapshot DJIA повреждён или пуст."));
            }

            var validationError = Validate(snapshot, out var entries);
            if (validationError is not null)
            {
                return Task.FromResult(IndexConstituentsResult.Failure(ProviderName, validationError));
            }

            return Task.FromResult(new IndexConstituentsResult(
                Status: IndexConstituentsStatus.Success,
                ProviderName: ProviderName,
                FetchedAt: DateTime.UtcNow,
                Constituents: entries,
                Message: null,
                AsOfDate: snapshot.AsOfDate,
                SourceUrl: snapshot.SourceUrl,
                IsCuratedSnapshot: true,
                IsStale: false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load curated DJIA snapshot");
            return Task.FromResult(IndexConstituentsResult.Failure(
                ProviderName,
                "Не удалось загрузить curated snapshot DJIA."));
        }
    }

    private static string? Validate(
        DjiaCuratedSnapshot snapshot,
        out IReadOnlyList<IndexConstituentEntry> entries)
    {
        entries = Array.Empty<IndexConstituentEntry>();

        if (string.IsNullOrWhiteSpace(snapshot.SourceUrl))
            return "Curated snapshot DJIA не содержит sourceUrl.";

        if (snapshot.AsOfDate == default)
            return "Curated snapshot DJIA не содержит корректную asOfDate.";

        if (snapshot.Constituents is null || snapshot.Constituents.Count == 0)
            return "Curated snapshot DJIA не содержит constituents.";

        var normalizedEntries = new List<IndexConstituentEntry>(snapshot.Constituents.Count);
        var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in snapshot.Constituents)
        {
            var ticker = item.Ticker?.Trim().ToUpperInvariant();
            var companyName = item.CompanyName?.Trim();
            var providerExchange = item.Exchange?.Trim();
            var providerSymbol = item.ProviderSymbol?.Trim();
            var isin = StockIdentifiers.Normalize(item.Isin);

            if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(companyName))
                return "Curated snapshot DJIA содержит пустой ticker или companyName.";

            if (string.IsNullOrWhiteSpace(providerExchange))
                return $"Curated snapshot DJIA содержит пустую биржу для тикера {ticker}.";

            if (!StockExchanges.TryNormalize(providerExchange, out var normalizedExchange))
                return $"Curated snapshot DJIA содержит неподдерживаемую биржу '{providerExchange}' для тикера {ticker}.";

            if (isin is not null && !StockIdentifiers.IsValidIsin(isin))
                return $"Curated snapshot DJIA содержит некорректный ISIN для тикера {ticker}.";

            providerSymbol ??= StockExchanges.ResolveProviderSymbol(ticker, normalizedExchange);
            providerSymbol = providerSymbol.Trim();
            if (providerSymbol.Length == 0)
                return $"Curated snapshot DJIA содержит пустой providerSymbol для тикера {ticker}.";

            var uniqueKey = $"{providerSymbol}|{normalizedExchange}";
            if (!uniqueKeys.Add(uniqueKey))
                return $"Curated snapshot DJIA содержит дубликат identity: {uniqueKey}.";

            normalizedEntries.Add(new IndexConstituentEntry(
                ProviderSymbol: providerSymbol,
                Ticker: ticker,
                CompanyName: companyName,
                ProviderExchange: normalizedExchange,
                Isin: isin));
        }

        entries = normalizedEntries;
        return null;
    }

    private sealed class DjiaCuratedSnapshot
    {
        [JsonPropertyName("sourceName")]
        public string SourceName { get; init; } = string.Empty;

        [JsonPropertyName("sourceUrl")]
        public string SourceUrl { get; init; } = string.Empty;

        [JsonPropertyName("asOfDate")]
        public DateTime AsOfDate { get; init; }

        [JsonPropertyName("constituents")]
        public List<DjiaConstituentItem> Constituents { get; init; } = [];
    }

    private sealed class DjiaConstituentItem
    {
        [JsonPropertyName("ticker")]
        public string Ticker { get; init; } = string.Empty;

        [JsonPropertyName("providerSymbol")]
        public string? ProviderSymbol { get; init; }

        [JsonPropertyName("companyName")]
        public string CompanyName { get; init; } = string.Empty;

        [JsonPropertyName("exchange")]
        public string Exchange { get; init; } = string.Empty;

        [JsonPropertyName("isin")]
        public string? Isin { get; init; }
    }
}
