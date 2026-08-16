namespace FinanceApp.Core.Models;

public static class StockExchanges
{
    public const string Nyse = "NYSE";
    public const string Nasdaq = "NASDAQ";
    public const string Frankfurt = "Frankfurt";

    public static IReadOnlyList<string> Supported { get; } = new[] { Nyse, Nasdaq, Frankfurt };

    public static bool TryNormalize(string? value, out string normalized)
    {
        var trimmedValue = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedValue))
        {
            normalized = Nyse;
            return true;
        }

        if (string.Equals(trimmedValue, Nyse, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Nyse;
            return true;
        }

        if (string.Equals(trimmedValue, Nasdaq, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Nasdaq;
            return true;
        }

        if (string.Equals(trimmedValue, Frankfurt, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Frankfurt;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static string InferFromTicker(string? ticker)
        => ticker?.Trim().EndsWith(".F", StringComparison.OrdinalIgnoreCase) == true
            ? Frankfurt
            : Nyse;

    /// <summary>
    /// Resolves the Yahoo/provider symbol for a stored ticker and exchange.
    /// For Frankfurt stocks whose ticker contains no period (i.e. a bare US-style ticker
    /// such as <c>AMZN</c>), appends the <c>.F</c> suffix required by Yahoo Finance.
    /// Symbols that already carry any exchange suffix (e.g. <c>AMZN.F</c>, <c>RHM.DE</c>)
    /// are returned unchanged, making the operation idempotent.
    /// Leading and trailing whitespace on <paramref name="ticker"/> is trimmed.
    /// Symbols for non-Frankfurt exchanges are returned unchanged.
    /// </summary>
    /// <param name="ticker">The stored/user-facing ticker symbol.</param>
    /// <param name="exchange">The exchange name; any casing accepted by <see cref="TryNormalize"/>.</param>
    /// <returns>The provider symbol to use when querying Yahoo Finance.</returns>
    public static string ResolveProviderSymbol(string? ticker, string? exchange)
    {
        var t = ticker?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(t)) return t;

        if (!TryNormalize(exchange, out var normalizedExchange)) return t;

        // Only bare tickers (no period) need the ".F" suffix for Frankfurt listings.
        // Tickers that already carry any suffix (AMZN.F, RHM.DE, …) are used as-is.
        if (normalizedExchange == Frankfurt && !t.Contains('.'))
        {
            return t + ".F";
        }

        return t;
    }
}
