using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

public interface IStockHistoryService
{
    Task SyncHistoricalDataForStockAsync(Stock stock, CancellationToken cancellationToken = default);
    Task SyncHistoricalDataForAllStocksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockHistoricalPrice>> GetHistoryAsync(int stockId, string range, CancellationToken cancellationToken = default);
}
