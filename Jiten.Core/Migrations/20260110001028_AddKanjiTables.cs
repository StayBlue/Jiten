using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddKanjiTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Kanji",
                schema: "jmdict",
                columns: table => new
                {
                    Character = table.Column<char>(type: "char(1)", nullable: false),
                    OnReadings = table.Column<List<string>>(type: "text[]", nullable: false),
                    KunReadings = table.Column<List<string>>(type: "text[]", nullable: false),
                    Meanings = table.Column<List<string>>(type: "text[]", nullable: false),
                    StrokeCount = table.Column<short>(type: "smallint", nullable: false),
                    JlptLevel = table.Column<short>(type: "smallint", nullable: true),
                    Grade = table.Column<short>(type: "smallint", nullable: true),
                    FrequencyRank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kanji", x => x.Character);
                });

            migrationBuilder.CreateTable(
                name: "WordKanji",
                schema: "jmdict",
                columns: table => new
                {
                    WordId = table.Column<int>(type: "integer", nullable: false),
                    ReadingIndex = table.Column<short>(type: "smallint", nullable: false),
                    KanjiCharacter = table.Column<char>(type: "char(1)", nullable: false),
                    Position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordKanji", x => new { x.WordId, x.ReadingIndex, x.KanjiCharacter, x.Position });
                    table.ForeignKey(
                        name: "FK_WordKanji_Kanji_KanjiCharacter",
                        column: x => x.KanjiCharacter,
                        principalSchema: "jmdict",
                        principalTable: "Kanji",
                        principalColumn: "Character",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WordKanji_Words_WordId",
                        column: x => x.WordId,
                        principalSchema: "jmdict",
                        principalTable: "Words",
                        principalColumn: "WordId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Kanji_FrequencyRank",
                schema: "jmdict",
                table: "Kanji",
                column: "FrequencyRank");

            migrationBuilder.CreateIndex(
                name: "IX_Kanji_JlptLevel",
                schema: "jmdict",
                table: "Kanji",
                column: "JlptLevel");

            migrationBuilder.CreateIndex(
                name: "IX_Kanji_StrokeCount",
                schema: "jmdict",
                table: "Kanji",
                column: "StrokeCount");

            migrationBuilder.CreateIndex(
                name: "IX_WordKanji_KanjiCharacter",
                schema: "jmdict",
                table: "WordKanji",
                column: "KanjiCharacter");

            migrationBuilder.CreateIndex(
                name: "IX_WordKanji_WordId_ReadingIndex",
                schema: "jmdict",
                table: "WordKanji",
                columns: new[] { "WordId", "ReadingIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WordKanji",
                schema: "jmdict");

            migrationBuilder.DropTable(
                name: "Kanji",
                schema: "jmdict");
        }
    }
}
