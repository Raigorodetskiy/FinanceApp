using FinanceApp.API.Models;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

public interface IStockQuoteConversionService
{
    Task<CurrencyConversionContext> GetConversionContextAsync(string? quoteCurrency, string? financialCurrency, CancellationToken cancellationToken = default);
    StockQuoteResponse BuildQuoteResponse(
        string symbol,
        decimal rawCurrentPrice,
        decimal rawPreviousClose,
        decimal percentChange,
        string marketState,
        CurrencyConversionContext conversionContext);
    StockHistoryPointResponse BuildHistoryPointResponse(StockHistoricalPrice historicalPrice, CurrencyConversionContext conversionContext);
}

public sealed class StockQuoteConversionService : IStockQuoteConversionService
{
    private readonly IExchangeRateService _exchangeRateService;

    public StockQuoteConversionService(IExchangeRateService exchangeRateService)
    {
        _exchangeRateService = exchangeRateService;
    }

    public async Task<CurrencyConversionContext> GetConversionContextAsync(
        string? quoteCurrency,
        string? financialCurrency,
        CancellationToken cancellationToken = default)
    {
        var metadata = QuoteCurrencyMetadata.Parse(quoteCurrency, financialCurrency);
        var rate = await _exchangeRateService.GetRateToEurAsync(metadata.NormalizedQuoteCurrency, cancellationToken);
        var warning = BuildWarning(metadata, rate);

        return new CurrencyConversionContext(metadata, rate, warning);
    }

    public StockQuoteResponse BuildQuoteResponse(
        string symbol,
        decimal rawCurrentPrice,
        decimal rawPreviousClose,
        decimal percentChange,
        string marketState,
        CurrencyConversionContext conversionContext)
    {
        var normalizedCurrentPrice = conversionContext.Normalize(rawCurrentPrice);
        var normalizedPreviousClose = conversionContext.Normalize(rawPreviousClose);
        var normalizedChange = normalizedCurrentPrice - normalizedPreviousClose;

        return new StockQuoteResponse
        {
            Symbol = symbol,
            RawCurrentPrice = rawCurrentPrice,
            RawPreviousClose = rawPreviousClose,
            RawChange = rawCurrentPrice - rawPreviousClose,
            Currency = conversionContext.Metadata.QuoteCurrency,
            FinancialCurrency = conversionContext.Metadata.FinancialCurrency,
            NormalizedQuoteCurrency = conversionContext.Metadata.NormalizedQuoteCurrency,
            QuoteUnitMultiplier = conversionContext.Metadata.QuoteUnitMultiplier,
            NormalizedCurrentPrice = normalizedCurrentPrice,
            NormalizedPreviousClose = normalizedPreviousClose,
            NormalizedChange = normalizedChange,
            CurrentPriceEur = conversionContext.ConvertToEur(rawCurrentPrice),
            ChangeEur = conversionContext.ConvertNormalizedToEur(normalizedChange),
            PercentChange = percentChange,
            MarketState = marketState,
            RateToEur = conversionContext.ExchangeRate.RateToEur,
            RateTimestampUtc = conversionContext.ExchangeRate.RateTimestampUtc,
            RateSource = conversionContext.ExchangeRate.Source,
            ConversionWarning = conversionContext.Warning
        };
    }

    public StockHistoryPointResponse BuildHistoryPointResponse(StockHistoricalPrice historicalPrice, CurrencyConversionContext conversionContext)
    {
        var openNormalized = conversionContext.Normalize(historicalPrice.Open);
        var highNormalized = conversionContext.Normalize(historicalPrice.High);
        var lowNormalized = conversionContext.Normalize(historicalPrice.Low);
        var closeNormalized = conversionContext.Normalize(historicalPrice.Close);

        return new StockHistoryPointResponse
        {
            Timestamp = historicalPrice.Timestamp,
            Interval = historicalPrice.Interval,
            OpenRaw = historicalPrice.Open,
            HighRaw = historicalPrice.High,
            LowRaw = historicalPrice.Low,
            CloseRaw = historicalPrice.Close,
            OpenNormalized = openNormalized,
            HighNormalized = highNormalized,
            LowNormalized = lowNormalized,
            CloseNormalized = closeNormalized,
            OpenEur = conversionContext.ConvertNormalizedToEur(openNormalized),
            HighEur = conversionContext.ConvertNormalizedToEur(highNormalized),
            LowEur = conversionContext.ConvertNormalizedToEur(lowNormalized),
            CloseEur = conversionContext.ConvertNormalizedToEur(closeNormalized),
            Volume = historicalPrice.Volume
        };
    }

    private static string? BuildWarning(QuoteCurrencyMetadata metadata, ExchangeRateResult rate)
    {
        if (string.IsNullOrWhiteSpace(metadata.QuoteCurrency))
        {
            return "Источник котировки не указал валюту, поэтому EUR-конвертация недоступна.";
        }

        if (!rate.IsAvailable)
        {
            return $"EUR-конвертация недоступна для валюты {metadata.NormalizedQuoteCurrency ?? metadata.QuoteCurrency}.";
        }

        return null;
    }
}

public sealed record CurrencyConversionContext(
    QuoteCurrencyMetadata Metadata,
    ExchangeRateResult ExchangeRate,
    string? Warning)
{
    public decimal Normalize(decimal rawValue) => rawValue * Metadata.QuoteUnitMultiplier;

    public decimal? ConvertToEur(decimal rawValue) => ConvertNormalizedToEur(Normalize(rawValue));

    public decimal? ConvertNormalizedToEur(decimal normalizedValue) =>
        ExchangeRate.RateToEur is { } rateToEur
            ? normalizedValue * rateToEur
            : null;
}

public sealed record QuoteCurrencyMetadata(
    string? QuoteCurrency,
    string? FinancialCurrency,
    string? NormalizedQuoteCurrency,
    decimal QuoteUnitMultiplier)
{
    public static QuoteCurrencyMetadata Parse(string? quoteCurrency, string? financialCurrency)
    {
        var rawCurrency = NormalizeOptionalValue(quoteCurrency);
        var reportingCurrency = NormalizeOptionalValue(financialCurrency);

        if (rawCurrency is null)
        {
            return new QuoteCurrencyMetadata(null, reportingCurrency, null, 1m);
        }

        if (string.Equals(rawCurrency, "GBp", StringComparison.Ordinal) ||
            string.Equals(rawCurrency, "GBX", StringComparison.OrdinalIgnoreCase))
        {
            return new QuoteCurrencyMetadata(rawCurrency, reportingCurrency, "GBP", 0.01m);
        }

        return new QuoteCurrencyMetadata(rawCurrency, reportingCurrency, rawCurrency.ToUpperInvariant(), 1m);
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
