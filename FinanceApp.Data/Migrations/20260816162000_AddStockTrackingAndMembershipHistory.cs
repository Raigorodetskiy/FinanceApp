using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockTrackingAndMembershipHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─── Stocks: TrackingStatus + ProviderSymbol ───────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "TrackingStatus",
                table: "Stocks",
                type: "int",
                nullable: false,
                // DB default is 1 (Tracked) so that raw legacy inserts without an explicit
                // status remain visible and behave like existing Tracked stocks.
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSymbol",
                table: "Stocks",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Backfill: all existing stocks are Tracked.
            migrationBuilder.Sql("UPDATE `Stocks` SET `TrackingStatus` = 1 WHERE `TrackingStatus` != 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_TrackingStatus",
                table: "Stocks",
                column: "TrackingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProviderSymbol",
                table: "Stocks",
                column: "ProviderSymbol",
                filter: "`ProviderSymbol` IS NOT NULL");

            // ─── StockMarketIndices: surrogate PK + membership history ────────────────
            // 1. Drop the existing composite PK constraint.
            migrationBuilder.DropPrimaryKey(
                name: "PK_StockMarketIndices",
                table: "StockMarketIndices");

            // 2. Add the surrogate identity column (INT AUTO_INCREMENT).
            //    In MySQL we add it without PK first, then add PK.
            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "StockMarketIndices",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            // 3. Add the new PK on the surrogate Id.
            migrationBuilder.AddPrimaryKey(
                name: "PK_StockMarketIndices",
                table: "StockMarketIndices",
                column: "Id");

            // 4. Add membership metadata columns.
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "StockMarketIndices",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProviderConstituentKey",
                table: "StockMarketIndices",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveFrom",
                table: "StockMarketIndices",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveTo",
                table: "StockMarketIndices",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastVerifiedAt",
                table: "StockMarketIndices",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImportedAt",
                table: "StockMarketIndices",
                type: "datetime(6)",
                nullable: false,
                defaultValueSql: "UTC_TIMESTAMP(6)");

            // 5. Add a non-unique index for fast (StockId, MarketIndexId) lookups.
            migrationBuilder.CreateIndex(
                name: "IX_StockMarketIndices_StockId_MarketIndexId",
                table: "StockMarketIndices",
                columns: new[] { "StockId", "MarketIndexId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: Down cannot perfectly restore the composite PK because the history rows
            // (EffectiveTo != NULL) created by the new schema have no equivalent in the old schema.
            // Only the latest current membership per (StockId, MarketIndexId) is kept.

            // Drop added indexes and columns.
            migrationBuilder.DropIndex(name: "IX_Stocks_TrackingStatus", table: "Stocks");
            migrationBuilder.DropIndex(name: "IX_Stocks_ProviderSymbol", table: "Stocks");
            migrationBuilder.DropColumn(name: "TrackingStatus", table: "Stocks");
            migrationBuilder.DropColumn(name: "ProviderSymbol", table: "Stocks");

            migrationBuilder.DropIndex(name: "IX_StockMarketIndices_StockId_MarketIndexId", table: "StockMarketIndices");
            migrationBuilder.DropColumn(name: "Source", table: "StockMarketIndices");
            migrationBuilder.DropColumn(name: "ProviderConstituentKey", table: "StockMarketIndices");
            migrationBuilder.DropColumn(name: "EffectiveFrom", table: "StockMarketIndices");
            migrationBuilder.DropColumn(name: "EffectiveTo", table: "StockMarketIndices");
            migrationBuilder.DropColumn(name: "LastVerifiedAt", table: "StockMarketIndices");
            migrationBuilder.DropColumn(name: "ImportedAt", table: "StockMarketIndices");

            // Restore composite PK (requires deduplication).
            migrationBuilder.Sql(
                "DELETE smi1 FROM `StockMarketIndices` smi1 " +
                "INNER JOIN `StockMarketIndices` smi2 ON smi1.StockId = smi2.StockId AND smi1.MarketIndexId = smi2.MarketIndexId " +
                "WHERE smi1.Id > smi2.Id;");

            migrationBuilder.DropPrimaryKey(name: "PK_StockMarketIndices", table: "StockMarketIndices");
            migrationBuilder.DropColumn(name: "Id", table: "StockMarketIndices");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockMarketIndices",
                table: "StockMarketIndices",
                columns: new[] { "StockId", "MarketIndexId" });
        }
    }
}
