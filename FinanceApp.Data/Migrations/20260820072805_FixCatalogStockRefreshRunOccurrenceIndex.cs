using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixCatalogStockRefreshRunOccurrenceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS `IX_CatalogStockRefreshRuns_BusinessDate_TimeZoneId` ON `CatalogStockRefreshRuns`;");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogStockRefreshRuns_BusinessDate_TimeZoneId",
                table: "CatalogStockRefreshRuns",
                columns: new[] { "BusinessDate", "TimeZoneId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS `IX_CatalogStockRefreshRuns_BusinessDate_TimeZoneId` ON `CatalogStockRefreshRuns`;");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogStockRefreshRuns_BusinessDate_TimeZoneId",
                table: "CatalogStockRefreshRuns",
                columns: new[] { "BusinessDate", "TimeZoneId" },
                unique: true);
        }
    }
}
