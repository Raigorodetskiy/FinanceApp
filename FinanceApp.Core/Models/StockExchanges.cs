namespace FinanceApp.Core.Models;

public static class StockExchanges
{
    public const string Nyse = "NYSE";
    public const string Frankfurt = "Frankfurt";

    public static IReadOnlyList<string> Supported { get; } = new[] { Nyse, Frankfurt };

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
}
