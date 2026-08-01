using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace FinanceApp.Core.Models;

public enum TransactionType { Deposit, Withdrawal, Buy, Sell, Dividend }

public class Transaction
{
    public int Id { get; set; }
    public int PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransactionType Type { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal SignedAmount { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Optional stock link (required for Buy, Sell, Dividend)
    public int? StockId { get; set; }
    public Stock? Stock { get; set; }

    // Optional order link for idempotent order-execution transactions
    public int? OrderId { get; set; }

    public decimal GetEffectiveSignedAmount() => TransactionDirection.ResolveSignedAmount(Type, Amount, SignedAmount);

    public void ApplySignedAmount(decimal signedAmount, TransactionType fallbackType)
    {
        SignedAmount = signedAmount;
        Amount = TransactionDirection.GetDisplayAmount(signedAmount);
        Type = TransactionDirection.ResolveType(signedAmount, fallbackType);
    }
}

public static class TransactionDirection
{
    public static bool HasStoredSignedAmount(decimal amount, decimal? signedAmount) =>
        signedAmount.HasValue && (signedAmount.Value != 0m || amount == 0m);

    /// <summary>
    /// Derives the signed cash-flow from a transaction type and a positive user-entered amount.
    /// Sign semantics: Deposit/Sell/Dividend = positive; Withdrawal/Buy = negative.
    /// </summary>
    public static decimal DeriveSignedAmount(TransactionType type, decimal positiveAmount)
    {
        var abs = decimal.Abs(positiveAmount);
        return type switch
        {
            TransactionType.Withdrawal or TransactionType.Buy => -abs,
            _ => abs,
        };
    }

    public static decimal ResolveSignedAmount(TransactionType type, decimal amount, decimal? signedAmount = null)
    {
        if (HasStoredSignedAmount(amount, signedAmount))
        {
            return signedAmount!.Value;
        }

        var normalizedAmount = decimal.Abs(amount);
        return type == TransactionType.Withdrawal ? -normalizedAmount : normalizedAmount;
    }

    public static TransactionType ResolveType(decimal signedAmount, TransactionType fallbackType)
    {
        if (signedAmount < 0m)
        {
            return TransactionType.Withdrawal;
        }

        if (signedAmount > 0m)
        {
            return TransactionType.Deposit;
        }

        return fallbackType;
    }

    public static decimal GetDisplayAmount(decimal signedAmount) => decimal.Abs(signedAmount);
}
