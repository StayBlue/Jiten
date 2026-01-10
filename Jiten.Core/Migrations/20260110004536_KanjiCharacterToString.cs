using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class KanjiCharacterToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "KanjiCharacter",
                schema: "jmdict",
                table: "WordKanji",
                type: "text",
                nullable: false,
                oldClrType: typeof(char),
                oldType: "char(1)");

            migrationBuilder.AlterColumn<string>(
                name: "Character",
                schema: "jmdict",
                table: "Kanji",
                type: "text",
                nullable: false,
                oldClrType: typeof(char),
                oldType: "char(1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<char>(
                name: "KanjiCharacter",
                schema: "jmdict",
                table: "WordKanji",
                type: "char(1)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<char>(
                name: "Character",
                schema: "jmdict",
                table: "Kanji",
                type: "char(1)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
