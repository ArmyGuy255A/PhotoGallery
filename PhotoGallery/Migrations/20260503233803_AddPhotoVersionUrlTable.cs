using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoVersionUrlTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoVersionUrls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    PresignedUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoVersionUrls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoVersionUrls_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVersionUrls_ExpiresAt",
                table: "PhotoVersionUrls",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVersionUrls_IsActive",
                table: "PhotoVersionUrls",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVersionUrls_PhotoId_Quality",
                table: "PhotoVersionUrls",
                columns: new[] { "PhotoId", "Quality" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoVersionUrls");
        }
    }
}
