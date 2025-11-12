using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations.UserDb
{
    /// <inheritdoc />
    public partial class AddUserPreferenceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserDeckPreference_UserId_IsIgnored",
                schema: "user",
                table: "UserDeckPreferences",
                columns: new[] { "UserId", "IsIgnored" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserDeckPreference_UserId_IsIgnored",
                schema: "user",
                table: "UserDeckPreferences");
        }
    }
}
