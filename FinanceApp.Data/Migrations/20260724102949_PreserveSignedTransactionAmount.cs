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
            migrationBuilder.AddColumn<decimal>(
                name: "SignedAmount",
                table: "Transactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE `Transactions`
                SET `SignedAmount` = CASE
                        WHEN `Amount` < 0 THEN `Amount`
                        WHEN `Type` = 0 THEN ABS(`Amount`)
                        ELSE -ABS(`Amount`)
                    END,
                    `Amount` = ABS(`Amount`),
                    `Type` = CASE
                        WHEN `Amount` < 0 THEN 1
                        ELSE `Type`
                    END;
                """);

            migrationBuilder.Sql("""
                UPDATE `Transactions`
                SET `Type` = 0,
                    `SignedAmount` = ABS(`Amount`)
                WHERE `Type` = 1
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
