using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedAccessCodeTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedAccessCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    AccessCodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedAccessCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedAccessCodes_AccessCodes_AccessCodeId",
                        column: x => x.AccessCodeId,
                        principalTable: "AccessCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavedAccessCodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedAccessCodes_AccessCodeId",
                table: "SavedAccessCodes",
                column: "AccessCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedAccessCodes_UserId",
                table: "SavedAccessCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedAccessCodes_UserId_AccessCodeId",
                table: "SavedAccessCodes",
                columns: new[] { "UserId", "AccessCodeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedAccessCodes");
        }
    }
}
