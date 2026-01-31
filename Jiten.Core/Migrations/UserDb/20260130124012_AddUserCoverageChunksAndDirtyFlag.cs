using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jiten.Core.Migrations.UserDb
{
    /// <inheritdoc />
    public partial class _Temp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                                             name: "CoverageDirty",
                                             schema: "user",
                                             table: "UserMetadatas",
                                             type: "boolean",
                                             nullable: false,
                                             defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                                                 name: "CoverageDirtyAt",
                                                 schema: "user",
                                                 table: "UserMetadatas",
                                                 type: "timestamp with time zone",
                                                 nullable: true);

            migrationBuilder.CreateTable(
                                         name: "UserCoverageChunks",
                                         schema: "user",
                                         columns: table => new
                                                           {
                                                               UserId = table.Column<string>(type: "uuid", nullable: false),
                                                               Metric = table.Column<short>(type: "smallint", nullable: false),
                                                               ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                                                               Values = table.Column<short[]>(type: "smallint[]", nullable: false),
                                                               ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                                                           },
                                         constraints: table =>
                                         {
                                             table.PrimaryKey("PK_UserCoverageChunks", x => new { x.UserId, x.Metric, x.ChunkIndex });
                                             table.ForeignKey(
                                                              name: "FK_UserCoverageChunks_AspNetUsers_UserId",
                                                              column: x => x.UserId,
                                                              principalSchema: "user",
                                                              principalTable: "AspNetUsers",
                                                              principalColumn: "Id",
                                                              onDelete: ReferentialAction.Cascade);
                                         });

            migrationBuilder.CreateIndex(
                                         name: "IX_UserCoverageChunks_UserId",
                                         schema: "user",
                                         table: "UserCoverageChunks",
                                         column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCoverageChunks",
                schema: "user");

            migrationBuilder.DropColumn(
                name: "CoverageDirty",
                schema: "user",
                table: "UserMetadatas");

            migrationBuilder.DropColumn(
                name: "CoverageDirtyAt",
                schema: "user",
                table: "UserMetadatas");
        }
    }
}
