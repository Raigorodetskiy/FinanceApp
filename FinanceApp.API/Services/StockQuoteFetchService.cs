using FinanceApp.API.Models;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Http;

namespace FinanceApp.API.Services;

/// <summary>
/// Result of fetching a current stock quote.
/// </summary>
public sealed record StockQuoteFetchResult
{
    public StockQuoteResponse? Quote { get; init; }
    public bool IsSuccess => Quote is not null;
    public bool IsRateLimited { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static StockQuoteFetchResult Success(StockQuoteResponse quote) =>
        new() { Quote = quote, StatusCode = StatusCodes.Status200OK };

    public static StockQuoteFetchResult RateLimit(string message) =>
        new() { IsRateLimited = true, StatusCode = StatusCodes.Status429TooManyRequests, ErrorMessage = message };

    public static StockQuoteFetchResult Failure(int statusCode, string message) =>
        new() { StatusCode = statusCode, ErrorMessage = message };
}

public interface IStockQuoteFetchService
{
    Task<StockQuoteFetchResult> FetchAsync(
        string ticker,
        string exchange,
        string? finanzenNetSlug,
        CancellationToken cancellationToken = default);
}

public sealed class StockQuoteFetchService : IStockQuoteFetchService
{
    private readonly IFinnhubQuoteService _finnhubQuoteService;
    private readonly IYahooQuoteService _yahooQuoteService;
    private readonly IFinanzenNetQuoteService _finanzenNetQuoteService;
    private readonly IStockQuoteConversionService _stockQuoteConversionService;
    private readonly ILogger<StockQuoteFetchService> _logger;

    public StockQuoteFetchService(
        IFinnhubQuoteService finnhubQuoteService,
        IYahooQuoteService yahooQuoteService,
        IFinanzenNetQuoteService finanzenNetQuoteService,
        IStockQuoteConversionService stockQuoteConversionService,
        ILogger<StockQuoteFetchService> logger)
    {
        _finnhubQuoteService = finnhubQuoteService;
        _yahooQuoteService = yahooQuoteService;
        _finanzenNetQuoteService = finanzenNetQuoteService;
        _stockQuoteConversionService = stockQuoteConversionService;
        _logger = logger;
    }

    public async Task<StockQuoteFetchResult> FetchAsync(
        string ticker,
        string exchange,
        string? finanzenNetSlug,
        CancellationToken cancellationToken = default)
    {
        if (!StockExchanges.TryNormalize(exchange, out var normalizedExchange))
        {
            return StockQuoteFetchResult.Failure(StatusCodes.Status400BadRequest, "Unsupported exchange.");
        }

        var providerSymbol = StockExchanges.ResolveProviderSymbol(ticker, normalizedExchange);

        string? currency;
        string? estimateCurrency;
        decimal currentPrice;
        decimal previousClose;
        decimal percentChange;
        string marketState;
        string priceSession;
        DateTime? priceTimestampUtc;
        string? delayWarning = null;
        decimal? rawDayHigh = null;
        decimal? rawDayLow = null;

        if (normalizedExchange == StockExchanges.Frankfurt)
        {
            var quoteResult = await _yahooQuoteService.GetQuoteAsync(providerSymbol, cancellationToken);
            if (!quoteResult.IsSuccess || quoteResult.Quote is null)
            {
                if (quoteResult.StatusCode == StatusCodes.Status429TooManyRequests)
                {
                    return StockQuoteFetchResult.RateLimit(quoteResult.ErrorMessage ?? "Quote provider rate limit exceeded.");
                }

                return StockQuoteFetchResult.Failure(quoteResult.StatusCode, quoteResult.ErrorMessage ?? "Could not fetch current quote.");
            }

            var quote = quoteResult.Quote;
            currency = quote.Currency;
            estimateCurrency = quote.EstimateCurrency;
            currentPrice = quote.CurrentPrice;
            previousClose = quote.PreviousClose;
            percentChange = quote.PercentChange;
            marketState = quote.MarketState;
            priceSession = quote.PriceSession;
            priceTimestampUtc = quote.PriceTimestampUtc;
            delayWarning = quote.IsDelayed ? quote.DelayReason : null;
            rawDayHigh = quote.DayHigh;
            rawDayLow = quote.DayLow;
        }
        else
        {
            var quoteResult = await _finnhubQuoteService.GetQuoteAsync(ticker, cancellationToken);
            if (!quoteResult.IsSuccess || quoteResult.Quote is null)
            {
                if (quoteResult.StatusCode == StatusCodes.Status429TooManyRequests)
                {
                    return StockQuoteFetchResult.RateLimit(quoteResult.ErrorMessage ?? "Quote provider rate limit exceeded.");
                }

                return StockQuoteFetchResult.Failure(quoteResult.StatusCode, quoteResult.ErrorMessage ?? "Could not fetch current quote.");
            }

            var quote = quoteResult.Quote;
            currency = quote.Currency;
            estimateCurrency = quote.EstimateCurrency;
            currentPrice = quote.CurrentPrice;
            previousClose = quote.PreviousClose;
            percentChange = quote.PercentChange;
            marketState = quote.MarketState;
            priceSession = quote.PriceSession;
            priceTimestampUtc = quote.QuoteTimestampUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(quote.QuoteTimestampUnix).UtcDateTime
                : null;
            rawDayHigh = quote.DayHigh;
            rawDayLow = quote.DayLow;
        }

        string? priceSource = null;
        if (_finanzenNetQuoteService.IsEnabled && !string.IsNullOrWhiteSpace(finanzenNetSlug))
        {
            try
            {
                var fnResult = await _finanzenNetQuoteService.GetPreMarketQuoteAsync(finanzenNetSlug, cancellationToken);
                if (fnResult.IsSuccess &&
                    fnResult.Quote is { } fnQuote &&
                    fnQuote.PriceSession == "PRE" &&
                    fnQuote.Price > 0m)
                {
                    var fnPercentChange = previousClose > 0m
                        ? (fnQuote.Price - previousClose) / previousClose * 100m
                        : 0m;

                    currentPrice = fnQuote.Price;
                    percentChange = fnPercentChange;
                    priceSession = "PRE";
                    priceTimestampUtc = fnQuote.ProviderTimestampUtc;
                    priceSource = fnQuote.Source;
                    if (!string.IsNullOrWhiteSpace(fnQuote.Currency))
                    {
                        currency = fnQuote.Currency;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Enrichment failure must not fail the primary quote result.
                _logger.LogDebug(ex, "FinanzenNet enrichment failed for slug {Slug}", finanzenNetSlug);
            }
        }

        var conversionContext = await _stockQuoteConversionService.GetConversionContextAsync(
            currency,
            estimateCurrency,
            cancellationToken);

        var response = _stockQuoteConversionService.BuildQuoteResponse(
            providerSymbol,
            currentPrice,
            previousClose,
            percentChange,
            marketState,
            conversionContext,
            priceSession,
            priceTimestampUtc,
            priceSource,
            delayWarning,
            rawDayHigh,
            rawDayLow);

        return StockQuoteFetchResult.Success(response);
    }
}
