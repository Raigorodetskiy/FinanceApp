using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinanceApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketIndices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketIndices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryOrRegion = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketIndices", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockMarketIndices",
                columns: table => new
                {
                    StockId = table.Column<int>(type: "int", nullable: false),
                    MarketIndexId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMarketIndices", x => new { x.StockId, x.MarketIndexId });
                    table.ForeignKey(
                        name: "FK_StockMarketIndices_MarketIndices_MarketIndexId",
                        column: x => x.MarketIndexId,
                        principalTable: "MarketIndices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMarketIndices_Stocks_StockId",
                        column: x => x.StockId,
                        principalTable: "Stocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "MarketIndices",
                columns: new[] { "Id", "Code", "CountryOrRegion", "CreatedAt", "Description", "IsArchived", "Name", "NormalizedCode", "NormalizedName", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "DJIA", "USA", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Индекс Доу-Джонса для промышленных компаний отражает динамику 30 крупнейших публичных компаний США. Взвешен по цене, а не по рыночной капитализации, что является методологическим ограничением: более дорогие акции оказывают непропорционально большое влияние. Используется как исторический барометр состояния американской экономики, однако из-за малого числа компонентов и ценового взвешивания считается менее репрезентативным, чем S&P 500.", false, "Dow Jones Industrial Average", "DJIA", "DOW JONES INDUSTRIAL AVERAGE", 10, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, "SPX", "USA", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по рыночной капитализации индекс 500 крупнейших публичных компаний США. Широко признан эталоном для американского рынка акций и базой для большинства пассивных инвестиционных стратегий. Охватывает около 80% совокупной капитализации рынка США. Основное ограничение — концентрация в технологическом секторе и крупнейших компаниях. Состав и методологию определяет комитет S&P.", false, "S&P 500", "SPX", "S&P 500", 20, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, "COMP", "USA", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по капитализации индекс всех акций, торгующихся на бирже NASDAQ, — преимущественно технологических и растущих компаний. Включает более 3 000 ценных бумаг. Высокая концентрация в IT-секторе делает его чувствительным к изменениям процентных ставок и настроениям в отношении акций роста.", false, "NASDAQ Composite", "COMP", "NASDAQ COMPOSITE", 30, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, "NDX", "USA", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по капитализации индекс 100 крупнейших нефинансовых компаний, котирующихся на NASDAQ. Концентрация в технологии, потребительском секторе и здравоохранении делает его популярным инструментом для ставки на инновационные компании. Отсутствие финансового сектора — ключевое отличие от широкого рынка.", false, "NASDAQ-100", "NDX", "NASDAQ-100", 40, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, "RUT", "USA", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Индекс 2 000 компаний малой капитализации из Russell 3000. Традиционно используется как барометр внутреннего экономического здоровья США: малые компании менее зависимы от глобальных цепочек поставок. Отличается более высокой волатильностью и менее ликвидным составом по сравнению с крупнокапитализированными индексами.", false, "Russell 2000", "RUT", "RUSSELL 2000", 50, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 6, "UKX", "UK", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по капитализации индекс 100 крупнейших компаний Лондонской фондовой биржи. Значительную долю занимают горнодобывающие, энергетические и финансовые компании с глобальными операциями. Существенная часть выручки компонентов поступает из-за рубежа, поэтому индекс реагирует на курс фунта стерлингов.", false, "FTSE 100", "UKX", "FTSE 100", 60, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 7, "DAX", "Germany", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по капитализации индекс 40 крупнейших немецких компаний на бирже Xetra. Является главным барометром германского и, косвенно, европейского промышленного сектора. Особенность: DAX — индекс совокупного дохода (total return), включающий реинвестированные дивиденды, что делает его напрямую несопоставимым с price-return индексами.", false, "DAX", "DAX", "DAX", 70, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 8, "PX1", "France", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по капитализации (free-float) индекс 40 крупнейших компаний Парижской фондовой биржи Euronext. Репрезентирует французский рынок и отражает широкий спектр отраслей: предметы роскоши, энергетика, финансы, промышленность. Многие компоненты являются глобальными корпорациями, поэтому чувствительны к валютным курсам.", false, "CAC 40", "PX1", "CAC 40", 80, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 9, "SX5E", "Eurozone", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс 50 крупнейших компаний еврозоны из 8 стран. Широко используется как базовый актив для деривативов и ETF, ориентированных на европейский рынок. Концентрация в нескольких странах (Франция, Германия) и секторах (финансы, промышленность) ограничивает диверсификацию.", false, "EURO STOXX 50", "SX5E", "EURO STOXX 50", 90, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 10, "SXXP", "Europe", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс 600 компаний из 17 европейских стран, включая страны вне еврозоны (Великобритания, Швейцария, Скандинавия). Обеспечивает более широкое географическое и секторальное покрытие Европы, чем EURO STOXX 50. Применяется как эталон для паневропейских стратегий.", false, "STOXX Europe 600", "SXXP", "STOXX EUROPE 600", 100, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 11, "SMI", "Switzerland", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс 20 крупнейших компаний Швейцарской биржи. Сильно сконцентрирован в фармацевтике, финансах и товарах повседневного спроса. Выражен в швейцарских франках, которые традиционно считаются защитным активом. Высокая отраслевая концентрация является ключевым ограничением индекса.", false, "SMI", "SMI", "SMI", 110, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 12, "IBEX", "Spain", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс 35 наиболее ликвидных акций Испанской фондовой биржи. Значительную долю занимают банки, телекоммуникационные и энергетические компании. Индекс чувствителен к динамике кредитного рынка еврозоны и политической обстановке.", false, "IBEX 35", "IBEX", "IBEX 35", 120, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 13, "FTSEMIB", "Italy", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс 40 крупнейших и наиболее ликвидных итальянских компаний. Существенный вес имеют финансовый и коммунальный секторы. Высокая концентрация в банках делает индекс особенно чувствительным к ситуации с государственным долгом Италии.", false, "FTSE MIB", "FTSEMIB", "FTSE MIB", 130, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 14, "NKY", "Japan", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Ценово-взвешенный индекс 225 избранных акций Токийской фондовой биржи — аналог Доу-Джонса для японского рынка. Является наиболее известным барометром японского фондового рынка. Ценовое взвешивание создаёт те же искажения, что и в DJIA: высокостоимостные акции оказывают непропорциональное влияние.", false, "Nikkei 225", "NKY", "NIKKEI 225", 140, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 15, "TPX", "Japan", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс всех акций первой секции Токийской фондовой биржи — более 2 000 компаний. Считается более репрезентативным отражением японского рынка, чем Nikkei 225. Используется японскими институциональными инвесторами как основной эталон.", false, "TOPIX", "TPX", "TOPIX", 150, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 16, "HSI", "Hong Kong", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс крупнейших компаний Гонконгской фондовой биржи. Включает компании из материкового Китая, котирующиеся в Гонконге (акции H). Чувствителен к регуляторной политике материкового Китая и геополитическим рискам. Методология и состав регулярно пересматриваются.", false, "Hang Seng Index", "HSI", "HANG SENG INDEX", 160, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 17, "CSI300", "China", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс 300 крупнейших акций, торгующихся на Шанхайской и Шэньчжэньской биржах (акции А). Отражает динамику китайского внутреннего рынка, доступного через механизмы Stock Connect. Подвержен влиянию регуляторных изменений и ограничений на движение капитала.", false, "CSI 300", "CSI300", "CSI 300", 170, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 18, "SHCOMP", "China", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по капитализации индекс всех акций А и Б, котирующихся на Шанхайской фондовой бирже. Используется как широкий барометр китайского рынка, однако включает большое число малоликвидных компаний. Доступ иностранных инвесторов ограничен квотами и регуляторными требованиями.", false, "Shanghai Composite", "SHCOMP", "SHANGHAI COMPOSITE", 180, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 19, "AS51", "Australia", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс 200 крупнейших компаний Австралийской фондовой биржи. Сильно сконцентрирован в финансовом и горнодобывающем секторах. Динамика тесно связана с ценами на сырьё и торговыми отношениями с Китаем. Стандартный эталон для австралийских портфельных стратегий.", false, "S&P/ASX 200", "AS51", "S&P/ASX 200", 190, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 20, "SPTSX", "Canada", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по рыночной капитализации индекс всех компаний, удовлетворяющих критериям включения Торонтской фондовой биржи. Существенную долю занимают финансовый сектор, горнодобыча и нефтегазовая промышленность. Динамика коррелирует с ценами на сырьё. Основной эталон канадского рынка акций.", false, "S&P/TSX Composite", "SPTSX", "S&P/TSX COMPOSITE", 200, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 21, "SENSEX", "India", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Ценово-взвешенный индекс 30 хорошо зарекомендовавших себя компаний Бомбейской фондовой биржи. Один из старейших индикаторов индийского рынка. Малое число компонентов ограничивает репрезентативность. Чувствителен к изменениям в регуляторной среде, процентным ставкам Резервного банка Индии и состоянию банковского сектора.", false, "BSE SENSEX", "SENSEX", "BSE SENSEX", 210, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 22, "NIFTY50", "India", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс 50 крупнейших компаний Национальной фондовой биржи Индии. Охватывает 13 секторов и считается более репрезентативным эталоном индийского рынка, чем SENSEX. Широко используется как база для деривативов и пассивных инструментов.", false, "NIFTY 50", "NIFTY50", "NIFTY 50", 220, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 23, "KOSPI", "South Korea", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по рыночной капитализации индекс всех акций обыкновенных акций Корейской фондовой биржи. Существенную долю занимают технологические и автомобильные конгломераты. Чувствителен к геополитическим рискам на Корейском полуострове и глобальному спросу на полупроводники.", false, "KOSPI", "KOSPI", "KOSPI", 230, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 24, "IBOV", "Brazil", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по ликвидности торгов индекс наиболее торгуемых акций биржи B3. Высокую долю занимают финансовые, сырьевые и энергетические компании. Подвержен влиянию бразильского реала, политических рисков и мировых цен на commodities. Является основным эталоном бразильского рынка акций.", false, "Bovespa (IBOVESPA)", "IBOV", "BOVESPA (IBOVESPA)", 240, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 25, "MSCIW", "Developed Markets", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс акций крупной и средней капитализации из более 20 развитых стран. Охватывает около 85% рынка с поправкой на free-float в каждой стране. Широко используется как глобальный эталон для развитых рынков. Высокая доля США (более 60%) является ключевым ограничением географической диверсификации.", false, "MSCI World", "MSCIW", "MSCI WORLD", 250, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 26, "MSCIEM", "Emerging Markets", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс акций крупной и средней капитализации из более 20 развивающихся стран. Охватывает около 85% рынка с поправкой на free-float в каждой стране. Отражает возможности роста развивающихся экономик, но несёт повышенные риски: валютные, политические и ликвидности.", false, "MSCI Emerging Markets", "MSCIEM", "MSCI EMERGING MARKETS", 260, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 27, "MSCIACWI", "Global", new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Взвешенный по free-float капитализации индекс акций крупной и средней капитализации из 47 стран — как развитых, так и развивающихся рынков. Объединяет MSCI World и MSCI Emerging Markets. Используется как единый глобальный эталон. Высокая доля США сохраняется и в ACWI.", false, "MSCI ACWI", "MSCIACWI", "MSCI ACWI", 270, new DateTime(2026, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketIndices_NormalizedCode",
                table: "MarketIndices",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMarketIndices_MarketIndexId",
                table: "StockMarketIndices",
                column: "MarketIndexId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMarketIndices");

            migrationBuilder.DropTable(
                name: "MarketIndices");
        }
    }
}
