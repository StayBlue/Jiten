using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeckRelationships",
                schema: "jiten",
                columns: table => new
                {
                    SourceDeckId = table.Column<int>(type: "integer", nullable: false),
                    TargetDeckId = table.Column<int>(type: "integer", nullable: false),
                    RelationshipType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckRelationships", x => new { x.SourceDeckId, x.TargetDeckId, x.RelationshipType });
                    table.ForeignKey(
                        name: "FK_DeckRelationships_Decks_SourceDeckId",
                        column: x => x.SourceDeckId,
                        principalSchema: "jiten",
                        principalTable: "Decks",
                        principalColumn: "DeckId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeckRelationships_Decks_TargetDeckId",
                        column: x => x.TargetDeckId,
                        principalSchema: "jiten",
                        principalTable: "Decks",
                        principalColumn: "DeckId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeckRelationships_SourceDeckId",
                schema: "jiten",
                table: "DeckRelationships",
                column: "SourceDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_DeckRelationships_TargetDeckId",
                schema: "jiten",
                table: "DeckRelationships",
                column: "TargetDeckId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeckRelationships",
                schema: "jiten");
        }
    }
}
