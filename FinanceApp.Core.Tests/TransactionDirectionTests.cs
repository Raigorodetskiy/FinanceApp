using FinanceApp.Core.Models;
using Xunit;

namespace FinanceApp.Core.Tests;

public class TransactionDirectionTests
{
    [Theory]
    [InlineData("Kundennummer 8368195 - 180626", 1500.00, TransactionType.Deposit)]
    [InlineData("Zinsabschluss 01.04.2026 - 30.06.2026", 2.67, TransactionType.Deposit)]
    [InlineData("Kartenzahlung", -42.15, TransactionType.Withdrawal)]
    [InlineData("Zinsabschluss 01.04.2026 - 30.06.2026", -1.25, TransactionType.Withdrawal)]
    public void ApplySignedAmount_PreservesDirectionForImportedTransactions(
        string description,
        decimal signedAmount,
        TransactionType expectedType)
    {
        var transaction = new Transaction
        {
            Description = description,
        };

        transaction.ApplySignedAmount(signedAmount, TransactionType.Withdrawal);

        Assert.Equal(expectedType, transaction.Type);
        Assert.Equal(signedAmount, transaction.SignedAmount);
        Assert.Equal(decimal.Abs(signedAmount), transaction.Amount);
    }

    [Fact]
    public void ResolveSignedAmount_UsesExplicitSignedAmountBeforeNormalizedDisplayAmount()
    {
        var signedAmount = TransactionDirection.ResolveSignedAmount(
            TransactionType.Withdrawal,
            2.67m,
            2.67m);

        Assert.Equal(2.67m, signedAmount);
        Assert.Equal(TransactionType.Deposit, TransactionDirection.ResolveType(signedAmount, TransactionType.Withdrawal));
    }

    [Fact]
    public void ResolveSignedAmount_FallsBackToTypeWhenSignedAmountIsDefaultZeroForLegacyRows()
    {
        var signedAmount = TransactionDirection.ResolveSignedAmount(
            TransactionType.Withdrawal,
            42.15m,
            0m);

        Assert.Equal(-42.15m, signedAmount);
    }
}
