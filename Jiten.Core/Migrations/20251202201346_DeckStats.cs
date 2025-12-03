using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class DeckStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeckStats",
                schema: "jiten",
                columns: table => new
                {
                    DeckId = table.Column<int>(type: "integer", nullable: false),
                    CoverageCurve = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckStats", x => x.DeckId);
                    table.ForeignKey(
                        name: "FK_DeckStats_Decks_DeckId",
                        column: x => x.DeckId,
                        principalSchema: "jiten",
                        principalTable: "Decks",
                        principalColumn: "DeckId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeckStats_ComputedAt",
                schema: "jiten",
                table: "DeckStats",
                column: "ComputedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeckStats",
                schema: "jiten");
        }
    }
}
