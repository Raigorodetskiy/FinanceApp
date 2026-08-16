using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Provides S&amp;P 500 constituents from a versioned curated snapshot bundled with the repository.
/// Used because the official index owner (S&amp;P Dow Jones Indices) does not provide a free,
/// stable public runtime endpoint without a commercial data license.
/// </summary>
public sealed class Sp500ConstituentsProvider : ISp500IndexConstituentsProvider
{
    public const string CuratedProviderName = "S&P Dow Jones Indices (curated snapshot)";
    private const string SnapshotRelativePath = "Data/index-constituents/sp500.curated.snapshot.json";

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<Sp500ConstituentsProvider> _logger;
    private readonly string _baseDirectory;

    public Sp500ConstituentsProvider(
        IWebHostEnvironment environment,
        ILogger<Sp500ConstituentsProvider> logger,
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
                    "Curated snapshot S&P 500 не найден в приложении.");
            }

            var json = await File.ReadAllTextAsync(snapshotPath, cancellationToken);
            var snapshot = JsonSerializer.Deserialize<Sp500CuratedSnapshot>(json);
            if (snapshot is null)
            {
                return IndexConstituentsResult.Failure(
                    ProviderName,
                    "Curated snapshot S&P 500 повреждён или пуст.");
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
            _logger.LogError(ex, "Failed to load curated S&P 500 snapshot");
            return IndexConstituentsResult.Failure(
                ProviderName,
                "Не удалось загрузить curated snapshot S&P 500.");
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
        Sp500CuratedSnapshot snapshot,
        out IReadOnlyList<IndexConstituentEntry> entries)
    {
        entries = Array.Empty<IndexConstituentEntry>();

        if (string.IsNullOrWhiteSpace(snapshot.SourceUrl))
            return "Curated snapshot S&P 500 не содержит sourceUrl.";

        if (snapshot.AsOfDate == default)
            return "Curated snapshot S&P 500 не содержит корректную asOfDate.";

        if (snapshot.Constituents is null || snapshot.Constituents.Count == 0)
            return "Curated snapshot S&P 500 не содержит constituents.";

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
                return "Curated snapshot S&P 500 содержит пустой ticker или companyName.";

            if (string.IsNullOrWhiteSpace(providerExchange))
                return $"Curated snapshot S&P 500 содержит пустую биржу для тикера {ticker}.";

            if (!StockExchanges.TryNormalize(providerExchange, out var normalizedExchange))
                return $"Curated snapshot S&P 500 содержит неподдерживаемую биржу '{providerExchange}' для тикера {ticker}.";

            if (isin is not null && !StockIdentifiers.IsValidIsin(isin))
                return $"Curated snapshot S&P 500 содержит некорректный ISIN для тикера {ticker}.";

            providerSymbol ??= StockExchanges.ResolveProviderSymbol(ticker, normalizedExchange);
            providerSymbol = providerSymbol?.Trim() ?? string.Empty;
            if (providerSymbol.Length == 0)
                return $"Curated snapshot S&P 500 содержит пустой providerSymbol для тикера {ticker}.";

            var uniqueKey = $"{providerSymbol}|{normalizedExchange}";
            if (!uniqueKeys.Add(uniqueKey))
                return $"Curated snapshot S&P 500 содержит дубликат identity: {uniqueKey}.";

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

    private sealed class Sp500CuratedSnapshot
    {
        [JsonPropertyName("sourceName")]
        public string SourceName { get; init; } = string.Empty;

        [JsonPropertyName("sourceUrl")]
        public string SourceUrl { get; init; } = string.Empty;

        [JsonPropertyName("asOfDate")]
        public DateTime AsOfDate { get; init; }

        [JsonPropertyName("constituents")]
        public List<Sp500ConstituentItem> Constituents { get; init; } = [];
    }

    private sealed class Sp500ConstituentItem
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
