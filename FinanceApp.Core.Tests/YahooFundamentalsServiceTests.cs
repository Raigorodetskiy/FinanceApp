using System.Net;
using System.Text;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Core.Tests;

public class YahooFundamentalsServiceTests
{
    private const string CompleteResponse = """
        {
          "quoteSummary": {
            "result": [{
              "summaryDetail": {
                "marketCap": { "raw": 1250000000000 },
                "trailingPE": { "raw": 19.8754 },
                "forwardPE": { "raw": 17.1254 },
                "priceToBook": { "raw": 4.5678 },
                "dividendYield": { "raw": 0.0134 },
                "currency": "USD"
              },
              "financialData": {
                "totalCash": { "raw": 45000000000 },
                "totalDebt": { "raw": 12000000000 },
                "ebitda": { "raw": 17000000000 },
                "freeCashflow": { "raw": 11000000000 },
                "financialCurrency": "USD"
              },
              "defaultKeyStatistics": {
                "enterpriseValue": { "raw": 1290000000000 },
                "priceToBook": { "raw": 4.5678 }
              },
              "incomeStatementHistory": {
                "incomeStatementHistory": [
                  {
                    "endDate": { "raw": 1735603200 },
                    "totalRevenue": { "raw": 55000000000 },
                    "operatingIncome": { "raw": 13000000000 },
                    "netIncome": { "raw": 9000000000 },
                    "ebitda": { "raw": 17000000000 },
                    "currencyCode": "USD"
                  }
                ]
              },
              "incomeStatementHistoryQuarterly": {
                "incomeStatementHistory": [
                  {
                    "endDate": { "raw": 1735603200 },
                    "totalRevenue": { "raw": 14000000000 },
                    "operatingIncome": { "raw": 3300000000 },
                    "netIncome": { "raw": 2200000000 },
                    "ebitda": { "raw": 4300000000 },
                    "currencyCode": "USD"
                  },
                  {
                    "endDate": { "raw": 1727740800 },
                    "totalRevenue": { "raw": 13800000000 },
                    "operatingIncome": { "raw": 3200000000 },
                    "netIncome": { "raw": 2100000000 },
                    "ebitda": { "raw": 4200000000 },
                    "currencyCode": "USD"
                  },
                  {
                    "endDate": { "raw": 1719792000 },
                    "totalRevenue": { "raw": 13500000000 },
                    "operatingIncome": { "raw": 3100000000 },
                    "netIncome": { "raw": 2050000000 },
                    "ebitda": { "raw": 4100000000 },
                    "currencyCode": "USD"
                  },
                  {
                    "endDate": { "raw": 1711843200 },
                    "totalRevenue": { "raw": 13200000000 },
                    "operatingIncome": { "raw": 3000000000 },
                    "netIncome": { "raw": 2000000000 },
                    "ebitda": { "raw": 4000000000 },
                    "currencyCode": "USD"
                  }
                ]
              },
              "balanceSheetHistory": {
                "balanceSheetStatements": [
                  {
                    "endDate": { "raw": 1735603200 },
                    "totalAssets": { "raw": 98000000000 },
                    "totalLiab": { "raw": 41000000000 },
                    "totalDebt": { "raw": 12000000000 },
                    "currencyCode": "USD"
                  }
                ]
              },
              "balanceSheetHistoryQuarterly": {
                "balanceSheetStatements": [
                  {
                    "endDate": { "raw": 1735603200 },
                    "totalAssets": { "raw": 98000000000 },
                    "totalLiab": { "raw": 41000000000 },
                    "totalDebt": { "raw": 12000000000 },
                    "currencyCode": "USD"
                  },
                  {
                    "endDate": { "raw": 1727740800 },
                    "totalAssets": { "raw": 96000000000 },
                    "totalLiab": { "raw": 40500000000 },
                    "totalDebt": { "raw": 11900000000 },
                    "currencyCode": "USD"
                  }
                ]
              },
              "cashflowStatementQuarterly": {
                "cashflowStatements": [
                  {
                    "endDate": { "raw": 1735603200 },
                    "capitalExpenditures": { "raw": -500000000 },
                    "totalCashFromOperatingActivities": { "raw": 3300000000 },
                    "currencyCode": "USD"
                  },
                  {
                    "endDate": { "raw": 1727740800 },
                    "capitalExpenditures": { "raw": -450000000 },
                    "totalCashFromOperatingActivities": { "raw": 3000000000 },
                    "currencyCode": "USD"
                  },
                  {
                    "endDate": { "raw": 1719792000 },
                    "capitalExpenditures": { "raw": -425000000 },
                    "totalCashFromOperatingActivities": { "raw": 2900000000 },
                    "currencyCode": "USD"
                  },
                  {
                    "endDate": { "raw": 1711843200 },
                    "capitalExpenditures": { "raw": -400000000 },
                    "totalCashFromOperatingActivities": { "raw": 2800000000 },
                    "currencyCode": "USD"
                  }
                ]
              },
              "earningsHistory": {
                "history": [
                  {
                    "quarter": { "raw": 1735603200, "fmt": "4Q2024" },
                    "epsEstimate": { "raw": 2.11 },
                    "epsActual": { "raw": 2.25 }
                  }
                ]
              },
              "earningsTrend": {
                "trend": [
                  {
                    "period": "+1q",
                    "endDate": { "raw": 1743379200 },
                    "earningsEstimate": { "avg": { "raw": 2.31 } },
                    "revenueEstimate": { "avg": { "raw": 14500000000 } }
                  }
                ]
              },
              "calendarEvents": {
                "earnings": {
                  "earningsDate": [
                    { "raw": 1743379200 },
                    { "raw": 1743465600 }
                  ]
                }
              }
            }]
          }
        }
        """;

    [Fact]
    public async Task GetFundamentalsAsync_ParsesCompleteResponse()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(CompleteResponse);
        var service = CreateService(handler);

        var result = await service.GetFundamentalsAsync("STX");

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<CompanyFundamentalsSnapshot>(result.Snapshot);
        Assert.Equal("STX", snapshot.SourceSymbol);
        Assert.Equal(1_250_000_000_000m, snapshot.MarketCap);
        Assert.Equal(1_290_000_000_000m, snapshot.EnterpriseValue);
        Assert.Equal(12_000_000_000m, snapshot.TotalDebt);
        Assert.Equal(45_000_000_000m, snapshot.CashAndEquivalents);
        Assert.Equal(54_500_000_000m, snapshot.RevenueTtm);
        Assert.Equal(8_350_000_000m, snapshot.NetIncomeTtm);
        Assert.Equal(16_600_000_000m, snapshot.EbitdaTtm);
        Assert.Equal(10_225_000_000m, snapshot.FreeCashFlowTtm);
        Assert.Equal("USD", snapshot.Currency);
        Assert.Equal(1, snapshot.Periods.Count(x => x.PeriodType == PeriodType.Annual));
        Assert.Equal(4, snapshot.Periods.Count(x => x.PeriodType == PeriodType.Quarterly));
        Assert.Contains(snapshot.EarningsEvents, x => x.DateStatus == EarningsDateStatus.Confirmed && x.EpsReported == 2.25m);
    }

    [Fact]
    public async Task GetFundamentalsAsync_MissingModules_LeavesFieldsNull()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson("""{"quoteSummary":{"result":[{}]}}""");
        var service = CreateService(handler);

        var result = await service.GetFundamentalsAsync("STX");

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<CompanyFundamentalsSnapshot>(result.Snapshot);
        Assert.Null(snapshot.MarketCap);
        Assert.Null(snapshot.TotalDebt);
        Assert.Empty(snapshot.Periods);
        Assert.Empty(snapshot.EarningsEvents);
    }

    [Fact]
    public async Task GetFundamentalsAsync_LargeValues_DoNotOverflow()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(CompleteResponse);
        var service = CreateService(handler);

        var result = await service.GetFundamentalsAsync("BIG");

        Assert.True(result.IsSuccess);
        Assert.Equal(1_250_000_000_000m, result.Snapshot!.MarketCap);
    }

    [Fact]
    public async Task GetFundamentalsAsync_RetainsAnnualAndQuarterlyPeriodsSeparately()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(CompleteResponse);
        var service = CreateService(handler);

        var result = await service.GetFundamentalsAsync("STX");

        var annual = result.Snapshot!.Periods.Where(x => x.PeriodType == PeriodType.Annual).ToList();
        var quarterly = result.Snapshot.Periods.Where(x => x.PeriodType == PeriodType.Quarterly).ToList();

        Assert.Single(annual);
        Assert.Equal(4, quarterly.Count);
        Assert.All(quarterly, period => Assert.Equal(PeriodType.Quarterly, period.PeriodType));
    }

    [Fact]
    public async Task GetFundamentalsAsync_EstimatedEarnings_AreNotMarkedConfirmed()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(CompleteResponse);
        var service = CreateService(handler);

        var result = await service.GetFundamentalsAsync("STX");

        var estimatedEvent = Assert.Single(result.Snapshot!.EarningsEvents, x => x.FiscalPeriod == "+1q");
        Assert.Equal(EarningsDateStatus.Estimated, estimatedEvent.DateStatus);
        Assert.Null(estimatedEvent.EpsReported);
    }

    private static YahooFundamentalsService CreateService(HttpMessageHandler handler)
    {
        var factory = new FixedHttpClientFactory(new HttpClient(handler));
        var coordinator = new YahooRequestCoordinator(
            factory,
            NullLogger<YahooRequestCoordinator>.Instance,
            Options.Create(new YahooFinanceOptions
            {
                MinRequestInterval = TimeSpan.Zero,
                CooldownDuration = TimeSpan.FromMinutes(30),
                QuoteCacheDuration = TimeSpan.Zero,
                FundamentalsCacheDuration = TimeSpan.FromHours(24),
                EarningsCacheDuration = TimeSpan.FromHours(6),
                RequestTimeout = TimeSpan.FromSeconds(10)
            }));

        return new YahooFundamentalsService(
            coordinator,
            NullLogger<YahooFundamentalsService>.Instance,
            Options.Create(new YahooFinanceOptions
            {
                MinRequestInterval = TimeSpan.Zero,
                CooldownDuration = TimeSpan.FromMinutes(30),
                QuoteCacheDuration = TimeSpan.Zero,
                FundamentalsCacheDuration = TimeSpan.FromHours(24),
                EarningsCacheDuration = TimeSpan.FromHours(6),
                RequestTimeout = TimeSpan.FromSeconds(10)
            }));
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FixedHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public void EnqueueJson(string payload)
        {
            _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
    }
}
