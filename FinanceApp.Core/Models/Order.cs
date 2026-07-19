using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApp.Core.Models;

public enum OrderType { Buy, Sell }
public enum OrderStatus { Pending, Executed, Cancelled }

public class Order
{
    public int Id { get; set; }
    public int PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
    public int StockId { get; set; }
    public Stock Stock { get; set; } = null!;
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal? StopLoss { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal? StopMarket { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExecutedAt { get; set; }
}
