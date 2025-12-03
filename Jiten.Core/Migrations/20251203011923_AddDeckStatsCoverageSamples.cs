using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddDeckStatsCoverageSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverageSamplesJson",
                schema: "jiten",
                table: "DeckStats",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverageSamplesJson",
                schema: "jiten",
                table: "DeckStats");
        }
    }
}
