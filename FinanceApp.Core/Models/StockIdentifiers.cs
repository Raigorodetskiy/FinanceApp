using System.Text.RegularExpressions;

namespace FinanceApp.Core.Models;

/// <summary>Helpers for normalizing and validating WKN and ISIN security identifiers.</summary>
public static class StockIdentifiers
{
    private static readonly Regex WknPattern = new(@"^[A-Z0-9]{6}$", RegexOptions.Compiled);
    private static readonly Regex IsinPattern = new(@"^[A-Z]{2}[A-Z0-9]{10}$", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a WKN or ISIN value: trims whitespace, converts to uppercase.
    /// Returns <c>null</c> when the input is null or consists only of whitespace.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (value == null) return null;
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>Returns <c>true</c> when <paramref name="wkn"/> matches exactly 6 uppercase alphanumeric characters.</summary>
    public static bool IsValidWkn(string wkn) => WknPattern.IsMatch(wkn);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="isin"/> matches exactly 12 characters:
    /// two uppercase letters (country prefix) followed by 10 uppercase alphanumeric characters.
    /// </summary>
    public static bool IsValidIsin(string isin) => IsinPattern.IsMatch(isin);
}
