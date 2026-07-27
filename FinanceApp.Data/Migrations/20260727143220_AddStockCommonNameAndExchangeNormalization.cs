using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCommonNameAndExchangeNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `Stocks`
                SET `Exchange` = CASE
                    WHEN UPPER(TRIM(COALESCE(`Exchange`, ''))) = 'NYSE' THEN 'NYSE'
                    WHEN UPPER(TRIM(COALESCE(`Exchange`, ''))) = 'FRANKFURT' THEN 'Frankfurt'
                    WHEN RIGHT(UPPER(TRIM(COALESCE(`Ticker`, ''))), 2) = '.F' THEN 'Frankfurt'
                    ELSE 'NYSE'
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Exchange",
                table: "Stocks",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NYSE",
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CommonName",
                table: "Stocks",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("""
                UPDATE `Stocks`
                SET `CommonName` = COALESCE(NULLIF(TRIM(`Name`), ''), `Name`)
                WHERE TRIM(`CommonName`) = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommonName",
                table: "Stocks");

            migrationBuilder.AlterColumn<string>(
                name: "Exchange",
                table: "Stocks",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32,
                oldDefaultValue: "NYSE")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
