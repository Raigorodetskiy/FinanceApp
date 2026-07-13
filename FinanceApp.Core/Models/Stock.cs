using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApp.Core.Models;

public class Stock
{
    public int Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentPrice { get; set; }
    public DateTime UpdatedAt { get; set; }
}
