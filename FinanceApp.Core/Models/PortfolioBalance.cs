namespace FinanceApp.Core.Models;

public class PortfolioBalance
{
    public decimal CashBalance { get; set; }
    public decimal BrokerCredit { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal StocksValue { get; set; }
    public decimal TotalPortfolioValue { get; set; }
}
