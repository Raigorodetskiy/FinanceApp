using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FinanceApp.Core.Tests;

public class TechnicalAnalysisSchemaMigrationTests
{
    private const string PreviousMigration = "20260819083726_AddPersistedDelayedQuoteMetadata";
    private const string CurrentMigration = "20260820043053_AddAdjustedCloseAndNonUniqueIsin";
    private const string DefaultConnectionString = "Server=example.invalid;Port=3306;Database=financeapp";

    [Fact]
    public void MigrationScript_ReplacesUniqueIsinIndex_AndAddsAdjustedClose()
    {
        using var context = CreateMySqlContext();
        var migrator = context.GetService<IMigrator>();

        var script = migrator.GenerateScript(PreviousMigration, CurrentMigration);

        Assert.Contains("DROP INDEX `IX_Stocks_Isin`", script);
        Assert.Contains("AdjustedClose", script);
        Assert.Contains("StockHistoricalPrices", script);
        Assert.Contains("CREATE INDEX `IX_Stocks_Isin` ON `Stocks` (`Isin`)", script);
        Assert.DoesNotContain("CREATE UNIQUE INDEX `IX_Stocks_Isin`", script);
    }

    [Fact]
    public void MigrationScript_Rollback_DropsAdjustedClose_AndRestoresUniqueIsinIndex()
    {
        using var context = CreateMySqlContext();
        var migrator = context.GetService<IMigrator>();

        var script = migrator.GenerateScript(CurrentMigration, PreviousMigration);

        Assert.Contains("DROP COLUMN `AdjustedClose`", script);
        Assert.Contains("CREATE UNIQUE INDEX `IX_Stocks_Isin`", script);
    }

    private static AppDbContext CreateMySqlContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(
                Environment.GetEnvironmentVariable("FINANCEAPP_TEST_MYSQL_CONNECTION")
                    ?? DefaultConnectionString,
                new MariaDbServerVersion(new Version(10, 5, 23)))
            .Options;

        return new AppDbContext(options);
    }
}
