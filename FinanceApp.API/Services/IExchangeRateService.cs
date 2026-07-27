namespace FinanceApp.API.Services;

public interface IExchangeRateService
{
    Task<ExchangeRateResult> GetRateToEurAsync(string? sourceCurrency, CancellationToken cancellationToken = default);
}

public sealed record ExchangeRateResult(
    string? SourceCurrency,
    decimal? RateToEur,
    DateTime? RateTimestampUtc,
    string? Source,
    string? Error)
{
    public bool IsAvailable => RateToEur.HasValue;
}
