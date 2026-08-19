using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogStockRefreshRunState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogStockRefreshRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RunKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScheduledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastProcessedStockId = table.Column<int>(type: "int", nullable: true),
                    PendingStockId = table.Column<int>(type: "int", nullable: true),
                    PendingQuoteCompleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PendingHistoryCompleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TotalDiscovered = table.Column<int>(type: "int", nullable: false),
                    Processed = table.Column<int>(type: "int", nullable: false),
                    QuoteSucceeded = table.Column<int>(type: "int", nullable: false),
                    QuoteFailed = table.Column<int>(type: "int", nullable: false),
                    QuoteSkipped = table.Column<int>(type: "int", nullable: false),
                    HistorySucceeded = table.Column<int>(type: "int", nullable: false),
                    HistoryFailed = table.Column<int>(type: "int", nullable: false),
                    HistorySkipped = table.Column<int>(type: "int", nullable: false),
                    RateLimited = table.Column<int>(type: "int", nullable: false),
                    Remaining = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailureSummary = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogStockRefreshRuns", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogStockRefreshRuns_BusinessDate_TimeZoneId",
                table: "CatalogStockRefreshRuns",
                columns: new[] { "BusinessDate", "TimeZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogStockRefreshRuns_RunKey",
                table: "CatalogStockRefreshRuns",
                column: "RunKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogStockRefreshRuns_Status_LeaseExpiresAtUtc",
                table: "CatalogStockRefreshRuns",
                columns: new[] { "Status", "LeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogStockRefreshRuns");
        }
    }
}
