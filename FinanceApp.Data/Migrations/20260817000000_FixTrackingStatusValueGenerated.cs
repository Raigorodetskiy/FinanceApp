using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixTrackingStatusValueGenerated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove the DB-level DEFAULT (1 = Tracked) from TrackingStatus.
            //
            // Root cause: HasDefaultValue(Tracked) in the previous EF configuration marks the
            // property as ValueGeneratedOnAdd, causing EF Core to exclude the column from INSERT
            // statements when the CLR value is CatalogOnly = 0 (the int CLR default/sentinel).
            // MySQL then substitutes the column DEFAULT (1), silently overwriting CatalogOnly with
            // Tracked. This is now fixed in AppDbContext by switching to ValueGeneratedNever().
            //
            // Removing the DEFAULT here ensures raw SQL inserts also behave correctly and that
            // the schema is consistent with the application model.
            migrationBuilder.AlterColumn<int>(
                name: "TrackingStatus",
                table: "Stocks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TrackingStatus",
                table: "Stocks",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
