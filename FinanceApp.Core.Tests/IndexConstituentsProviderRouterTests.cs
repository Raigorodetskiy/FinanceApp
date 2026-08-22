using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace FinanceApp.Core.Tests;

public class IndexConstituentsProviderRouterTests
{
    [Fact]
    public async Task Router_UsesDaxProvider_ByCanonicalCode()
    {
        var djiaProvider = CreateDjiaProvider();
        var ndxProvider = CreateNasdaq100Provider();
        var spxProvider = CreateSp500Provider();
        var daxProvider = CreateDaxProvider();
        var router = new IndexConstituentsProviderRouter(djiaProvider, ndxProvider, spxProvider, daxProvider, new YahooIndexConstituentsProvider());
        var dax = new MarketIndex { Id = 7, Code = " dax ", NormalizedCode = " DaX " };

        var result = await router.GetConstituentsAsync(dax);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.Equal(DaxConstituentsProvider.CuratedProviderName, result.ProviderName);
    }

    [Fact]
    public async Task Router_UsesDjiaProvider_ByStableCode()
    {
        var provider = CreateDjiaProvider();
        var ndxProvider = CreateNasdaq100Provider();
        var spxProvider = CreateSp500Provider();
        var daxProvider = CreateDaxProvider();
        var router = new IndexConstituentsProviderRouter(provider, ndxProvider, spxProvider, daxProvider, new YahooIndexConstituentsProvider());
        var djia = new MarketIndex { Id = 999, Code = "djia", NormalizedCode = "DJIA" };

        var result = await router.GetConstituentsAsync(djia);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.Equal(DowJonesIndustrialAverageConstituentsProvider.CuratedProviderName, result.ProviderName);
    }

    [Fact]
    public async Task Router_LeavesOtherIndicesOnUnsupportedFallback()
    {
        var provider = CreateDjiaProvider();
        var ndxProvider = CreateNasdaq100Provider();
        var spxProvider = CreateSp500Provider();
        var daxProvider = CreateDaxProvider();
        var router = new IndexConstituentsProviderRouter(provider, ndxProvider, spxProvider, daxProvider, new YahooIndexConstituentsProvider());
        var unknown = new MarketIndex { Id = 1, Code = "MSCI", NormalizedCode = "MSCI" };

        var result = await router.GetConstituentsAsync(unknown);

        Assert.Equal(IndexConstituentsStatus.Unsupported, result.Status);
        Assert.Equal("Yahoo Finance", result.ProviderName);
    }

    [Fact]
    public async Task Router_UsesNasdaq100Provider_ByCanonicalCode()
    {
        var djiaProvider = CreateDjiaProvider();
        var ndxProvider = CreateNasdaq100Provider();
        var spxProvider = CreateSp500Provider();
        var daxProvider = CreateDaxProvider();
        var router = new IndexConstituentsProviderRouter(djiaProvider, ndxProvider, spxProvider, daxProvider, new YahooIndexConstituentsProvider());
        var ndx = new MarketIndex { Id = 4, Code = " ndx ", NormalizedCode = " nDx " };

        var result = await router.GetConstituentsAsync(ndx);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.Equal(Nasdaq100ConstituentsProvider.CuratedProviderName, result.ProviderName);
    }

    [Fact]
    public async Task Router_UsesSp500Provider_ByCanonicalCode()
    {
        var djiaProvider = CreateDjiaProvider();
        var ndxProvider = CreateNasdaq100Provider();
        var spxProvider = CreateSp500Provider();
        var daxProvider = CreateDaxProvider();
        var router = new IndexConstituentsProviderRouter(djiaProvider, ndxProvider, spxProvider, daxProvider, new YahooIndexConstituentsProvider());
        var spx = new MarketIndex { Id = 5, Code = " spx ", NormalizedCode = " SpX " };

        var result = await router.GetConstituentsAsync(spx);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.Equal(Sp500ConstituentsProvider.CuratedProviderName, result.ProviderName);
    }

    [Fact]
    public async Task DjiCuratedSnapshot_HasExpectedShape_AsOfAndUniqueConstituents()
    {
        var provider = CreateDjiaProvider();
        var djia = new MarketIndex { Code = "DJIA", NormalizedCode = "DJIA" };

        var result = await provider.GetConstituentsAsync(djia);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.True(result.IsCuratedSnapshot);
        Assert.NotNull(result.AsOfDate);
        Assert.Equal(30, result.Constituents.Count);
        Assert.All(result.Constituents, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Ticker));
            Assert.False(string.IsNullOrWhiteSpace(c.CompanyName));
            Assert.False(string.IsNullOrWhiteSpace(c.ProviderSymbol));
            Assert.True(StockExchanges.TryNormalize(c.ProviderExchange, out _));
        });
        Assert.Equal(
            30,
            result.Constituents
                .Select(c => $"{c.ProviderSymbol}|{c.ProviderExchange}")
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task Nasdaq100CuratedSnapshot_HasExpectedShape_AsOfAndUniqueConstituents()
    {
        var provider = CreateNasdaq100Provider();
        var ndx = new MarketIndex { Code = "NDX", NormalizedCode = "NDX" };

        var result = await provider.GetConstituentsAsync(ndx);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.True(result.IsCuratedSnapshot);
        Assert.NotNull(result.AsOfDate);
        Assert.NotNull(result.SourceUrl);
        Assert.Equal(101, result.Constituents.Count);
        Assert.All(result.Constituents, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Ticker));
            Assert.False(string.IsNullOrWhiteSpace(c.CompanyName));
            Assert.False(string.IsNullOrWhiteSpace(c.ProviderSymbol));
            Assert.True(StockExchanges.TryNormalize(c.ProviderExchange, out _));
        });
        Assert.Equal(
            101,
            result.Constituents
                .Select(c => $"{c.ProviderSymbol}|{c.ProviderExchange}")
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Contains(result.Constituents, c => c.Ticker == "GOOG");
        Assert.Contains(result.Constituents, c => c.Ticker == "GOOGL");
    }

    [Fact]
    public async Task Sp500CuratedSnapshot_HasExpectedShape_AsOfAndUniqueConstituents()
    {
        var provider = CreateSp500Provider();
        var spx = new MarketIndex { Code = "SPX", NormalizedCode = "SPX" };

        var result = await provider.GetConstituentsAsync(spx);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.True(result.IsCuratedSnapshot);
        Assert.NotNull(result.AsOfDate);
        Assert.NotNull(result.SourceUrl);
        // S&P 500 snapshot includes 503 securities.
        Assert.Equal(503, result.Constituents.Count);
        Assert.All(result.Constituents, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Ticker));
            Assert.False(string.IsNullOrWhiteSpace(c.CompanyName));
            Assert.False(string.IsNullOrWhiteSpace(c.ProviderSymbol));
            Assert.True(StockExchanges.TryNormalize(c.ProviderExchange, out _));
        });
        Assert.Equal(
            503,
            result.Constituents
                .Select(c => $"{c.ProviderSymbol}|{c.ProviderExchange}")
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task Sp500CuratedSnapshot_HasRequiredRepresentativeSymbols()
    {
        var provider = CreateSp500Provider();
        var spx = new MarketIndex { Code = "SPX", NormalizedCode = "SPX" };

        var result = await provider.GetConstituentsAsync(spx);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.Contains(result.Constituents, c => c.Ticker == "AAPL");
        Assert.Contains(result.Constituents, c => c.Ticker == "MSFT");
        Assert.Contains(result.Constituents, c => c.Ticker == "AMZN");
        Assert.Contains(result.Constituents, c => c.Ticker == "NVDA");
        // Class-share tickers must be present with correct providerSymbol (Yahoo Finance convention)
        Assert.Contains(result.Constituents, c => c.Ticker == "BRK.B" && c.ProviderSymbol == "BRK-B");
        Assert.Contains(result.Constituents, c => c.Ticker == "BF.B" && c.ProviderSymbol == "BF-B");
    }

    [Fact]
    public async Task Sp500CuratedSnapshot_ExposesExpectedSectorVocabulary()
    {
        var provider = CreateSp500Provider();
        var spx = new MarketIndex { Code = "SPX", NormalizedCode = "SPX" };

        var result = await provider.GetConstituentsAsync(spx);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        var sectors = result.Constituents
            .Select(c => c.Sector)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(11, sectors.Count);
        Assert.Contains("Information Technology", sectors);
        Assert.Contains("Health Care", sectors);
        Assert.Contains("Real Estate", sectors);
    }

    [Fact]
    public async Task Sp500CuratedSnapshot_HandlesKnownAnomalies_WithoutInventingIdentifiers()
    {
        var provider = CreateSp500Provider();
        var spx = new MarketIndex { Code = "SPX", NormalizedCode = "SPX" };

        var result = await provider.GetConstituentsAsync(spx);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        var rmd = Assert.Single(result.Constituents, c => c.Ticker == "RMD");
        Assert.Equal("ResMed", rmd.CompanyName);

        var vmrk = Assert.Single(result.Constituents, c => c.Ticker == "VMRK");
        Assert.Null(vmrk.Isin);
        Assert.Null(vmrk.Wkn);
    }

    [Fact]
    public async Task Sp500CuratedSnapshot_InvalidHeader_ReturnsProviderFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"financeapp-sp500-bad-header-{Guid.NewGuid():N}");
        var dataDir = Path.Combine(root, "Data", "index-constituents");
        Directory.CreateDirectory(dataDir);
        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "SP500_2026-08-21.csv"),
            "\uFEFFTicker,Company,ISIN,WKN,Sector\nAAPL;Apple Inc.;US0378331005;865985;Information Technology\n");
        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "sp500.curated.snapshot.json"),
            "{\"constituents\":[]}");

        try
        {
            var provider = new Sp500ConstituentsProvider(
                new StubWebHostEnvironment { ContentRootPath = root },
                NullLogger<Sp500ConstituentsProvider>.Instance,
                root);

            var result = await provider.GetConstituentsAsync(new MarketIndex { Code = "SPX", NormalizedCode = "SPX" });

            Assert.Equal(IndexConstituentsStatus.ProviderFailure, result.Status);
            Assert.Contains("неожиданный заголовок", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Sp500CuratedSnapshot_HasSourceUrlAndAsOf()
    {
        var provider = CreateSp500Provider();
        var spx = new MarketIndex { Code = "SPX", NormalizedCode = "SPX" };

        var result = await provider.GetConstituentsAsync(spx);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.SourceUrl));
        Assert.NotNull(result.AsOfDate);
        Assert.True(result.IsCuratedSnapshot);
        Assert.False(result.IsStale);
    }

    [Fact]
    public async Task DaxCuratedSnapshot_HasExpectedShape_AsOfAndUniqueConstituents()
    {
        var provider = CreateDaxProvider();
        var dax = new MarketIndex { Code = "DAX", NormalizedCode = "DAX" };

        var result = await provider.GetConstituentsAsync(dax);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.True(result.IsCuratedSnapshot);
        Assert.NotNull(result.AsOfDate);
        Assert.NotNull(result.SourceUrl);
        Assert.Equal(40, result.Constituents.Count);
        Assert.All(result.Constituents, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Ticker));
            Assert.False(string.IsNullOrWhiteSpace(c.CompanyName));
            Assert.False(string.IsNullOrWhiteSpace(c.ProviderSymbol));
            Assert.True(StockExchanges.TryNormalize(c.ProviderExchange, out var ex) && ex == StockExchanges.Frankfurt);
        });
        Assert.Equal(
            40,
            result.Constituents
                .Select(c => $"{c.ProviderSymbol}|{c.ProviderExchange}")
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task DaxCuratedSnapshot_HasRepresentativeSymbols()
    {
        var provider = CreateDaxProvider();
        var dax = new MarketIndex { Code = "DAX", NormalizedCode = "DAX" };

        var result = await provider.GetConstituentsAsync(dax);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        // Representative well-known DAX constituents
        Assert.Contains(result.Constituents, c => c.Ticker == "SAP" && c.ProviderSymbol == "SAP.DE");
        Assert.Contains(result.Constituents, c => c.Ticker == "SIE" && c.ProviderSymbol == "SIE.DE");
        Assert.Contains(result.Constituents, c => c.Ticker == "ALV" && c.ProviderSymbol == "ALV.DE");
        Assert.Contains(result.Constituents, c => c.Ticker == "BMW" && c.ProviderSymbol == "BMW.DE");
        Assert.Contains(result.Constituents, c => c.Ticker == "RHM" && c.ProviderSymbol == "RHM.DE");
    }

    [Fact]
    public async Task DaxCuratedSnapshot_HasDotDeProviderSymbolConvention()
    {
        var provider = CreateDaxProvider();
        var dax = new MarketIndex { Code = "DAX", NormalizedCode = "DAX" };

        var result = await provider.GetConstituentsAsync(dax);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        // All DAX provider symbols must end with .DE (Yahoo Finance convention for Xetra/Frankfurt)
        Assert.All(result.Constituents, c =>
            Assert.True(c.ProviderSymbol.EndsWith(".DE", StringComparison.OrdinalIgnoreCase),
                $"Expected .DE suffix on providerSymbol for {c.Ticker}, got: {c.ProviderSymbol}"));
    }

    [Fact]
    public async Task DaxCuratedSnapshot_SpecialTickerCases_AreCorrect()
    {
        var provider = CreateDaxProvider();
        var dax = new MarketIndex { Code = "DAX", NormalizedCode = "DAX" };

        var result = await provider.GetConstituentsAsync(dax);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        // Numeric-prefixed ticker
        Assert.Contains(result.Constituents, c => c.Ticker == "1COV" && c.ProviderSymbol == "1COV.DE");
        // Preference-share tickers with numeric suffix
        Assert.Contains(result.Constituents, c => c.Ticker == "VOW3" && c.ProviderSymbol == "VOW3.DE");
        Assert.Contains(result.Constituents, c => c.Ticker == "HEN3" && c.ProviderSymbol == "HEN3.DE");
        Assert.Contains(result.Constituents, c => c.Ticker == "MUV2" && c.ProviderSymbol == "MUV2.DE");
        // P911 (Porsche AG)
        Assert.Contains(result.Constituents, c => c.Ticker == "P911" && c.ProviderSymbol == "P911.DE");
    }

    [Fact]
    public async Task DaxCuratedSnapshot_HasSourceUrlAndAsOf()
    {
        var provider = CreateDaxProvider();
        var dax = new MarketIndex { Code = "DAX", NormalizedCode = "DAX" };

        var result = await provider.GetConstituentsAsync(dax);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.SourceUrl));
        Assert.NotNull(result.AsOfDate);
        Assert.True(result.IsCuratedSnapshot);
        Assert.False(result.IsStale);
    }

    [Fact]
    public async Task DaxCuratedSnapshot_MrkTicker_IsNotMergedWithUsMrkNyse()
    {
        // Merck KGaA (Frankfurt, MRK.DE) must not have the same identity as Merck & Co. (NYSE, MRK)
        var provider = CreateDaxProvider();
        var dax = new MarketIndex { Code = "DAX", NormalizedCode = "DAX" };

        var result = await provider.GetConstituentsAsync(dax);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        var mrkDax = result.Constituents.SingleOrDefault(c => c.Ticker == "MRK");
        Assert.NotNull(mrkDax);
        Assert.Equal("MRK.DE", mrkDax.ProviderSymbol);
        Assert.Equal(StockExchanges.Frankfurt, mrkDax.ProviderExchange);
        // The identity MRK.DE|Frankfurt is distinct from MRK|NYSE used by Merck & Co.
        Assert.NotEqual("MRK|NYSE", $"{mrkDax.ProviderSymbol}|{mrkDax.ProviderExchange}");
    }

    [Fact]
    public async Task DjiProvider_PrefersAppBaseDirectorySnapshot_WhenBothLayoutsExist()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"financeapp-djia-base-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(Path.GetTempPath(), $"financeapp-djia-content-{Guid.NewGuid():N}");
        try
        {
            CreateSnapshot(baseDir, "https://example.test/base");
            CreateSnapshot(contentRoot, "https://example.test/content");

            var provider = new DowJonesIndustrialAverageConstituentsProvider(
                new StubWebHostEnvironment { ContentRootPath = contentRoot },
                NullLogger<DowJonesIndustrialAverageConstituentsProvider>.Instance,
                baseDir);

            var result = await provider.GetConstituentsAsync(new MarketIndex { Code = "DJIA", NormalizedCode = "DJIA" });

            Assert.Equal(IndexConstituentsStatus.Success, result.Status);
            Assert.Equal("https://example.test/base", result.SourceUrl);
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, true);
        }
    }

    [Fact]
    public async Task DjiProvider_FallsBackToContentRoot_WhenAppBaseSnapshotMissing()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"financeapp-djia-base-missing-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(Path.GetTempPath(), $"financeapp-djia-content-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(baseDir);
            CreateSnapshot(contentRoot, "https://example.test/content-root");

            var provider = new DowJonesIndustrialAverageConstituentsProvider(
                new StubWebHostEnvironment { ContentRootPath = contentRoot },
                NullLogger<DowJonesIndustrialAverageConstituentsProvider>.Instance,
                baseDir);

            var result = await provider.GetConstituentsAsync(new MarketIndex { Code = "DJIA", NormalizedCode = "DJIA" });

            Assert.Equal(IndexConstituentsStatus.Success, result.Status);
            Assert.Equal("https://example.test/content-root", result.SourceUrl);
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, true);
        }
    }

    private static DowJonesIndustrialAverageConstituentsProvider CreateDjiaProvider()
    {
        return new DowJonesIndustrialAverageConstituentsProvider(
            new StubWebHostEnvironment { ContentRootPath = Path.Combine(FindRepositoryRoot(), "FinanceApp.API") },
            NullLogger<DowJonesIndustrialAverageConstituentsProvider>.Instance);
    }

    private static Nasdaq100ConstituentsProvider CreateNasdaq100Provider()
    {
        return new Nasdaq100ConstituentsProvider(
            new StubWebHostEnvironment { ContentRootPath = Path.Combine(FindRepositoryRoot(), "FinanceApp.API") },
            NullLogger<Nasdaq100ConstituentsProvider>.Instance);
    }

    private static Sp500ConstituentsProvider CreateSp500Provider()
    {
        return new Sp500ConstituentsProvider(
            new StubWebHostEnvironment { ContentRootPath = Path.Combine(FindRepositoryRoot(), "FinanceApp.API") },
            NullLogger<Sp500ConstituentsProvider>.Instance);
    }

    private static DaxConstituentsProvider CreateDaxProvider()
    {
        return new DaxConstituentsProvider(
            new StubWebHostEnvironment { ContentRootPath = Path.Combine(FindRepositoryRoot(), "FinanceApp.API") },
            NullLogger<DaxConstituentsProvider>.Instance);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FinanceApp.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
            throw new InvalidOperationException("Repository root with FinanceApp.sln not found.");

        return dir.FullName;
    }

    private static void CreateSnapshot(string rootPath, string sourceUrl)
    {
        var snapshotPath = Path.Combine(rootPath, "Data", "index-constituents", "djia.curated.snapshot.json");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        var payload = """
        {
          "sourceName": "Test Source",
          "sourceUrl": "SOURCE_URL",
          "asOfDate": "2026-08-16T00:00:00Z",
          "constituents": [
            {
              "ticker": "AAPL",
              "providerSymbol": "AAPL",
              "companyName": "Apple Inc.",
              "exchange": "NASDAQ",
              "isin": "US0378331005"
            }
          ]
        }
        """.Replace("SOURCE_URL", sourceUrl, StringComparison.Ordinal);

        var validated = JsonSerializer.Deserialize<object>(payload);
        File.WriteAllText(snapshotPath, JsonSerializer.Serialize(validated));
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
