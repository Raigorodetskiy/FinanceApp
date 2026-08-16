using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Routes index-constituent requests by stable market-index code.
/// </summary>
public sealed class IndexConstituentsProviderRouter : IIndexConstituentsProvider
{
    private const string DjiaCode = "DJIA";
    private const string Nasdaq100Code = "NDX";
    private const string Sp500Code = "SPX";

    private readonly IDjiaIndexConstituentsProvider _djiaProvider;
    private readonly INasdaq100IndexConstituentsProvider _nasdaq100Provider;
    private readonly ISp500IndexConstituentsProvider _sp500Provider;
    private readonly IUnsupportedIndexConstituentsProvider _fallbackProvider;

    public IndexConstituentsProviderRouter(
        IDjiaIndexConstituentsProvider djiaProvider,
        INasdaq100IndexConstituentsProvider nasdaq100Provider,
        ISp500IndexConstituentsProvider sp500Provider,
        IUnsupportedIndexConstituentsProvider fallbackProvider)
    {
        _djiaProvider = djiaProvider;
        _nasdaq100Provider = nasdaq100Provider;
        _sp500Provider = sp500Provider;
        _fallbackProvider = fallbackProvider;
    }

    public string ProviderName => "Index constituents router";

    public Task<IndexConstituentsResult> GetConstituentsAsync(
        MarketIndex index,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(index);
        if (string.Equals(normalizedCode, DjiaCode, StringComparison.Ordinal))
        {
            return _djiaProvider.GetConstituentsAsync(index, cancellationToken);
        }

        if (string.Equals(normalizedCode, Nasdaq100Code, StringComparison.Ordinal))
        {
            return _nasdaq100Provider.GetConstituentsAsync(index, cancellationToken);
        }

        if (string.Equals(normalizedCode, Sp500Code, StringComparison.Ordinal))
        {
            return _sp500Provider.GetConstituentsAsync(index, cancellationToken);
        }

        return _fallbackProvider.GetConstituentsAsync(index, cancellationToken);
    }

    private static string NormalizeCode(MarketIndex index)
    {
        var candidate = string.IsNullOrWhiteSpace(index.NormalizedCode)
            ? index.Code
            : index.NormalizedCode;
        return candidate?.Trim().ToUpperInvariant() ?? string.Empty;
    }
}
