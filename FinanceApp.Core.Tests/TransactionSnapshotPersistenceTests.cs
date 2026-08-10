using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FinanceApp.Core.Tests;

public class TransactionSnapshotPersistenceTests
{
    private const string PreviousMigration = "20260801120000_AddStockPriceSnapshot";
    private const string CurrentMigration = "20260810082923_AddTransactionInstrumentSnapshots";
    private const string DefaultConnectionString = "Server=example.invalid;Port=3306;Database=financeapp";

    [Fact]
    public void AppDbContext_MapsTransactionSnapshotFields_WithExpectedLengthsPrecisionAndStringConversion()
    {
        using var context = CreateMySqlContext();
        var entityType = context.Model.FindEntityType(typeof(Transaction));
        Assert.NotNull(entityType);

        var instrumentCode = entityType!.FindProperty(nameof(Transaction.InstrumentCode));
        Assert.NotNull(instrumentCode);
        Assert.Equal(32, instrumentCode!.GetMaxLength());
        Assert.Equal("varchar(32)", instrumentCode.GetColumnType());

        var instrumentCodeType = entityType.FindProperty(nameof(Transaction.InstrumentCodeType));
        Assert.NotNull(instrumentCodeType);
        Assert.Equal(8, instrumentCodeType!.GetMaxLength());
        Assert.Equal("varchar(8)", instrumentCodeType.GetColumnType());
        Assert.Equal(typeof(string), instrumentCodeType.GetTypeMapping().Converter?.ProviderClrType);

        var quantity = entityType.FindProperty(nameof(Transaction.Quantity));
        Assert.NotNull(quantity);
        Assert.Equal(18, quantity!.GetPrecision());
        Assert.Equal(8, quantity.GetScale());
        Assert.Equal("decimal(18,8)", quantity.GetColumnType());

        var unitPrice = entityType.FindProperty(nameof(Transaction.UnitPrice));
        Assert.NotNull(unitPrice);
        Assert.Equal(18, unitPrice!.GetPrecision());
        Assert.Equal(8, unitPrice.GetScale());
        Assert.Equal("decimal(18,8)", unitPrice.GetColumnType());
    }

    [Fact]
    public void MigrationScript_AddsSnapshotColumnsAndBackfillsSafely()
    {
        using var context = CreateMySqlContext();
        var migrator = context.GetService<IMigrator>();

        var script = migrator.GenerateScript(PreviousMigration, CurrentMigration);

        Assert.Contains("InstrumentCode", script);
        Assert.Contains("varchar(32)", script);
        Assert.Contains("InstrumentCodeType", script);
        Assert.Contains("varchar(8)", script);
        Assert.Contains("Quantity", script);
        Assert.Contains("UnitPrice", script);
        Assert.Contains("UPPER(TRIM(s.`Isin`))", script);
        Assert.Contains("THEN 'ISIN'", script);
        Assert.Contains("THEN 'Ticker'", script);
        Assert.Contains("t.`InstrumentCode` IS NULL", script);
        Assert.Contains("t.`InstrumentCodeType` IS NULL", script);
        Assert.Contains("COALESCE(t.`Quantity`, o.`Quantity`)", script);
        Assert.Contains("COALESCE(t.`UnitPrice`, o.`Price`)", script);
    }

    [Fact]
    public void MigrationScript_RollsBackByDroppingSnapshotColumns()
    {
        using var context = CreateMySqlContext();
        var migrator = context.GetService<IMigrator>();

        var script = migrator.GenerateScript(CurrentMigration, PreviousMigration);

        Assert.Contains("DROP COLUMN `InstrumentCode`", script);
        Assert.Contains("DROP COLUMN `InstrumentCodeType`", script);
        Assert.Contains("DROP COLUMN `Quantity`", script);
        Assert.Contains("DROP COLUMN `UnitPrice`", script);
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
