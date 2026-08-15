using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSectorsAndIndustries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sectors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sectors", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Industries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SectorId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Industries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Industries_Sectors_SectorId",
                        column: x => x.SectorId,
                        principalTable: "Sectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "IndustryId",
                table: "Stocks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Industries_SectorId_NormalizedName",
                table: "Industries",
                columns: new[] { "SectorId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sectors_NormalizedName",
                table: "Sectors",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_IndustryId",
                table: "Stocks",
                column: "IndustryId");

            migrationBuilder.InsertData(
                table: "Sectors",
                columns: new[] { "Id", "Name", "NormalizedName", "IsArchived", "SortOrder", "CreatedAtUtc", "UpdatedAtUtc" },
                values: new object[,]
                {
                { 1, "Энергетика", "ЭНЕРГЕТИКА", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 2, "Материалы", "МАТЕРИАЛЫ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 3, "Промышленность", "ПРОМЫШЛЕННОСТЬ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 4, "Потребительские товары и услуги", "ПОТРЕБИТЕЛЬСКИЕ ТОВАРЫ И УСЛУГИ", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 5, "Товары повседневного спроса", "ТОВАРЫ ПОВСЕДНЕВНОГО СПРОСА", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 6, "Здравоохранение", "ЗДРАВООХРАНЕНИЕ", false, 60, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 7, "Финансы", "ФИНАНСЫ", false, 70, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 8, "Информационные технологии", "ИНФОРМАЦИОННЫЕ ТЕХНОЛОГИИ", false, 80, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 9, "Коммуникационные услуги", "КОММУНИКАЦИОННЫЕ УСЛУГИ", false, 90, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 10, "Коммунальные услуги", "КОММУНАЛЬНЫЕ УСЛУГИ", false, 100, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 11, "Недвижимость", "НЕДВИЖИМОСТЬ", false, 110, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 12, "Другое", "ДРУГОЕ", false, 120, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "Industries",
                columns: new[] { "Id", "SectorId", "Name", "NormalizedName", "IsArchived", "SortOrder", "CreatedAtUtc", "UpdatedAtUtc" },
                values: new object[,]
                {
                { 1, 1, "Нефть и газ", "НЕФТЬ И ГАЗ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 2, 1, "Оборудование и услуги для энергетики", "ОБОРУДОВАНИЕ И УСЛУГИ ДЛЯ ЭНЕРГЕТИКИ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 3, 1, "Уголь и прочие виды топлива", "УГОЛЬ И ПРОЧИЕ ВИДЫ ТОПЛИВА", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 4, 1, "Возобновляемая энергетика", "ВОЗОБНОВЛЯЕМАЯ ЭНЕРГЕТИКА", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 5, 2, "Химическая промышленность", "ХИМИЧЕСКАЯ ПРОМЫШЛЕННОСТЬ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 6, 2, "Металлургия и добыча", "МЕТАЛЛУРГИЯ И ДОБЫЧА", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 7, 2, "Строительные материалы", "СТРОИТЕЛЬНЫЕ МАТЕРИАЛЫ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 8, 2, "Упаковка", "УПАКОВКА", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 9, 2, "Бумага и лесная промышленность", "БУМАГА И ЛЕСНАЯ ПРОМЫШЛЕННОСТЬ", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 10, 3, "Аэрокосмическая и оборонная промышленность", "АЭРОКОСМИЧЕСКАЯ И ОБОРОННАЯ ПРОМЫШЛЕННОСТЬ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 11, 3, "Машиностроение", "МАШИНОСТРОЕНИЕ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 12, 3, "Электрооборудование", "ЭЛЕКТРООБОРУДОВАНИЕ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 13, 3, "Строительство и инжиниринг", "СТРОИТЕЛЬСТВО И ИНЖИНИРИНГ", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 14, 3, "Транспорт и логистика", "ТРАНСПОРТ И ЛОГИСТИКА", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 15, 3, "Коммерческие и профессиональные услуги", "КОММЕРЧЕСКИЕ И ПРОФЕССИОНАЛЬНЫЕ УСЛУГИ", false, 60, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 16, 4, "Автомобили и комплектующие", "АВТОМОБИЛИ И КОМПЛЕКТУЮЩИЕ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 17, 4, "Товары длительного пользования", "ТОВАРЫ ДЛИТЕЛЬНОГО ПОЛЬЗОВАНИЯ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 18, 4, "Одежда и предметы роскоши", "ОДЕЖДА И ПРЕДМЕТЫ РОСКОШИ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 19, 4, "Гостиницы, рестораны и отдых", "ГОСТИНИЦЫ, РЕСТОРАНЫ И ОТДЫХ", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 20, 4, "Розничная торговля", "РОЗНИЧНАЯ ТОРГОВЛЯ", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 21, 4, "Интернет-торговля", "ИНТЕРНЕТ-ТОРГОВЛЯ", false, 60, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 22, 5, "Продукты питания", "ПРОДУКТЫ ПИТАНИЯ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 23, 5, "Напитки", "НАПИТКИ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 24, 5, "Табачная продукция", "ТАБАЧНАЯ ПРОДУКЦИЯ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 25, 5, "Бытовая и личная гигиена", "БЫТОВАЯ И ЛИЧНАЯ ГИГИЕНА", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 26, 5, "Продовольственная розница", "ПРОДОВОЛЬСТВЕННАЯ РОЗНИЦА", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 27, 6, "Фармацевтика", "ФАРМАЦЕВТИКА", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 28, 6, "Биотехнологии", "БИОТЕХНОЛОГИИ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 29, 6, "Медицинское оборудование", "МЕДИЦИНСКОЕ ОБОРУДОВАНИЕ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 30, 6, "Медицинские услуги", "МЕДИЦИНСКИЕ УСЛУГИ", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 31, 6, "Медицинское страхование", "МЕДИЦИНСКОЕ СТРАХОВАНИЕ", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 32, 7, "Банки", "БАНКИ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 33, 7, "Страхование", "СТРАХОВАНИЕ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 34, 7, "Управление активами", "УПРАВЛЕНИЕ АКТИВАМИ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 35, 7, "Инвестиционные услуги", "ИНВЕСТИЦИОННЫЕ УСЛУГИ", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 36, 7, "Платёжные системы", "ПЛАТЁЖНЫЕ СИСТЕМЫ", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 37, 7, "Финансовые технологии", "ФИНАНСОВЫЕ ТЕХНОЛОГИИ", false, 60, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 38, 8, "Программное обеспечение", "ПРОГРАММНОЕ ОБЕСПЕЧЕНИЕ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 39, 8, "IT-услуги", "IT-УСЛУГИ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 40, 8, "Полупроводники", "ПОЛУПРОВОДНИКИ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 41, 8, "Компьютерное оборудование", "КОМПЬЮТЕРНОЕ ОБОРУДОВАНИЕ", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 42, 8, "Электронные компоненты", "ЭЛЕКТРОННЫЕ КОМПОНЕНТЫ", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 43, 9, "Телекоммуникации", "ТЕЛЕКОММУНИКАЦИИ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 44, 9, "Медиа", "МЕДИА", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 45, 9, "Развлечения", "РАЗВЛЕЧЕНИЯ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 46, 9, "Интерактивные сервисы и платформы", "ИНТЕРАКТИВНЫЕ СЕРВИСЫ И ПЛАТФОРМЫ", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 47, 10, "Электроэнергетика", "ЭЛЕКТРОЭНЕРГЕТИКА", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 48, 10, "Газоснабжение", "ГАЗОСНАБЖЕНИЕ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 49, 10, "Водоснабжение", "ВОДОСНАБЖЕНИЕ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 50, 10, "Независимые производители энергии", "НЕЗАВИСИМЫЕ ПРОИЗВОДИТЕЛИ ЭНЕРГИИ", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 51, 11, "Жилая недвижимость", "ЖИЛАЯ НЕДВИЖИМОСТЬ", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 52, 11, "Коммерческая недвижимость", "КОММЕРЧЕСКАЯ НЕДВИЖИМОСТЬ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 53, 11, "Промышленная недвижимость", "ПРОМЫШЛЕННАЯ НЕДВИЖИМОСТЬ", false, 30, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 54, 11, "REIT", "REIT", false, 40, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 55, 11, "Управление и развитие недвижимости", "УПРАВЛЕНИЕ И РАЗВИТИЕ НЕДВИЖИМОСТИ", false, 50, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 56, 12, "Диверсифицированный бизнес", "ДИВЕРСИФИЦИРОВАННЫЙ БИЗНЕС", false, 10, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
                { 57, 12, "Неклассифицированные компании", "НЕКЛАССИФИЦИРОВАННЫЕ КОМПАНИИ", false, 20, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Industries_IndustryId",
                table: "Stocks",
                column: "IndustryId",
                principalTable: "Industries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Industries_IndustryId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_IndustryId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "IndustryId",
                table: "Stocks");

            migrationBuilder.DropTable(
                name: "Industries");

            migrationBuilder.DropTable(
                name: "Sectors");
        }
    }
}
