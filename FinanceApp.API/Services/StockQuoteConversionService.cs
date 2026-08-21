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
        CurrencyConversionContext conversionContext,
        string priceSession = "REGULAR",
        DateTime? priceTimestampUtc = null,
        string? priceSource = null,
        string? delayWarning = null,
        decimal? rawDayHigh = null,
        decimal? rawDayLow = null);
    StockHistoryPointResponse BuildHistoryPointResponse(StockHistoricalPrice historicalPrice, CurrencyConversionContext conversionContext);
}

public sealed class StockQuoteConversionService : IStockQuoteConversionService
{
    /// <summary>
    /// A price is considered stale when its provider timestamp is older than this threshold.
    /// 24 hours covers intra-day and overnight scenarios without falsely flagging a same-day
    /// regular close during the following morning's pre-market session.
    /// </summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(24);

    private readonly IExchangeRateService _exchangeRateService;
    private readonly TimeProvider _timeProvider;

    public StockQuoteConversionService(
        IExchangeRateService exchangeRateService,
        TimeProvider? timeProvider = null)
    {
        _exchangeRateService = exchangeRateService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        CurrencyConversionContext conversionContext,
        string priceSession = "REGULAR",
        DateTime? priceTimestampUtc = null,
        string? priceSource = null,
        string? delayWarning = null,
        decimal? rawDayHigh = null,
        decimal? rawDayLow = null)
    {
        var normalizedCurrentPrice = conversionContext.Normalize(rawCurrentPrice);
        var normalizedPreviousClose = conversionContext.Normalize(rawPreviousClose);
        var normalizedChange = normalizedCurrentPrice - normalizedPreviousClose;

        decimal? normalizedDayHigh = rawDayHigh.HasValue ? conversionContext.Normalize(rawDayHigh.Value) : null;
        decimal? normalizedDayLow = rawDayLow.HasValue ? conversionContext.Normalize(rawDayLow.Value) : null;

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
            RawDayHigh = rawDayHigh,
            RawDayLow = rawDayLow,
            NormalizedDayHigh = normalizedDayHigh,
            NormalizedDayLow = normalizedDayLow,
            DayHighEur = normalizedDayHigh.HasValue ? conversionContext.ConvertNormalizedToEur(normalizedDayHigh.Value) : null,
            DayLowEur = normalizedDayLow.HasValue ? conversionContext.ConvertNormalizedToEur(normalizedDayLow.Value) : null,
            MarketState = marketState,
            PriceSession = priceSession,
            PriceTimestampUtc = priceTimestampUtc,
            IsStale = ComputeIsStale(priceTimestampUtc) || delayWarning is not null,
            PriceSource = priceSource,
            DelayWarning = delayWarning,
            RateToEur = conversionContext.ExchangeRate.RateToEur,
            RateTimestampUtc = conversionContext.ExchangeRate.RateTimestampUtc,
            RateSource = conversionContext.ExchangeRate.Source,
            ConversionWarning = conversionContext.Warning
        };
    }

    private bool ComputeIsStale(DateTime? priceTimestampUtc)
    {
        if (priceTimestampUtc is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return (now - priceTimestampUtc.Value) > StaleThreshold;
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
            Volume = historicalPrice.Volume,
            IsQuoteDerived = historicalPrice.IsQuoteDerived
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
