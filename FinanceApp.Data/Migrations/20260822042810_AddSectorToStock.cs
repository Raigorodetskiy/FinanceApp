using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSectorToStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SectorId",
                table: "Stocks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_SectorId",
                table: "Stocks",
                column: "SectorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Sectors_SectorId",
                table: "Stocks",
                column: "SectorId",
                principalTable: "Sectors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Sectors_SectorId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_SectorId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "SectorId",
                table: "Stocks");
        }
    }
}
