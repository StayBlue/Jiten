using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations.UserDb
{
    /// <inheritdoc />
    public partial class AddUserWordSetState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserWordSetStates",
                schema: "user",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SetId = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWordSetStates", x => new { x.UserId, x.SetId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserWordSetState_UserId",
                schema: "user",
                table: "UserWordSetStates",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserWordSetStates",
                schema: "user");
        }
    }
}
