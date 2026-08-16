using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Yahoo Finance constituent provider.
/// 
/// Yahoo Finance does not expose a reliable structured endpoint for index constituents in its
/// public/free tier. This implementation returns <see cref="IndexConstituentsStatus.Unsupported"/>
/// for all indices until a confirmed, stable endpoint is available.
/// </summary>
public sealed class YahooIndexConstituentsProvider : IUnsupportedIndexConstituentsProvider
{
    public string ProviderName => "Yahoo Finance";

    public Task<IndexConstituentsResult> GetConstituentsAsync(
        MarketIndex index,
        CancellationToken cancellationToken = default)
    {
        var result = IndexConstituentsResult.Unsupported(
            ProviderName,
            "Автоматическая загрузка состава для этого индекса не поддерживается. " +
            "Yahoo Finance не предоставляет структурированный публичный endpoint для компонентов индексов.");

        return Task.FromResult(result);
    }
}
