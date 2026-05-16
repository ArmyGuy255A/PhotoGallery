using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteAccessCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessCodes_Albums_AlbumId",
                table: "AccessCodes");

            migrationBuilder.AlterColumn<Guid>(
                name: "AlbumId",
                table: "AccessCodes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "DeletedAlbumTitle",
                table: "AccessCodes",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "AccessCodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AccessCodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AccessCodes_IsDeleted",
                table: "AccessCodes",
                column: "IsDeleted");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessCodes_Albums_AlbumId",
                table: "AccessCodes",
                column: "AlbumId",
                principalTable: "Albums",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessCodes_Albums_AlbumId",
                table: "AccessCodes");

            migrationBuilder.DropIndex(
                name: "IX_AccessCodes_IsDeleted",
                table: "AccessCodes");

            migrationBuilder.DropColumn(
                name: "DeletedAlbumTitle",
                table: "AccessCodes");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "AccessCodes");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AccessCodes");

            migrationBuilder.AlterColumn<Guid>(
                name: "AlbumId",
                table: "AccessCodes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessCodes_Albums_AlbumId",
                table: "AccessCodes",
                column: "AlbumId",
                principalTable: "Albums",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
