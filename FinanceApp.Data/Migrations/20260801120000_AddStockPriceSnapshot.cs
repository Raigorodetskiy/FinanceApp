using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockPriceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentPriceChange",
                table: "Stocks",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentPriceChangePercent",
                table: "Stocks",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentPriceAt",
                table: "Stocks",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentPriceChange",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "CurrentPriceChangePercent",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "CurrentPriceAt",
                table: "Stocks");
        }
    }
}
