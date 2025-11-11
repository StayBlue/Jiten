using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations.UserDb
{
    /// <inheritdoc />
    public partial class AddUserDeckPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserDeckPreferences",
                schema: "user",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeckId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsFavourite = table.Column<bool>(type: "boolean", nullable: false),
                    IsIgnored = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDeckPreferences", x => new { x.UserId, x.DeckId });
                    table.ForeignKey(
                        name: "FK_UserDeckPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "user",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDeckPreference_UserId",
                schema: "user",
                table: "UserDeckPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDeckPreference_UserId_IsFavourite",
                schema: "user",
                table: "UserDeckPreferences",
                columns: new[] { "UserId", "IsFavourite" });

            migrationBuilder.CreateIndex(
                name: "IX_UserDeckPreference_UserId_Status",
                schema: "user",
                table: "UserDeckPreferences",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDeckPreferences",
                schema: "user");
        }
    }
}
