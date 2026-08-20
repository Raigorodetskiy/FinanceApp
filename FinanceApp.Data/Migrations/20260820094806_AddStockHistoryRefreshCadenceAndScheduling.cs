using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockHistoryRefreshCadenceAndScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HistoryRefreshCadence",
                table: "Stocks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFullHistoryBackfillSucceededAtUtc",
                table: "Stocks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHistoryReconciliationSucceededAtUtc",
                table: "Stocks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIncrementalHistoryRefreshSucceededAtUtc",
                table: "Stocks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextFullHistoryBackfillAtUtc",
                table: "Stocks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextHistoryReconciliationAtUtc",
                table: "Stocks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextIncrementalHistoryRefreshAtUtc",
                table: "Stocks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.Sql(
                """
                SET @financeapp_history_seed_now_utc := UTC_TIMESTAMP(6);

                UPDATE `Stocks` s
                LEFT JOIN (
                    SELECT DISTINCT `StockId`
                    FROM `StockHistoricalPrices`
                    WHERE `Interval` = '1d'
                ) h ON h.`StockId` = s.`Id`
                SET
                    s.`HistoryRefreshCadence` = CASE
                        WHEN s.`TrackingStatus` = 1 THEN 1
                        WHEN s.`TrackingStatus` = 0 THEN 2
                        ELSE 0
                    END,
                    s.`NextIncrementalHistoryRefreshAtUtc` = CASE
                        WHEN (CASE
                            WHEN s.`TrackingStatus` = 1 THEN 1
                            WHEN s.`TrackingStatus` = 0 THEN 2
                            ELSE 0
                        END) = 0 THEN NULL
                        WHEN (CASE
                            WHEN s.`TrackingStatus` = 1 THEN 1
                            WHEN s.`TrackingStatus` = 0 THEN 2
                            ELSE 0
                        END) = 1
                            THEN DATE_ADD(@financeapp_history_seed_now_utc, INTERVAL MOD(s.`Id`, 24) HOUR)
                        ELSE DATE_ADD(
                            DATE_ADD(@financeapp_history_seed_now_utc, INTERVAL MOD(s.`Id`, 7) DAY),
                            INTERVAL MOD(s.`Id`, 24) HOUR)
                    END,
                    s.`NextHistoryReconciliationAtUtc` = CASE
                        WHEN (CASE
                            WHEN s.`TrackingStatus` = 1 THEN 1
                            WHEN s.`TrackingStatus` = 0 THEN 2
                            ELSE 0
                        END) = 0 THEN NULL
                        WHEN (CASE
                            WHEN s.`TrackingStatus` = 1 THEN 1
                            WHEN s.`TrackingStatus` = 0 THEN 2
                            ELSE 0
                        END) = 1
                            THEN DATE_ADD(
                                DATE_ADD(@financeapp_history_seed_now_utc, INTERVAL MOD(s.`Id`, 7) DAY),
                                INTERVAL MOD(s.`Id`, 24) HOUR)
                        ELSE DATE_ADD(
                            DATE_ADD(@financeapp_history_seed_now_utc, INTERVAL MOD(s.`Id`, 30) DAY),
                            INTERVAL MOD(s.`Id`, 24) HOUR)
                    END,
                    s.`NextFullHistoryBackfillAtUtc` = CASE
                        WHEN (CASE
                            WHEN s.`TrackingStatus` = 1 THEN 1
                            WHEN s.`TrackingStatus` = 0 THEN 2
                            ELSE 0
                        END) = 0 THEN NULL
                        WHEN h.`StockId` IS NULL
                            THEN DATE_SUB(@financeapp_history_seed_now_utc, INTERVAL 1 MINUTE)
                        ELSE DATE_ADD(
                            DATE_ADD(@financeapp_history_seed_now_utc, INTERVAL MOD(s.`Id`, 30) DAY),
                            INTERVAL MOD(s.`Id`, 24) HOUR)
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_HistoryRefreshCadence_NextFullBackfill_Id",
                table: "Stocks",
                columns: new[] { "HistoryRefreshCadence", "NextFullHistoryBackfillAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_HistoryRefreshCadence_NextIncremental_Id",
                table: "Stocks",
                columns: new[] { "HistoryRefreshCadence", "NextIncrementalHistoryRefreshAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_HistoryRefreshCadence_NextReconciliation_Id",
                table: "Stocks",
                columns: new[] { "HistoryRefreshCadence", "NextHistoryReconciliationAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_HistoryRefreshCadence_NextFullBackfill_Id",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_HistoryRefreshCadence_NextIncremental_Id",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_HistoryRefreshCadence_NextReconciliation_Id",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "HistoryRefreshCadence",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "LastFullHistoryBackfillSucceededAtUtc",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "LastHistoryReconciliationSucceededAtUtc",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "LastIncrementalHistoryRefreshSucceededAtUtc",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "NextFullHistoryBackfillAtUtc",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "NextHistoryReconciliationAtUtc",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "NextIncrementalHistoryRefreshAtUtc",
                table: "Stocks");
        }
    }
}
