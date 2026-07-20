using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApp.Core.Models;

public class Dividend
{
    public int Id { get; set; }
    public int PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
    public int StockId { get; set; }
    public Stock Stock { get; set; } = null!;
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
