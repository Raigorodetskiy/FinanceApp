using FinanceApp.API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class StockMetadataCuratedSnapshotServiceTests
{
    [Fact]
    public void FindByListingIdentity_ReturnsKnownCuratedIsin()
    {
        var service = new StockMetadataCuratedSnapshotService(
            new StubWebHostEnvironment { ContentRootPath = Path.Combine(FindRepositoryRoot(), "FinanceApp.API") },
            NullLogger<StockMetadataCuratedSnapshotService>.Instance);

        var candidate = service.FindByListingIdentity("SAP.DE", "Frankfurt");

        Assert.NotNull(candidate);
        Assert.Equal("DE0007164600", candidate!.Isin);
        Assert.Null(candidate.Wkn);
    }

    [Fact]
    public void FindByListingIdentity_ReturnsNullForUnknownListing()
    {
        var service = new StockMetadataCuratedSnapshotService(
            new StubWebHostEnvironment { ContentRootPath = Path.Combine(FindRepositoryRoot(), "FinanceApp.API") },
            NullLogger<StockMetadataCuratedSnapshotService>.Instance);

        var candidate = service.FindByListingIdentity("UNKNOWN.SYMBOL", "NYSE");

        Assert.Null(candidate);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FinanceApp.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "FinanceApp.API";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
