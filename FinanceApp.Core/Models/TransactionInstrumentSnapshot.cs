namespace FinanceApp.Core.Models;

public static class TransactionInstrumentSnapshot
{
    public static string? NormalizeInstrumentCode(string? instrumentCode, InstrumentCodeType? instrumentCodeType)
    {
        if (string.IsNullOrWhiteSpace(instrumentCode))
            return null;

        var trimmed = instrumentCode.Trim();
        return instrumentCodeType == InstrumentCodeType.ISIN
            ? StockIdentifiers.Normalize(trimmed)
            : trimmed;
    }

    public static (string? InstrumentCode, InstrumentCodeType? InstrumentCodeType) ResolveFromStock(Stock? stock)
    {
        if (stock == null)
            return (null, null);

        var normalizedIsin = StockIdentifiers.Normalize(stock.Isin);
        if (!string.IsNullOrEmpty(normalizedIsin))
            return (normalizedIsin, InstrumentCodeType.ISIN);

        var trimmedTicker = string.IsNullOrWhiteSpace(stock.Ticker) ? null : stock.Ticker.Trim();
        return string.IsNullOrEmpty(trimmedTicker)
            ? (null, null)
            : (trimmedTicker, InstrumentCodeType.Ticker);
    }
}
