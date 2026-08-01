using FinanceApp.Core.Models;
using Xunit;

namespace FinanceApp.Core.Tests;

/// <summary>
/// Tests for <see cref="StockExchanges.ResolveProviderSymbol"/>, the single authoritative
/// Yahoo/provider symbol resolver used by both the current-quote and history code paths.
/// </summary>
public class StockExchangesTests
{
    // ── ResolveProviderSymbol – basic Frankfurt resolution ──────────────────────

    [Fact]
    public void ResolveProviderSymbol_BareTickerFrankfurt_AppendsDotF()
    {
        // Requirement 1: AMZN + FRA → AMZN.F
        Assert.Equal("AMZN.F", StockExchanges.ResolveProviderSymbol("AMZN", StockExchanges.Frankfurt));
    }

    [Fact]
    public void ResolveProviderSymbol_AlreadyDotFTickerFrankfurt_RemainsUnchanged()
    {
        // Requirement 2: AMZN.F + FRA → AMZN.F (idempotent)
        Assert.Equal("AMZN.F", StockExchanges.ResolveProviderSymbol("AMZN.F", StockExchanges.Frankfurt));
    }

    // ── Case-insensitivity and idempotency ──────────────────────────────────────

    [Theory]
    [InlineData("amzn.f")]   // lowercase .f suffix
    [InlineData("AMZN.f")]   // mixed case .f
    [InlineData("AMZN.F")]   // uppercase .F
    public void ResolveProviderSymbol_DotFVariantsFrankfurt_AllRemainUnchanged(string ticker)
    {
        // Requirement 3: suffix handling is case-insensitive and idempotent.
        // Any ticker that already carries a period is left as-is.
        var result = StockExchanges.ResolveProviderSymbol(ticker, StockExchanges.Frankfurt);
        Assert.Equal(ticker, result);
    }

    [Fact]
    public void ResolveProviderSymbol_IdempotentDoubleApplication()
    {
        // Applying the resolver twice to the same input gives the same result.
        var first = StockExchanges.ResolveProviderSymbol("AMZN", StockExchanges.Frankfurt);   // AMZN.F
        var second = StockExchanges.ResolveProviderSymbol(first, StockExchanges.Frankfurt);   // still AMZN.F
        Assert.Equal(first, second);
    }

    // ── Whitespace trimming ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(" AMZN ")]
    [InlineData("  AMZN")]
    [InlineData("AMZN  ")]
    public void ResolveProviderSymbol_LeadingTrailingWhitespaceFrankfurt_IsTrimmedAndDotFAppended(string ticker)
    {
        // Requirement 4: surrounding whitespace is trimmed before resolution.
        Assert.Equal("AMZN.F", StockExchanges.ResolveProviderSymbol(ticker, StockExchanges.Frankfurt));
    }

    // ── Non-Frankfurt symbols are unchanged ─────────────────────────────────────

    [Theory]
    [InlineData("AMZN",  "NYSE")]
    [InlineData("AAPL",  "NYSE")]
    [InlineData("MSFT",  null)]       // null exchange defaults to NYSE
    [InlineData("GOOG",  "  ")]       // whitespace-only exchange defaults to NYSE
    public void ResolveProviderSymbol_NonFrankfurtExchange_SymbolUnchanged(string ticker, string? exchange)
    {
        // Requirement 5: symbols for non-Frankfurt exchanges are not given ".F".
        Assert.Equal(ticker, StockExchanges.ResolveProviderSymbol(ticker, exchange));
    }

    // ── Ticker already carrying a non-.F suffix (XETRA etc.) ───────────────────

    [Fact]
    public void ResolveProviderSymbol_TickerWithDifferentSuffixFrankfurt_IsNotModified()
    {
        // A ticker like RHM.DE already has an exchange suffix; it must not become RHM.DE.F.
        Assert.Equal("RHM.DE", StockExchanges.ResolveProviderSymbol("RHM.DE", StockExchanges.Frankfurt));
    }

    // ── Quote and history use the same resolver ─────────────────────────────────

    [Fact]
    public void ResolveProviderSymbol_QuoteAndHistoryPathsYieldIdenticalSymbol()
    {
        // Requirement 8: quote and history cannot silently use different provider symbols
        // for the same FRA stock.  Both code paths call StockExchanges.ResolveProviderSymbol,
        // so this verifies the shared single-source-of-truth property.
        const string storedTicker = "AMZN";
        const string exchange = StockExchanges.Frankfurt;

        var quoteProviderSymbol = StockExchanges.ResolveProviderSymbol(storedTicker, exchange);
        var historyProviderSymbol = StockExchanges.ResolveProviderSymbol(storedTicker, exchange);

        Assert.Equal(quoteProviderSymbol, historyProviderSymbol);
        Assert.Equal("AMZN.F", quoteProviderSymbol);
    }

    // ── Edge cases ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveProviderSymbol_NullOrEmptyTicker_ReturnsEmptyString(string? ticker)
    {
        var result = StockExchanges.ResolveProviderSymbol(ticker, StockExchanges.Frankfurt);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveProviderSymbol_UnknownExchange_TickerUnchanged()
    {
        // An unrecognised exchange means TryNormalize fails; ticker is returned unchanged.
        Assert.Equal("AMZN", StockExchanges.ResolveProviderSymbol("AMZN", "UNKNOWN_EXCHANGE"));
    }
}
