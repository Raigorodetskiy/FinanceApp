using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Routes index-constituent requests by stable market-index code.
/// </summary>
public sealed class IndexConstituentsProviderRouter : IIndexConstituentsProvider
{
    private const string DjiaCode = "DJIA";

    private readonly IDjiaIndexConstituentsProvider _djiaProvider;
    private readonly IUnsupportedIndexConstituentsProvider _fallbackProvider;

    public IndexConstituentsProviderRouter(
        IDjiaIndexConstituentsProvider djiaProvider,
        IUnsupportedIndexConstituentsProvider fallbackProvider)
    {
        _djiaProvider = djiaProvider;
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
