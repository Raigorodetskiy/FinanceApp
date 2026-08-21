using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMetadataEnrichmentJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_Wkn",
                table: "Stocks");

            migrationBuilder.CreateTable(
                name: "StockMetadataEnrichmentJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    SelectedStockIdsJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDryRun = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TotalStocks = table.Column<int>(type: "int", nullable: false),
                    ProcessedStocks = table.Column<int>(type: "int", nullable: false),
                    SucceededStocks = table.Column<int>(type: "int", nullable: false),
                    PartialStocks = table.Column<int>(type: "int", nullable: false),
                    ReviewStocks = table.Column<int>(type: "int", nullable: false),
                    ConflictStocks = table.Column<int>(type: "int", nullable: false),
                    NotFoundStocks = table.Column<int>(type: "int", nullable: false),
                    RateLimitedStocks = table.Column<int>(type: "int", nullable: false),
                    FailedStocks = table.Column<int>(type: "int", nullable: false),
                    LastProcessedStockId = table.Column<int>(type: "int", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    RetryAfterUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    InitiatedByUserId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiagnosticSummary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetadataStaleAfterUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMetadataEnrichmentJobs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockMetadataIndustryMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Provider = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedSector = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedIndustry = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IndustryId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMetadataIndustryMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMetadataIndustryMappings_Industries_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockMetadataEnrichmentResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    JobId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StockId = table.Column<int>(type: "int", nullable: false),
                    ProviderSymbol = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Exchange = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OldIsin = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CandidateIsin = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OldWkn = table.Column<string>(type: "varchar(6)", maxLength: 6, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CandidateWkn = table.Column<string>(type: "varchar(6)", maxLength: 6, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OldIndustryId = table.Column<int>(type: "int", nullable: true),
                    CandidateIndustryId = table.Column<int>(type: "int", nullable: true),
                    RawProviderSector = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RawProviderIndustry = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsinSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WknSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IndustrySource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsinConfidence = table.Column<int>(type: "int", nullable: false),
                    WknConfidence = table.Column<int>(type: "int", nullable: false),
                    IndustryConfidence = table.Column<int>(type: "int", nullable: false),
                    IsinDecision = table.Column<int>(type: "int", nullable: false),
                    WknDecision = table.Column<int>(type: "int", nullable: false),
                    IndustryDecision = table.Column<int>(type: "int", nullable: false),
                    Diagnostics = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ManuallyApproved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Rejected = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMetadataEnrichmentResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMetadataEnrichmentResults_StockMetadataEnrichmentJobs_J~",
                        column: x => x.JobId,
                        principalTable: "StockMetadataEnrichmentJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_Wkn",
                table: "Stocks",
                column: "Wkn",
                filter: "`Wkn` IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockMetadataEnrichmentJobs_RetryAfterUtc",
                table: "StockMetadataEnrichmentJobs",
                column: "RetryAfterUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockMetadataEnrichmentJobs_Status_CreatedAtUtc",
                table: "StockMetadataEnrichmentJobs",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMetadataEnrichmentResults_JobId_Id",
                table: "StockMetadataEnrichmentResults",
                columns: new[] { "JobId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMetadataEnrichmentResults_JobId_StockId",
                table: "StockMetadataEnrichmentResults",
                columns: new[] { "JobId", "StockId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMetadataIndustryMappings_IndustryId",
                table: "StockMetadataIndustryMappings",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMetadataIndustryMappings_Provider_NormalizedSector_Norm~",
                table: "StockMetadataIndustryMappings",
                columns: new[] { "Provider", "NormalizedSector", "NormalizedIndustry" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMetadataEnrichmentResults");

            migrationBuilder.DropTable(
                name: "StockMetadataIndustryMappings");

            migrationBuilder.DropTable(
                name: "StockMetadataEnrichmentJobs");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_Wkn",
                table: "Stocks");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_Wkn",
                table: "Stocks",
                column: "Wkn",
                unique: true,
                filter: "`Wkn` IS NOT NULL");
        }
    }
}
