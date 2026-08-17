using System.Reflection;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FinanceApp.Core.Tests;

public class MigrationDiscoveryTests
{
    [Fact]
    public void GetMigrations_Contains_AddStockTrackingAndMembershipHistory()
    {
        using var context = CreateContext();

        var migrations = context.Database.GetMigrations().ToList();
        Assert.Contains("20260816162000_AddStockTrackingAndMembershipHistory", migrations);

        var migrationType = typeof(AppDbContext).Assembly.GetType("FinanceApp.Data.Migrations.AddStockTrackingAndMembershipHistory");
        Assert.NotNull(migrationType);

        var dbContextAttribute = migrationType!.GetCustomAttribute<DbContextAttribute>();
        Assert.NotNull(dbContextAttribute);
        Assert.Equal(typeof(AppDbContext), dbContextAttribute!.ContextType);

        var migrationAttribute = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.NotNull(migrationAttribute);
        Assert.Equal("20260816162000_AddStockTrackingAndMembershipHistory", migrationAttribute!.Id);
    }

    [Fact]
    public void Model_Contains_StockTracking_And_MembershipHistory_Metadata()
    {
        using var context = CreateContext();

        var stockEntity = context.Model.FindEntityType(typeof(Stock));
        Assert.NotNull(stockEntity);

        var trackingStatus = stockEntity!.FindProperty(nameof(Stock.TrackingStatus));
        Assert.NotNull(trackingStatus);
        // ValueGeneratedNever: EF always includes TrackingStatus in INSERT statements,
        // regardless of the value. This prevents the bug where CatalogOnly = 0 (the CLR
        // default for int) was omitted from INSERT and MySQL substituted DEFAULT 1.
        Assert.Equal(ValueGenerated.Never, trackingStatus.ValueGenerated);
        // No explicit HasDefaultValue configured; GetDefaultValue returns the CLR default (0 = CatalogOnly).
        Assert.Equal(StockTrackingStatus.CatalogOnly, (StockTrackingStatus?)trackingStatus.GetDefaultValue());

        var providerSymbol = stockEntity.FindProperty(nameof(Stock.ProviderSymbol));
        Assert.NotNull(providerSymbol);
        Assert.Equal(50, providerSymbol.GetMaxLength());
        Assert.Contains(
            stockEntity.GetIndexes(),
            index => index.GetDatabaseName() == "IX_Stocks_ProviderSymbol"
                && index.GetFilter() == "`ProviderSymbol` IS NOT NULL"
                && index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(Stock.ProviderSymbol) }));

        var membershipEntity = context.Model.FindEntityType(typeof(StockMarketIndex));
        Assert.NotNull(membershipEntity);

        var importedAt = membershipEntity!.FindProperty(nameof(StockMarketIndex.ImportedAt));
        Assert.NotNull(importedAt);
        Assert.Equal("UTC_TIMESTAMP(6)", importedAt.GetDefaultValueSql());

        Assert.Equal(100, membershipEntity.FindProperty(nameof(StockMarketIndex.Source))?.GetMaxLength());
        Assert.Equal(100, membershipEntity.FindProperty(nameof(StockMarketIndex.ProviderConstituentKey))?.GetMaxLength());
        Assert.Contains(
            membershipEntity.GetIndexes(),
            index => index.GetDatabaseName() == "IX_StockMarketIndices_StockId_MarketIndexId"
                && index.Properties.Select(property => property.Name).SequenceEqual(new[]
                {
                    nameof(StockMarketIndex.StockId),
                    nameof(StockMarketIndex.MarketIndexId)
                }));
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new AppDbContext(options);
    }
}
