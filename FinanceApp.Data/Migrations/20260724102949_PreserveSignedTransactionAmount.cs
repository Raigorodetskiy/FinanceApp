using FinanceApp.Core.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreserveSignedTransactionAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const int depositType = (int)TransactionType.Deposit;
            const int withdrawalType = (int)TransactionType.Withdrawal;

            migrationBuilder.AddColumn<decimal>(
                name: "SignedAmount",
                table: "Transactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql($"""
                -- TransactionType.Deposit = {depositType}, TransactionType.Withdrawal = {withdrawalType}
                UPDATE `Transactions`
                SET `SignedAmount` = CASE
                        WHEN `Amount` < 0 THEN `Amount`
                        WHEN `Type` = {depositType} THEN ABS(`Amount`)
                        ELSE -ABS(`Amount`)
                    END,
                    `Amount` = ABS(`Amount`),
                    `Type` = CASE
                        WHEN `Amount` < 0 THEN {withdrawalType}
                        ELSE `Type`
                    END;
                """);

            migrationBuilder.Sql($"""
                -- TransactionType.Deposit = {depositType}, TransactionType.Withdrawal = {withdrawalType}
                UPDATE `Transactions`
                SET `Type` = {depositType},
                    `SignedAmount` = ABS(`Amount`)
                WHERE `Type` = {withdrawalType}
                  AND `Description` LIKE 'Kundennummer % - ______';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignedAmount",
                table: "Transactions");
        }
    }
}
