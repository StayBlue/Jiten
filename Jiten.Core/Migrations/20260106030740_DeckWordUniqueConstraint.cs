using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class DeckWordUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeckWordReadingIndexDeck",
                schema: "jiten",
                table: "DeckWords");

            migrationBuilder.CreateIndex(
                name: "IX_DeckWords_DeckId_WordId_ReadingIndex",
                schema: "jiten",
                table: "DeckWords",
                columns: new[] { "DeckId", "WordId", "ReadingIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeckWords_DeckId_WordId_ReadingIndex",
                schema: "jiten",
                table: "DeckWords");

            migrationBuilder.CreateIndex(
                name: "IX_DeckWordReadingIndexDeck",
                schema: "jiten",
                table: "DeckWords",
                columns: new[] { "WordId", "ReadingIndex", "DeckId" });
        }
    }
}
