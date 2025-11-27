using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class GenresAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeckGenres",
                schema: "jiten",
                columns: table => new
                {
                    DeckId = table.Column<int>(type: "integer", nullable: false),
                    Genre = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckGenres", x => new { x.DeckId, x.Genre });
                    table.ForeignKey(
                        name: "FK_DeckGenres_Decks_DeckId",
                        column: x => x.DeckId,
                        principalSchema: "jiten",
                        principalTable: "Decks",
                        principalColumn: "DeckId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalGenreMappings",
                schema: "jiten",
                columns: table => new
                {
                    ExternalGenreMappingId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ExternalGenreName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    JitenGenre = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalGenreMappings", x => x.ExternalGenreMappingId);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                schema: "jiten",
                columns: table => new
                {
                    TagId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.TagId);
                });

            migrationBuilder.CreateTable(
                name: "DeckTags",
                schema: "jiten",
                columns: table => new
                {
                    DeckId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    Percentage = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckTags", x => new { x.DeckId, x.TagId });
                    table.CheckConstraint("CK_DeckTags_Percentage", "\"Percentage\" >= 0 AND \"Percentage\" <= 100");
                    table.ForeignKey(
                        name: "FK_DeckTags_Decks_DeckId",
                        column: x => x.DeckId,
                        principalSchema: "jiten",
                        principalTable: "Decks",
                        principalColumn: "DeckId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeckTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "jiten",
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalTagMappings",
                schema: "jiten",
                columns: table => new
                {
                    ExternalTagMappingId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ExternalTagName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalTagMappings", x => x.ExternalTagMappingId);
                    table.ForeignKey(
                        name: "FK_ExternalTagMappings_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "jiten",
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeckGenres_DeckId",
                schema: "jiten",
                table: "DeckGenres",
                column: "DeckId");

            migrationBuilder.CreateIndex(
                name: "IX_DeckGenres_Genre",
                schema: "jiten",
                table: "DeckGenres",
                column: "Genre");

            migrationBuilder.CreateIndex(
                name: "IX_DeckTags_Percentage",
                schema: "jiten",
                table: "DeckTags",
                column: "Percentage");

            migrationBuilder.CreateIndex(
                name: "IX_DeckTags_TagId",
                schema: "jiten",
                table: "DeckTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalGenreMapping_Provider_ExternalName",
                schema: "jiten",
                table: "ExternalGenreMappings",
                columns: new[] { "Provider", "ExternalGenreName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalGenreMapping_Provider_ExternalName_JitenGenre",
                schema: "jiten",
                table: "ExternalGenreMappings",
                columns: new[] { "Provider", "ExternalGenreName", "JitenGenre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalTagMapping_Provider_ExternalName",
                schema: "jiten",
                table: "ExternalTagMappings",
                columns: new[] { "Provider", "ExternalTagName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalTagMapping_Provider_ExternalName_TagId",
                schema: "jiten",
                table: "ExternalTagMappings",
                columns: new[] { "Provider", "ExternalTagName", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalTagMappings_TagId",
                schema: "jiten",
                table: "ExternalTagMappings",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                schema: "jiten",
                table: "Tags",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeckGenres",
                schema: "jiten");

            migrationBuilder.DropTable(
                name: "DeckTags",
                schema: "jiten");

            migrationBuilder.DropTable(
                name: "ExternalGenreMappings",
                schema: "jiten");

            migrationBuilder.DropTable(
                name: "ExternalTagMappings",
                schema: "jiten");

            migrationBuilder.DropTable(
                name: "Tags",
                schema: "jiten");
        }
    }
}
