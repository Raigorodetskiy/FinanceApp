using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionInstrumentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstrumentCode",
                table: "Transactions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "InstrumentCodeType",
                table: "Transactions",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "Transactions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "Transactions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE `Transactions` t
                INNER JOIN `Stocks` s ON s.`Id` = t.`StockId`
                SET
                    t.`InstrumentCode` = CASE
                        WHEN NULLIF(TRIM(s.`Isin`), '') IS NOT NULL THEN UPPER(TRIM(s.`Isin`))
                        WHEN NULLIF(TRIM(s.`Ticker`), '') IS NOT NULL THEN TRIM(s.`Ticker`)
                        ELSE t.`InstrumentCode`
                    END,
                    t.`InstrumentCodeType` = CASE
                        WHEN NULLIF(TRIM(s.`Isin`), '') IS NOT NULL THEN 'ISIN'
                        WHEN NULLIF(TRIM(s.`Ticker`), '') IS NOT NULL THEN 'Ticker'
                        ELSE t.`InstrumentCodeType`
                    END
                WHERE t.`InstrumentCode` IS NULL
                  AND t.`InstrumentCodeType` IS NULL
                  AND (
                      NULLIF(TRIM(s.`Isin`), '') IS NOT NULL
                      OR NULLIF(TRIM(s.`Ticker`), '') IS NOT NULL
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE `Transactions` t
                INNER JOIN `Orders` o ON o.`Id` = t.`OrderId`
                SET
                    t.`Quantity` = COALESCE(t.`Quantity`, o.`Quantity`),
                    t.`UnitPrice` = COALESCE(t.`UnitPrice`, o.`Price`)
                WHERE t.`OrderId` IS NOT NULL
                  AND (t.`Quantity` IS NULL OR t.`UnitPrice` IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstrumentCode",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "InstrumentCodeType",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "Transactions");
        }
    }
}
