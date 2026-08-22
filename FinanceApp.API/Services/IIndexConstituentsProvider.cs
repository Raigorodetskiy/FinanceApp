using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Represents a single constituent entry returned by a provider.
/// </summary>
public sealed record IndexConstituentEntry(
    /// <summary>Symbol as provided by this data source.</summary>
    string ProviderSymbol,
    /// <summary>User-facing ticker (may equal ProviderSymbol).</summary>
    string Ticker,
    /// <summary>Company name.</summary>
    string CompanyName,
    /// <summary>Exchange code as reported by the provider.</summary>
    string? ProviderExchange,
    /// <summary>ISIN if reliably provided by the source; otherwise null.</summary>
    string? Isin,
    /// <summary>WKN if reliably provided by the source; otherwise null.</summary>
    string? Wkn = null,
    /// <summary>Provider-sector label when available.</summary>
    string? Sector = null,
    /// <summary>Provider-industry label when available.</summary>
    string? Industry = null
);

/// <summary>
/// Status of a constituent provider response.
/// </summary>
public enum IndexConstituentsStatus
{
    /// <summary>Constituents were retrieved successfully.</summary>
    Success,
    /// <summary>This provider does not support constituents for the given index.</summary>
    Unsupported,
    /// <summary>Provider returned a partial/stale result; existing memberships must not be closed.</summary>
    Partial,
    /// <summary>Provider rate-limited this request.</summary>
    RateLimited,
    /// <summary>Provider encountered an error.</summary>
    ProviderFailure,
}

/// <summary>
/// Result of a constituent provider request.
/// </summary>
public sealed record IndexConstituentsResult(
    IndexConstituentsStatus Status,
    string ProviderName,
    DateTime FetchedAt,
    IReadOnlyList<IndexConstituentEntry> Constituents,
    /// <summary>Human-readable message (error description, warning, unsupported reason).</summary>
    string? Message = null,
    /// <summary>Snapshot effective date reported by source (if available).</summary>
    DateTime? AsOfDate = null,
    /// <summary>Human-readable source attribution URL (if available).</summary>
    string? SourceUrl = null,
    /// <summary>True when source is a curated/versioned snapshot, not a live endpoint.</summary>
    bool IsCuratedSnapshot = false,
    /// <summary>True when data may be stale.</summary>
    bool IsStale = false
)
{
    public static IndexConstituentsResult Unsupported(string providerName, string? reason = null)
        => new(IndexConstituentsStatus.Unsupported, providerName, DateTime.UtcNow, [], reason);

    public static IndexConstituentsResult Failure(string providerName, string message)
        => new(IndexConstituentsStatus.ProviderFailure, providerName, DateTime.UtcNow, [], message);

    public static IndexConstituentsResult RateLimited(string providerName, string? message = null)
        => new(IndexConstituentsStatus.RateLimited, providerName, DateTime.UtcNow, [], message);
}

/// <summary>
/// Abstraction for retrieving index constituent lists from a data provider.
/// </summary>
public interface IIndexConstituentsProvider
{
    /// <summary>Human-readable name of this provider (e.g. "Yahoo Finance").</summary>
    string ProviderName { get; }

    Task<IndexConstituentsResult> GetConstituentsAsync(
        MarketIndex index,
        CancellationToken cancellationToken = default);
}

public interface IDjiaIndexConstituentsProvider : IIndexConstituentsProvider;

public interface INasdaq100IndexConstituentsProvider : IIndexConstituentsProvider;

public interface ISp500IndexConstituentsProvider : IIndexConstituentsProvider;

public interface IDaxIndexConstituentsProvider : IIndexConstituentsProvider;

public interface IUnsupportedIndexConstituentsProvider : IIndexConstituentsProvider;
