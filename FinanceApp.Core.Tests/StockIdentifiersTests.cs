using FinanceApp.Core.Models;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockIdentifiersTests
{
    // ── Normalize ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("865985", "865985")]
    [InlineData(" 865985 ", "865985")]
    [InlineData("865985 ", "865985")]
    [InlineData("abc123", "ABC123")]
    [InlineData("us0378331005", "US0378331005")]
    public void Normalize_ReturnsExpected(string? input, string? expected)
    {
        Assert.Equal(expected, StockIdentifiers.Normalize(input));
    }

    // ── WKN validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("865985", true)]   // valid numeric
    [InlineData("A0DPWH", true)]   // valid alphanumeric
    [InlineData("ZZZZZZ", true)]   // all letters
    [InlineData("000000", true)]   // all zeros
    public void IsValidWkn_ValidValues_ReturnsTrue(string wkn, bool expected)
    {
        Assert.Equal(expected, StockIdentifiers.IsValidWkn(wkn));
    }

    [Theory]
    [InlineData("86598")]          // too short
    [InlineData("8659851")]        // too long
    [InlineData("86598!")]         // special char
    [InlineData("86598a")]         // lowercase letter
    [InlineData("")]               // empty
    public void IsValidWkn_InvalidValues_ReturnsFalse(string wkn)
    {
        Assert.False(StockIdentifiers.IsValidWkn(wkn));
    }

    // ── ISIN validation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("US0378331005", true)]  // Apple
    [InlineData("DE0005140008", true)]  // Deutsche Bank
    [InlineData("GB0002634946", true)]  // Two-letter country + alphanumeric
    public void IsValidIsin_ValidValues_ReturnsTrue(string isin, bool expected)
    {
        Assert.Equal(expected, StockIdentifiers.IsValidIsin(isin));
    }

    [Theory]
    [InlineData("US037833100")]        // too short
    [InlineData("US03783310056")]      // too long
    [InlineData("1S0378331005")]       // starts with digit
    [InlineData("us0378331005")]       // lowercase country code
    [InlineData("US037833100!")]       // special char
    [InlineData("")]                   // empty
    public void IsValidIsin_InvalidValues_ReturnsFalse(string isin)
    {
        Assert.False(StockIdentifiers.IsValidIsin(isin));
    }
}
