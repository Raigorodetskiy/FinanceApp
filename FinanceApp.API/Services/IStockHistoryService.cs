using FinanceApp.API.Models;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

public interface IStockHistoryService
{
    Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default);
    Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default);
    Task<StockHistoryResponse> GetHistoryAsync(Stock stock, string range, CancellationToken cancellationToken = default);
    Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, CancellationToken cancellationToken = default);
    Task<StockHistoryRefreshResponse> RefreshHistoryAsync(Stock stock, StockHistoryRefreshTrigger trigger, CancellationToken cancellationToken = default);
}
