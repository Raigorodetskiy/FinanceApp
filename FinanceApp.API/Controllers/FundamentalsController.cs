using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/Stocks/{stockId}/fundamentals")]
[Authorize]
public class FundamentalsController : ControllerBase
{
    private readonly IFundamentalsService _fundamentalsService;

    public FundamentalsController(IFundamentalsService fundamentalsService)
    {
        _fundamentalsService = fundamentalsService;
    }

    [HttpGet]
    public async Task<ActionResult<FundamentalsResponse>> Get(int stockId, CancellationToken ct)
    {
        try
        {
            var result = await _fundamentalsService.GetFundamentalsAsync(stockId, ct);
            return Ok(ToResponse(stockId, result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<FundamentalsResponse>> Refresh(int stockId, CancellationToken ct)
    {
        try
        {
            var result = await _fundamentalsService.RefreshFundamentalsAsync(stockId, ct);
            return Ok(ToResponse(stockId, result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static FundamentalsResponse ToResponse(int stockId, FundamentalsResult result)
    {
        var snapshot = result.Snapshot;
        return new FundamentalsResponse
        {
            StockId = stockId,
            State = result.State.ToString(),
            WarningMessage = result.WarningMessage,
            Snapshot = snapshot is null
                ? null
                : new FundamentalsSnapshotDto
                {
                    Id = snapshot.Id,
                    SourceSymbol = snapshot.SourceSymbol,
                    MarketCap = snapshot.MarketCap,
                    EnterpriseValue = snapshot.EnterpriseValue,
                    TotalDebt = snapshot.TotalDebt,
                    CashAndEquivalents = snapshot.CashAndEquivalents,
                    RevenueTtm = snapshot.RevenueTtm,
                    NetIncomeTtm = snapshot.NetIncomeTtm,
                    EbitdaTtm = snapshot.EbitdaTtm,
                    OperatingIncomeTtm = snapshot.OperatingIncomeTtm,
                    FreeCashFlowTtm = snapshot.FreeCashFlowTtm,
                    TotalAssets = snapshot.TotalAssets,
                    TotalLiabilities = snapshot.TotalLiabilities,
                    PeRatio = snapshot.PeRatio,
                    ForwardPeRatio = snapshot.ForwardPeRatio,
                    PbRatio = snapshot.PbRatio,
                    DividendYield = snapshot.DividendYield,
                    Currency = snapshot.Currency,
                    Source = snapshot.Source,
                    AsOfDate = snapshot.AsOfDate,
                    FetchedAtUtc = snapshot.FetchedAtUtc
                },
            Periods = snapshot is null
                ? Array.Empty<FinancialPeriodDto>()
                : snapshot.Periods
                    .OrderByDescending(x => x.PeriodEndDate)
                    .ThenByDescending(x => x.PeriodType)
                    .Select(x => new FinancialPeriodDto
                    {
                        Id = x.Id,
                        PeriodType = x.PeriodType.ToString(),
                        FiscalYear = x.FiscalYear,
                        FiscalQuarter = x.FiscalQuarter,
                        PeriodEndDate = x.PeriodEndDate,
                        ReportedCurrency = x.ReportedCurrency,
                        Revenue = x.Revenue,
                        OperatingIncome = x.OperatingIncome,
                        NetIncome = x.NetIncome,
                        EpsReported = x.EpsReported,
                        EpsEstimate = x.EpsEstimate,
                        Ebitda = x.Ebitda,
                        TotalDebt = x.TotalDebt,
                        TotalAssets = x.TotalAssets,
                        TotalLiabilities = x.TotalLiabilities,
                        FreeCashFlow = x.FreeCashFlow,
                        Source = x.Source,
                        AsOfDate = x.AsOfDate,
                        FetchedAtUtc = x.FetchedAtUtc
                    })
                    .ToList(),
            EarningsEvents = snapshot is null
                ? Array.Empty<EarningsEventDto>()
                : snapshot.EarningsEvents
                    .OrderBy(x => x.ReportDate ?? DateTime.MaxValue)
                    .ThenBy(x => x.FiscalPeriod)
                    .Select(x => new EarningsEventDto
                    {
                        Id = x.Id,
                        ReportDate = x.ReportDate,
                        ReportDateEnd = x.ReportDateEnd,
                        DateStatus = x.DateStatus.ToString(),
                        EpsEstimate = x.EpsEstimate,
                        EpsReported = x.EpsReported,
                        RevenueEstimate = x.RevenueEstimate,
                        RevenueReported = x.RevenueReported,
                        FiscalPeriod = x.FiscalPeriod,
                        Source = x.Source,
                        FetchedAtUtc = x.FetchedAtUtc
                    })
                    .ToList()
        };
    }
}
