using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketIndexHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderSymbol",
                table: "MarketIndices",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MarketIndexHistoricalPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MarketIndexId = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Interval = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Open = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: true),
                    Provider = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FetchedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ProviderSymbol = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketIndexHistoricalPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketIndexHistoricalPrices_MarketIndices_MarketIndexId",
                        column: x => x.MarketIndexId,
                        principalTable: "MarketIndices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 1, column: "ProviderSymbol", value: "^DJI");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 2, column: "ProviderSymbol", value: "^GSPC");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 3, column: "ProviderSymbol", value: "^IXIC");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 4, column: "ProviderSymbol", value: "^NDX");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 5, column: "ProviderSymbol", value: "^RUT");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 6, column: "ProviderSymbol", value: "^FTSE");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 7, column: "ProviderSymbol", value: "^GDAXI");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 8, column: "ProviderSymbol", value: "^FCHI");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 9, column: "ProviderSymbol", value: "^STOXX50E");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 10, column: "ProviderSymbol", value: "^STOXX");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 11, column: "ProviderSymbol", value: "^SSMI");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 12, column: "ProviderSymbol", value: "^IBEX");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 13, column: "ProviderSymbol", value: "FTSEMIB.MI");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 14, column: "ProviderSymbol", value: "^N225");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 15, column: "ProviderSymbol", value: "^TOPX");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 16, column: "ProviderSymbol", value: "^HSI");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 17, column: "ProviderSymbol", value: "000300.SS");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 18, column: "ProviderSymbol", value: "000001.SS");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 19, column: "ProviderSymbol", value: "^AXJO");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 20, column: "ProviderSymbol", value: "^GSPTSE");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 21, column: "ProviderSymbol", value: "^BSESN");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 22, column: "ProviderSymbol", value: "^NSEI");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 23, column: "ProviderSymbol", value: "^KS11");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 24, column: "ProviderSymbol", value: "^BVSP");
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 25, column: "ProviderSymbol", value: null);
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 26, column: "ProviderSymbol", value: null);
            migrationBuilder.UpdateData(table: "MarketIndices", keyColumn: "Id", keyValue: 27, column: "ProviderSymbol", value: null);

            migrationBuilder.CreateIndex(
                name: "IX_MarketIndexHistoricalPrices_MarketIndexId_Interval_Timestamp",
                table: "MarketIndexHistoricalPrices",
                columns: new[] { "MarketIndexId", "Interval", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketIndexHistoricalPrices_MarketIndexId_Timestamp_Interval",
                table: "MarketIndexHistoricalPrices",
                columns: new[] { "MarketIndexId", "Timestamp", "Interval" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MarketIndexHistoricalPrices");

            migrationBuilder.DropColumn(name: "ProviderSymbol", table: "MarketIndices");
        }
    }
}
