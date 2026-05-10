using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCartItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserCartItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PhotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceAlbumId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCartItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCartItems_Albums_SourceAlbumId",
                        column: x => x.SourceAlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserCartItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCartItems_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCartItems_PhotoId",
                table: "UserCartItems",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCartItems_SourceAlbumId",
                table: "UserCartItems",
                column: "SourceAlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCartItems_UserId",
                table: "UserCartItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCartItems_UserId_PhotoId_Quality",
                table: "UserCartItems",
                columns: new[] { "UserId", "PhotoId", "Quality" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCartItems");
        }
    }
}
