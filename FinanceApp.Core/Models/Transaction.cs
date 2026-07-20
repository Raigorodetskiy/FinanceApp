using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApp.Core.Models;

public enum TransactionType { Deposit, Withdrawal }

public class Transaction
{
    public int Id { get; set; }
    public int PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
    public TransactionType Type { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
