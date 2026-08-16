using FinanceApp.API.Models;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

public interface IMarketIndexHistoryService
{
    Task<MarketIndexHistoryResponse> GetHistoryAsync(MarketIndex index, string range, CancellationToken cancellationToken = default);
    Task<MarketIndexRefreshResponse> RefreshHistoryAsync(MarketIndex index, string range, CancellationToken cancellationToken = default);
}
