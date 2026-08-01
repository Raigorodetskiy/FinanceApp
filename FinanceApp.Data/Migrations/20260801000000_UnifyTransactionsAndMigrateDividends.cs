using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <summary>
    /// Extends Transaction with optional StockId and OrderId columns.
    /// - StockId enables Buy / Sell / Dividend transactions to reference a stock.
    /// - OrderId carries a unique constraint (filtered for IS NOT NULL) to prevent duplicate
    ///   transactions when an executed order is saved multiple times.
    ///
    /// Existing dividend rows from the Dividends table are copied into Transactions as
    /// TransactionType.Dividend (value = 4). The migration is idempotent: dividends that
    /// already have a corresponding Transaction row (identified by the same PortfolioId,
    /// StockId, Amount and CreatedAt rounded to the second) are skipped.
    ///
    /// The legacy Dividends table is intentionally preserved so that the old
    /// /finance/dividends endpoints keep working for compatibility. The UI no longer
    /// uses a separate dividend workflow.
    /// </summary>
    public partial class UnifyTransactionsAndMigrateDividends : Migration
    {
        // Enum values for TransactionType
        private const int DividendType = 4;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add optional StockId column
            migrationBuilder.AddColumn<int>(
                name: "StockId",
                table: "Transactions",
                type: "int",
                nullable: true);

            // 2. Add optional OrderId column
            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                table: "Transactions",
                type: "int",
                nullable: true);

            // 3. Unique index on OrderId (filtered — only non-NULL rows must be unique)
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_OrderId",
                table: "Transactions",
                column: "OrderId",
                unique: true,
                filter: "`OrderId` IS NOT NULL");

            // 4. FK: Transactions.StockId -> Stocks.Id (SET NULL on delete so that deleting
            //    a stock does not cascade-delete financial history)
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_StockId",
                table: "Transactions",
                column: "StockId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Stocks_StockId",
                table: "Transactions",
                column: "StockId",
                principalTable: "Stocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // 5. Migrate existing Dividend rows to Transactions.
            //    Idempotency guard: skip rows where a matching transaction already exists.
            migrationBuilder.Sql($@"
INSERT INTO `Transactions` (`PortfolioId`, `StockId`, `Type`, `Amount`, `SignedAmount`, `Description`, `CreatedAt`)
SELECT
    d.`PortfolioId`,
    d.`StockId`,
    {DividendType},
    d.`Amount`,
    d.`Amount`,
    CONCAT('Дивиденды — ', s.`Ticker`, ' · ', s.`Name`),
    d.`PaidAt`
FROM `Dividends` d
INNER JOIN `Stocks` s ON s.`Id` = d.`StockId`
WHERE NOT EXISTS (
    SELECT 1
    FROM `Transactions` t
    WHERE t.`PortfolioId` = d.`PortfolioId`
      AND t.`StockId`     = d.`StockId`
      AND t.`Type`        = {DividendType}
      AND t.`Amount`      = d.`Amount`
      AND DATE_FORMAT(t.`CreatedAt`, '%Y-%m-%d %H:%i:%s') = DATE_FORMAT(d.`PaidAt`, '%Y-%m-%d %H:%i:%s')
);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove migrated dividend transactions (those with Type = Dividend and no OrderId)
            migrationBuilder.Sql($@"
DELETE FROM `Transactions`
WHERE `Type` = {DividendType} AND `OrderId` IS NULL;
");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Stocks_StockId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_StockId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_OrderId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "StockId",
                table: "Transactions");
        }
    }
}
