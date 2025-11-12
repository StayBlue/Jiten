using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Decks_ParentDeckId",
                schema: "jiten",
                table: "Decks");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterCount",
                schema: "jiten",
                table: "Decks",
                column: "CharacterCount");

            migrationBuilder.CreateIndex(
                name: "IX_Difficulty",
                schema: "jiten",
                table: "Decks",
                column: "Difficulty");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalRating",
                schema: "jiten",
                table: "Decks",
                column: "ExternalRating");

            migrationBuilder.CreateIndex(
                name: "IX_ParentDeckId_MediaType",
                schema: "jiten",
                table: "Decks",
                columns: new[] { "ParentDeckId", "MediaType" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseDate",
                schema: "jiten",
                table: "Decks",
                column: "ReleaseDate");

            migrationBuilder.CreateIndex(
                name: "IX_UniqueKanjiCount",
                schema: "jiten",
                table: "Decks",
                column: "UniqueKanjiCount");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharacterCount",
                schema: "jiten",
                table: "Decks");

            migrationBuilder.DropIndex(
                name: "IX_Difficulty",
                schema: "jiten",
                table: "Decks");

            migrationBuilder.DropIndex(
                name: "IX_ExternalRating",
                schema: "jiten",
                table: "Decks");

            migrationBuilder.DropIndex(
                name: "IX_ParentDeckId_MediaType",
                schema: "jiten",
                table: "Decks");

            migrationBuilder.DropIndex(
                name: "IX_ReleaseDate",
                schema: "jiten",
                table: "Decks");

            migrationBuilder.DropIndex(
                name: "IX_UniqueKanjiCount",
                schema: "jiten",
                table: "Decks");

            migrationBuilder.CreateIndex(
                name: "IX_Decks_ParentDeckId",
                schema: "jiten",
                table: "Decks",
                column: "ParentDeckId");
        }
    }
}
