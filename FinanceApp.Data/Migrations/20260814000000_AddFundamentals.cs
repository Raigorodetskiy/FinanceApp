using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundamentals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundamentalsSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StockId = table.Column<int>(type: "int", nullable: false),
                    SourceSymbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MarketCap = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    EnterpriseValue = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    TotalDebt = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    CashAndEquivalents = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    RevenueTtm = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    NetIncomeTtm = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    EbitdaTtm = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    OperatingIncomeTtm = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    FreeCashFlowTtm = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    TotalAssets = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    TotalLiabilities = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    PeRatio = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ForwardPeRatio = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PbRatio = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DividendYield = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    Currency = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, defaultValue: "Yahoo Finance")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AsOfDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundamentalsSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FundamentalsSnapshots_Stocks_StockId",
                        column: x => x.StockId,
                        principalTable: "Stocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EarningsEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SnapshotId = table.Column<int>(type: "int", nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReportDateEnd = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DateStatus = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: "Unknown")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EpsEstimate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    EpsReported = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    RevenueEstimate = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    RevenueReported = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    FiscalPeriod = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, defaultValue: "Yahoo Finance")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FetchedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarningsEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EarningsEvents_FundamentalsSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "FundamentalsSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FinancialPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SnapshotId = table.Column<int>(type: "int", nullable: false),
                    PeriodType = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FiscalYear = table.Column<int>(type: "int", nullable: true),
                    FiscalQuarter = table.Column<int>(type: "int", nullable: true),
                    PeriodEndDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReportedCurrency = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Revenue = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    OperatingIncome = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    NetIncome = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    EpsReported = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    EpsEstimate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Ebitda = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    TotalDebt = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    TotalAssets = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    TotalLiabilities = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    FreeCashFlow = table.Column<decimal>(type: "decimal(28,2)", nullable: true),
                    Source = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, defaultValue: "Yahoo Finance")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AsOfDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialPeriods_FundamentalsSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "FundamentalsSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EarningsEvents_SnapshotId_ReportDate_FiscalPeriod",
                table: "EarningsEvents",
                columns: new[] { "SnapshotId", "ReportDate", "FiscalPeriod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialPeriods_SnapshotId_PeriodType_PeriodEndDate",
                table: "FinancialPeriods",
                columns: new[] { "SnapshotId", "PeriodType", "PeriodEndDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundamentalsSnapshots_StockId",
                table: "FundamentalsSnapshots",
                column: "StockId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EarningsEvents");

            migrationBuilder.DropTable(
                name: "FinancialPeriods");

            migrationBuilder.DropTable(
                name: "FundamentalsSnapshots");
        }
    }
}
