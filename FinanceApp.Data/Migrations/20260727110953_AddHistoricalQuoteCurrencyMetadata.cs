using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalQuoteCurrencyMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FinancialCurrency",
                table: "StockHistoricalPrices",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedQuoteCurrency",
                table: "StockHistoricalPrices",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "QuoteCurrency",
                table: "StockHistoricalPrices",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "QuoteUnitMultiplier",
                table: "StockHistoricalPrices",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 1m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinancialCurrency",
                table: "StockHistoricalPrices");

            migrationBuilder.DropColumn(
                name: "NormalizedQuoteCurrency",
                table: "StockHistoricalPrices");

            migrationBuilder.DropColumn(
                name: "QuoteCurrency",
                table: "StockHistoricalPrices");

            migrationBuilder.DropColumn(
                name: "QuoteUnitMultiplier",
                table: "StockHistoricalPrices");
        }
    }
}
