using System.Text.Json;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

public sealed record CuratedIdentifierCandidate(string? Isin, string? Wkn, string Source, StockMetadataEnrichmentConfidence Confidence);

public interface IStockMetadataCuratedSnapshotService
{
    CuratedIdentifierCandidate? FindByListingIdentity(string providerSymbol, string exchange);
}

public sealed class StockMetadataCuratedSnapshotService : IStockMetadataCuratedSnapshotService
{
    private const string SnapshotDirectoryRelativePath = "Data/index-constituents";

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<StockMetadataCuratedSnapshotService> _logger;
    private readonly string _baseDirectory;
    private readonly Lazy<Dictionary<string, CuratedIdentifierCandidate>> _lookup;

    public StockMetadataCuratedSnapshotService(
        IWebHostEnvironment environment,
        ILogger<StockMetadataCuratedSnapshotService> logger,
        string? baseDirectoryOverride = null)
    {
        _environment = environment;
        _logger = logger;
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectoryOverride) ? AppContext.BaseDirectory : baseDirectoryOverride;
        _lookup = new Lazy<Dictionary<string, CuratedIdentifierCandidate>>(BuildLookup, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public CuratedIdentifierCandidate? FindByListingIdentity(string providerSymbol, string exchange)
    {
        if (string.IsNullOrWhiteSpace(providerSymbol) || string.IsNullOrWhiteSpace(exchange))
        {
            return null;
        }

        if (!StockExchanges.TryNormalize(exchange, out var normalizedExchange))
        {
            return null;
        }

        var key = BuildKey(providerSymbol.Trim(), normalizedExchange);
        return _lookup.Value.TryGetValue(key, out var candidate) ? candidate : null;
    }

    private Dictionary<string, CuratedIdentifierCandidate> BuildLookup()
    {
        var directory = ResolveSnapshotDirectory();
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Curated snapshot directory was not found: {Directory}", directory);
            return new Dictionary<string, CuratedIdentifierCandidate>(StringComparer.Ordinal);
        }

        var files = Directory.GetFiles(directory, "*.curated.snapshot.json", SearchOption.TopDirectoryOnly);
        var map = new Dictionary<string, CuratedIdentifierCandidate>(StringComparer.Ordinal);
        var conflicts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                if (!document.RootElement.TryGetProperty("constituents", out var constituents)
                    || constituents.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var sourceName = document.RootElement.TryGetProperty("sourceName", out var source)
                    ? source.GetString() ?? Path.GetFileName(file)
                    : Path.GetFileName(file);

                foreach (var item in constituents.EnumerateArray())
                {
                    var providerSymbol = item.TryGetProperty("providerSymbol", out var symbolEl)
                        ? symbolEl.GetString()
                        : null;
                    var exchange = item.TryGetProperty("exchange", out var exchangeEl)
                        ? exchangeEl.GetString()
                        : null;
                    var isin = item.TryGetProperty("isin", out var isinEl)
                        ? StockIdentifiers.Normalize(isinEl.GetString())
                        : null;

                    if (string.IsNullOrWhiteSpace(providerSymbol)
                        || string.IsNullOrWhiteSpace(exchange)
                        || isin is null
                        || !StockIdentifiers.IsValidIsin(isin)
                        || !StockExchanges.TryNormalize(exchange, out var normalizedExchange))
                    {
                        continue;
                    }

                    var key = BuildKey(providerSymbol.Trim(), normalizedExchange);
                    var candidate = new CuratedIdentifierCandidate(isin, null, sourceName, StockMetadataEnrichmentConfidence.High);

                    if (map.TryGetValue(key, out var existing)
                        && !string.Equals(existing.Isin, candidate.Isin, StringComparison.Ordinal))
                    {
                        conflicts.Add(key);
                        map.Remove(key);
                        continue;
                    }

                    if (!conflicts.Contains(key))
                    {
                        map[key] = candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse curated snapshot file {File}", file);
            }
        }

        if (conflicts.Count > 0)
        {
            _logger.LogWarning("Excluded {ConflictCount} ambiguous listing identities from curated snapshots.", conflicts.Count);
        }

        return map;
    }

    private string ResolveSnapshotDirectory()
    {
        var appBasePath = Path.GetFullPath(Path.Combine(_baseDirectory, SnapshotDirectoryRelativePath));
        if (Directory.Exists(appBasePath))
        {
            return appBasePath;
        }

        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, SnapshotDirectoryRelativePath));
    }

    private static string BuildKey(string providerSymbol, string exchange)
        => $"{providerSymbol.Trim()}|{exchange.Trim().ToUpperInvariant()}";
}
