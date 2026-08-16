using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class IndexConstituentsProviderRouterTests
{
    [Fact]
    public async Task Router_UsesDjiaProvider_ByStableCode()
    {
        var provider = CreateDjiaProvider();
        var router = new IndexConstituentsProviderRouter(provider, new YahooIndexConstituentsProvider());
        var djia = new MarketIndex { Id = 999, Code = "djia", NormalizedCode = "DJIA" };

        var result = await router.GetConstituentsAsync(djia);

        Assert.Equal(IndexConstituentsStatus.Success, result.Status);
        Assert.Equal(DowJonesIndustrialAverageConstituentsProvider.CuratedProviderName, result.ProviderName);
    }

    [Fact]
    public async Task Router_LeavesOtherIndicesOnUnsupportedFallback()
    {
        var provider = CreateDjiaProvider();
        var router = new IndexConstituentsProviderRouter(provider, new YahooIndexConstituentsProvider());
        var spx = new MarketIndex { Id = 1, Code = "SPX", NormalizedCode = "SPX" };

        var result = await router.GetConstituentsAsync(spx);

        Assert.Equal(IndexConstituentsStatus.Unsupported, result.Status);
        Assert.Equal("Yahoo Finance", result.ProviderName);
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

    private static DowJonesIndustrialAverageConstituentsProvider CreateDjiaProvider()
    {
        return new DowJonesIndustrialAverageConstituentsProvider(
            new StubWebHostEnvironment { ContentRootPath = Path.Combine(FindRepositoryRoot(), "FinanceApp.API") },
            NullLogger<DowJonesIndustrialAverageConstituentsProvider>.Instance);
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
