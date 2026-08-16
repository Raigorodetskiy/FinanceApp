namespace FinanceApp.Core.Models;

public class StockMarketIndex
{
    public int StockId { get; set; }
    public int MarketIndexId { get; set; }

    public Stock Stock { get; set; } = null!;
    public MarketIndex MarketIndex { get; set; } = null!;
}
