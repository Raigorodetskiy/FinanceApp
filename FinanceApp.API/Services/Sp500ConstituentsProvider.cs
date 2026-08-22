using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Provides S&amp;P 500 constituents from the curated CSV snapshot bundled in the repository.
/// </summary>
public sealed class Sp500ConstituentsProvider : ISp500IndexConstituentsProvider
{
    public const string CuratedProviderName = "S&P Dow Jones Indices (curated CSV snapshot)";
    private const string CsvRelativePath = "Data/index-constituents/SP500_2026-08-21.csv";
    private const string LegacySnapshotRelativePath = "Data/index-constituents/sp500.curated.snapshot.json";
    private const string ExpectedHeader = "Ticker;Company;ISIN;WKN;Sector";
    private const int ExpectedDataRowCount = 503;
    private static readonly DateTime SnapshotAsOfDateUtc = new(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);

    private static readonly HashSet<string> AllowedSectors =
    [
        "Communication Services",
        "Consumer Discretionary",
        "Consumer Staples",
        "Energy",
        "Financials",
        "Health Care",
        "Industrials",
        "Information Technology",
        "Materials",
        "Real Estate",
        "Utilities",
    ];

    private static readonly IReadOnlyDictionary<string, string> ExplicitExchangeMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["APP"] = StockExchanges.Nasdaq,
        ["ARES"] = StockExchanges.Nyse,
        ["BNY"] = StockExchanges.Nyse,
        ["CASY"] = StockExchanges.Nasdaq,
        ["CIEN"] = StockExchanges.Nyse,
        ["COHR"] = StockExchanges.Nyse,
        ["CRH"] = StockExchanges.Nyse,
        ["CVNA"] = StockExchanges.Nyse,
        ["ECHO"] = StockExchanges.Nasdaq,
        ["EME"] = StockExchanges.Nyse,
        ["FDXF"] = StockExchanges.Nyse,
        ["FERG"] = StockExchanges.Nyse,
        ["FISV"] = StockExchanges.Nasdaq,
        ["FIX"] = StockExchanges.Nyse,
        ["FLEX"] = StockExchanges.Nasdaq,
        ["FOX"] = StockExchanges.Nasdaq,
        ["HONA"] = StockExchanges.Nasdaq,
        ["HOOD"] = StockExchanges.Nasdaq,
        ["IBKR"] = StockExchanges.Nasdaq,
        ["LITE"] = StockExchanges.Nasdaq,
        ["MRSH"] = StockExchanges.Nyse,
        ["MRVL"] = StockExchanges.Nasdaq,
        ["NWS"] = StockExchanges.Nasdaq,
        ["Q"] = StockExchanges.Nyse,
        ["RDDT"] = StockExchanges.Nyse,
        ["SNDK"] = StockExchanges.Nasdaq,
        ["VEEV"] = StockExchanges.Nyse,
        ["VMRK"] = StockExchanges.Nyse,
        ["VRT"] = StockExchanges.Nyse,
    };

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
            var csvPath = ResolveCsvPath();
            if (csvPath is null || !File.Exists(csvPath))
            {
                return IndexConstituentsResult.Failure(
                    ProviderName,
                    "Curated CSV S&P 500 не найден в приложении.");
            }

            var legacyExchangeMap = await LoadLegacySnapshotExchangeMapAsync(cancellationToken);
            var (status, message, entries) = await ParseCsvAsync(csvPath, legacyExchangeMap, cancellationToken);

            return new IndexConstituentsResult(
                Status: status,
                ProviderName: ProviderName,
                FetchedAt: DateTime.UtcNow,
                Constituents: entries,
                Message: message,
                AsOfDate: SnapshotAsOfDateUtc,
                SourceUrl: "FinanceApp.Data/index-constituents/SP500_2026-08-21.csv",
                IsCuratedSnapshot: true,
                IsStale: status != IndexConstituentsStatus.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load curated S&P 500 CSV snapshot");
            return IndexConstituentsResult.Failure(
                ProviderName,
                "Не удалось загрузить curated CSV snapshot S&P 500.");
        }
    }

    private string? ResolveCsvPath()
    {
        var appBaseCsvPath = Path.GetFullPath(Path.Combine(_baseDirectory, CsvRelativePath));
        if (File.Exists(appBaseCsvPath))
        {
            return appBaseCsvPath;
        }

        var contentRootCsvPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, CsvRelativePath));
        if (File.Exists(contentRootCsvPath))
        {
            return contentRootCsvPath;
        }

        var repoCsvPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "FinanceApp.Data", "index-constituents", "SP500_2026-08-21.csv"));
        return File.Exists(repoCsvPath) ? repoCsvPath : null;
    }

    private string ResolveLegacySnapshotPath()
    {
        var appBaseSnapshotPath = Path.GetFullPath(Path.Combine(_baseDirectory, LegacySnapshotRelativePath));
        if (File.Exists(appBaseSnapshotPath))
        {
            return appBaseSnapshotPath;
        }

        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, LegacySnapshotRelativePath));
    }

    private async Task<Dictionary<string, string>> LoadLegacySnapshotExchangeMapAsync(CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshotPath = ResolveLegacySnapshotPath();
        if (!File.Exists(snapshotPath))
        {
            return map;
        }

        try
        {
            var json = await File.ReadAllTextAsync(snapshotPath, cancellationToken);
            var snapshot = JsonSerializer.Deserialize<Sp500LegacySnapshot>(json);
            if (snapshot?.Constituents is null)
            {
                return map;
            }

            foreach (var item in snapshot.Constituents)
            {
                var ticker = item.Ticker?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(ticker)) continue;
                if (!StockExchanges.TryNormalize(item.Exchange, out var normalizedExchange)) continue;
                map[ticker] = normalizedExchange;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load legacy SP500 exchange map; explicit map only will be used.");
        }

        return map;
    }

    private static async Task<(IndexConstituentsStatus Status, string? Message, IReadOnlyList<IndexConstituentEntry> Entries)> ParseCsvAsync(
        string csvPath,
        IReadOnlyDictionary<string, string> legacyExchangeMap,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: true);

        var header = (await reader.ReadLineAsync(cancellationToken) ?? string.Empty).Trim();
        if (!string.Equals(header, ExpectedHeader, StringComparison.Ordinal))
        {
            return (IndexConstituentsStatus.ProviderFailure, $"Curated CSV S&P 500 имеет неожиданный заголовок: {header}", Array.Empty<IndexConstituentEntry>());
        }

        var entries = new List<IndexConstituentEntry>(ExpectedDataRowCount);
        var unresolvedTickers = new List<string>();
        var seenIdentities = new HashSet<string>(StringComparer.Ordinal);
        var row = 1;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            row++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split(';');
            if (columns.Length != 5)
            {
                return (IndexConstituentsStatus.ProviderFailure, $"Curated CSV S&P 500: строка {row} содержит {columns.Length} колонок вместо 5.", Array.Empty<IndexConstituentEntry>());
            }

            var ticker = columns[0].Trim().ToUpperInvariant();
            var companyName = NormalizeCompanyName(columns[1]);
            var isin = StockIdentifiers.Normalize(columns[2]);
            var wkn = StockIdentifiers.Normalize(columns[3]);
            var sector = NormalizeClassification(columns[4]);

            if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(companyName))
            {
                return (IndexConstituentsStatus.ProviderFailure, $"Curated CSV S&P 500: строка {row} содержит пустой ticker/company.", Array.Empty<IndexConstituentEntry>());
            }

            if (isin is not null && !StockIdentifiers.IsValidIsin(isin))
            {
                return (IndexConstituentsStatus.ProviderFailure, $"Curated CSV S&P 500: некорректный ISIN для {ticker}.", Array.Empty<IndexConstituentEntry>());
            }

            if (wkn is not null && !StockIdentifiers.IsValidWkn(wkn))
            {
                return (IndexConstituentsStatus.ProviderFailure, $"Curated CSV S&P 500: некорректный WKN для {ticker}.", Array.Empty<IndexConstituentEntry>());
            }

            if (sector is null || !AllowedSectors.Contains(sector))
            {
                return (IndexConstituentsStatus.ProviderFailure, $"Curated CSV S&P 500: некорректный Sector '{columns[4]}' для {ticker}.", Array.Empty<IndexConstituentEntry>());
            }

            var exchange = ResolveExchange(ticker, legacyExchangeMap);
            if (exchange is null)
            {
                unresolvedTickers.Add(ticker);
                continue;
            }

            var providerSymbol = ResolveProviderSymbol(ticker, exchange);
            var identity = $"{providerSymbol}|{exchange}";
            if (!seenIdentities.Add(identity))
            {
                return (IndexConstituentsStatus.ProviderFailure, $"Curated CSV S&P 500 содержит дубликат identity: {identity}.", Array.Empty<IndexConstituentEntry>());
            }

            entries.Add(new IndexConstituentEntry(
                ProviderSymbol: providerSymbol,
                Ticker: ticker,
                CompanyName: companyName,
                ProviderExchange: exchange,
                Isin: isin,
                Wkn: wkn,
                Sector: sector,
                Industry: null));
        }

        if (entries.Count + unresolvedTickers.Count != ExpectedDataRowCount)
        {
            return (IndexConstituentsStatus.ProviderFailure, $"Curated CSV S&P 500 содержит {entries.Count + unresolvedTickers.Count} непустых строк вместо {ExpectedDataRowCount}.", Array.Empty<IndexConstituentEntry>());
        }

        if (unresolvedTickers.Count > 0)
        {
            var preview = string.Join(", ", unresolvedTickers.Take(10));
            var message = $"Не удалось определить биржу NYSE/NASDAQ для {unresolvedTickers.Count} тикеров: {preview}.";
            return (IndexConstituentsStatus.Partial, message, entries);
        }

        return (IndexConstituentsStatus.Success, null, entries);
    }

    private static string ResolveProviderSymbol(string ticker, string exchange)
    {
        if (ticker is "BRK.B" or "BF.B" or "BRK.A" or "BF.A")
        {
            return ticker.Replace('.', '-');
        }

        return StockExchanges.ResolveProviderSymbol(ticker, exchange);
    }

    private static string? ResolveExchange(string ticker, IReadOnlyDictionary<string, string> legacyExchangeMap)
    {
        if (legacyExchangeMap.TryGetValue(ticker, out var fromLegacy) && StockExchanges.TryNormalize(fromLegacy, out var normalizedLegacy))
        {
            return normalizedLegacy;
        }

        if (ExplicitExchangeMap.TryGetValue(ticker, out var explicitExchange) && StockExchanges.TryNormalize(explicitExchange, out var normalizedExplicit))
        {
            return normalizedExplicit;
        }

        return null;
    }

    private static string NormalizeCompanyName(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.EndsWith('|'))
        {
            normalized = normalized.TrimEnd('|').TrimEnd();
        }

        return normalized;
    }

    private static string? NormalizeClassification(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class Sp500LegacySnapshot
    {
        [JsonPropertyName("constituents")]
        public List<Sp500LegacyConstituent> Constituents { get; init; } = [];
    }

    private sealed class Sp500LegacyConstituent
    {
        [JsonPropertyName("ticker")]
        public string Ticker { get; init; } = string.Empty;

        [JsonPropertyName("exchange")]
        public string Exchange { get; init; } = string.Empty;
    }
}
