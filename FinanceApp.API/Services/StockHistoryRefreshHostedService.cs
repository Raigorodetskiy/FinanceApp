namespace FinanceApp.API.Services;

public class StockHistoryRefreshHostedService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StockHistoryRefreshHostedService> _logger;

    public StockHistoryRefreshHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<StockHistoryRefreshHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RefreshAllStocksAsync(stoppingToken);

            using var timer = new PeriodicTimer(RefreshInterval);
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RefreshAllStocksAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAllStocksAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var historyService = scope.ServiceProvider.GetRequiredService<IStockHistoryService>();
            await historyService.SyncHistoricalDataForAllStocksAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed automatic stock history refresh cycle");
        }
    }
}
