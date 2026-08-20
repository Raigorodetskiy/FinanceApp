using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdjustedCloseAndNonUniqueIsin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_Isin",
                table: "Stocks");

            migrationBuilder.AddColumn<decimal>(
                name: "AdjustedClose",
                table: "StockHistoricalPrices",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_Isin",
                table: "Stocks",
                column: "Isin",
                filter: "`Isin` IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_Isin",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "AdjustedClose",
                table: "StockHistoricalPrices");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_Isin",
                table: "Stocks",
                column: "Isin",
                unique: true,
                filter: "`Isin` IS NOT NULL");
        }
    }
}
