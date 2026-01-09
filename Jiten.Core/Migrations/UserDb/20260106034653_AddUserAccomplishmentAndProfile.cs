using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Jiten.Core.Migrations.UserDb
{
    /// <inheritdoc />
    public partial class AddUserAccomplishmentAndProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAccomplishments",
                schema: "user",
                columns: table => new
                {
                    AccomplishmentId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: true),
                    CompletedDeckCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCharacterCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalWordCount = table.Column<long>(type: "bigint", nullable: false),
                    UniqueWordCount = table.Column<int>(type: "integer", nullable: false),
                    UniqueWordUsedOnceCount = table.Column<int>(type: "integer", nullable: false),
                    UniqueKanjiCount = table.Column<int>(type: "integer", nullable: false),
                    LastComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccomplishments", x => x.AccomplishmentId);
                    table.ForeignKey(
                        name: "FK_UserAccomplishments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "user",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                schema: "user",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "user",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccomplishment_UserId",
                schema: "user",
                table: "UserAccomplishments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccomplishment_UserId_MediaType",
                schema: "user",
                table: "UserAccomplishments",
                columns: new[] { "UserId", "MediaType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAccomplishments",
                schema: "user");

            migrationBuilder.DropTable(
                name: "UserProfiles",
                schema: "user");
        }
    }
}
