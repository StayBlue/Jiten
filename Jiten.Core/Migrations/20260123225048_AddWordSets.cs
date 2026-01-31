using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Jiten.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWordSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WordSets",
                schema: "jiten",
                columns: table => new
                {
                    SetId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordSets", x => x.SetId);
                });

            migrationBuilder.CreateTable(
                name: "WordSetMembers",
                schema: "jiten",
                columns: table => new
                {
                    SetId = table.Column<int>(type: "integer", nullable: false),
                    WordId = table.Column<int>(type: "integer", nullable: false),
                    ReadingIndex = table.Column<short>(type: "smallint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordSetMembers", x => new { x.SetId, x.WordId, x.ReadingIndex });
                    table.ForeignKey(
                        name: "FK_WordSetMembers_WordSets_SetId",
                        column: x => x.SetId,
                        principalSchema: "jiten",
                        principalTable: "WordSets",
                        principalColumn: "SetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WordSetMember_WordId_ReadingIndex",
                schema: "jiten",
                table: "WordSetMembers",
                columns: new[] { "WordId", "ReadingIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_WordSet_Slug",
                schema: "jiten",
                table: "WordSets",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WordSetMembers",
                schema: "jiten");

            migrationBuilder.DropTable(
                name: "WordSets",
                schema: "jiten");
        }
    }
}
