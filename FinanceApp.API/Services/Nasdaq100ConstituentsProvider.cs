using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Provides NASDAQ-100 constituents from a versioned curated snapshot bundled with the repository.
/// Used because a free/public stable structured runtime endpoint is not available in this environment.
/// </summary>
public sealed class Nasdaq100ConstituentsProvider : INasdaq100IndexConstituentsProvider
{
    public const string CuratedProviderName = "Nasdaq Global Indexes (curated snapshot)";
    private const string SnapshotRelativePath = "Data/index-constituents/nasdaq100.curated.snapshot.json";

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<Nasdaq100ConstituentsProvider> _logger;
    private readonly string _baseDirectory;

    public Nasdaq100ConstituentsProvider(
        IWebHostEnvironment environment,
        ILogger<Nasdaq100ConstituentsProvider> logger,
        string? baseDirectoryOverride = null)
    {
        _environment = environment;
        _logger = logger;
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectoryOverride)
            ? AppContext.BaseDirectory
            : baseDirectoryOverride;
    }

    public string ProviderName => CuratedProviderName;

    public async Task<IndexConstituentsResult> GetConstituentsAsync(
        MarketIndex index,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshotPath = ResolveSnapshotPath();
            if (!File.Exists(snapshotPath))
            {
                return IndexConstituentsResult.Failure(
                    ProviderName,
                    "Curated snapshot NASDAQ-100 не найден в приложении.");
            }

            var json = await File.ReadAllTextAsync(snapshotPath, cancellationToken);
            var snapshot = JsonSerializer.Deserialize<Nasdaq100CuratedSnapshot>(json);
            if (snapshot is null)
            {
                return IndexConstituentsResult.Failure(
                    ProviderName,
                    "Curated snapshot NASDAQ-100 повреждён или пуст.");
            }

            var validationError = Validate(snapshot, out var entries);
            if (validationError is not null)
            {
                return IndexConstituentsResult.Failure(ProviderName, validationError);
            }

            return new IndexConstituentsResult(
                Status: IndexConstituentsStatus.Success,
                ProviderName: ProviderName,
                FetchedAt: DateTime.UtcNow,
                Constituents: entries,
                Message: null,
                AsOfDate: snapshot.AsOfDate,
                SourceUrl: snapshot.SourceUrl,
                IsCuratedSnapshot: true,
                IsStale: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load curated NASDAQ-100 snapshot");
            return IndexConstituentsResult.Failure(
                ProviderName,
                "Не удалось загрузить curated snapshot NASDAQ-100.");
        }
    }

    private string ResolveSnapshotPath()
    {
        var appBaseSnapshotPath = Path.GetFullPath(Path.Combine(_baseDirectory, SnapshotRelativePath));
        if (File.Exists(appBaseSnapshotPath))
        {
            return appBaseSnapshotPath;
        }

        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, SnapshotRelativePath));
    }

    private static string? Validate(
        Nasdaq100CuratedSnapshot snapshot,
        out IReadOnlyList<IndexConstituentEntry> entries)
    {
        entries = Array.Empty<IndexConstituentEntry>();

        if (string.IsNullOrWhiteSpace(snapshot.SourceUrl))
            return "Curated snapshot NASDAQ-100 не содержит sourceUrl.";

        if (snapshot.AsOfDate == default)
            return "Curated snapshot NASDAQ-100 не содержит корректную asOfDate.";

        if (snapshot.Constituents is null || snapshot.Constituents.Count == 0)
            return "Curated snapshot NASDAQ-100 не содержит constituents.";

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
                return "Curated snapshot NASDAQ-100 содержит пустой ticker или companyName.";

            if (string.IsNullOrWhiteSpace(providerExchange))
                return $"Curated snapshot NASDAQ-100 содержит пустую биржу для тикера {ticker}.";

            if (!StockExchanges.TryNormalize(providerExchange, out var normalizedExchange))
                return $"Curated snapshot NASDAQ-100 содержит неподдерживаемую биржу '{providerExchange}' для тикера {ticker}.";

            if (isin is not null && !StockIdentifiers.IsValidIsin(isin))
                return $"Curated snapshot NASDAQ-100 содержит некорректный ISIN для тикера {ticker}.";

            providerSymbol ??= StockExchanges.ResolveProviderSymbol(ticker, normalizedExchange);
            providerSymbol = providerSymbol?.Trim() ?? string.Empty;
            if (providerSymbol.Length == 0)
                return $"Curated snapshot NASDAQ-100 содержит пустой providerSymbol для тикера {ticker}.";

            var uniqueKey = $"{providerSymbol}|{normalizedExchange}";
            if (!uniqueKeys.Add(uniqueKey))
                return $"Curated snapshot NASDAQ-100 содержит дубликат identity: {uniqueKey}.";

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

    private sealed class Nasdaq100CuratedSnapshot
    {
        [JsonPropertyName("sourceName")]
        public string SourceName { get; init; } = string.Empty;

        [JsonPropertyName("sourceUrl")]
        public string SourceUrl { get; init; } = string.Empty;

        [JsonPropertyName("asOfDate")]
        public DateTime AsOfDate { get; init; }

        [JsonPropertyName("constituents")]
        public List<Nasdaq100ConstituentItem> Constituents { get; init; } = [];
    }

    private sealed class Nasdaq100ConstituentItem
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
