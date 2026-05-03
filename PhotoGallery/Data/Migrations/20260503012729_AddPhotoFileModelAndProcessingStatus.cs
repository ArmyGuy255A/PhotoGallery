using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoFileModelAndProcessingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasHigh",
                table: "Photos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasLow",
                table: "Photos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasMedium",
                table: "Photos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasThumbnail",
                table: "Photos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingCompletedAt",
                table: "Photos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                table: "Photos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingStatus",
                table: "Photos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PhotoFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    BlobPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoFiles_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Photos_ProcessingStatus",
                table: "Photos",
                column: "ProcessingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoFiles_PhotoId",
                table: "PhotoFiles",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoFiles_PhotoId_Quality",
                table: "PhotoFiles",
                columns: new[] { "PhotoId", "Quality" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoFiles");

            migrationBuilder.DropIndex(
                name: "IX_Photos_ProcessingStatus",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "HasHigh",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "HasLow",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "HasMedium",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "HasThumbnail",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ProcessingCompletedAt",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ProcessingStatus",
                table: "Photos");
        }
    }
}
