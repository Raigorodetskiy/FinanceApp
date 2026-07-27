using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWknIsinToStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Isin",
                table: "Stocks",
                type: "varchar(12)",
                maxLength: 12,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Wkn",
                table: "Stocks",
                type: "varchar(6)",
                maxLength: 6,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_Isin",
                table: "Stocks",
                column: "Isin",
                unique: true,
                filter: "`Isin` IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_Wkn",
                table: "Stocks",
                column: "Wkn",
                unique: true,
                filter: "`Wkn` IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_Isin",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_Wkn",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "Isin",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "Wkn",
                table: "Stocks");
        }
    }
}
