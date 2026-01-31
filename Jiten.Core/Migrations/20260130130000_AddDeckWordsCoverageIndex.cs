using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddDeckWordsCoverageIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 CREATE INDEX IF NOT EXISTS "IX_DeckWords_WordId_ReadingIndex_IncDeckIdOcc"
                                 ON "jiten"."DeckWords" ("WordId", "ReadingIndex")
                                 INCLUDE ("DeckId", "Occurrences");
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 DROP INDEX IF EXISTS "jiten"."IX_DeckWords_WordId_ReadingIndex_IncDeckIdOcc";
                                 """);
        }
    }
}

