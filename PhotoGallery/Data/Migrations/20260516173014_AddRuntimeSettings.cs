using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuntimeSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LastModifiedBy = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeSettings_Category",
                table: "RuntimeSettings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeSettings_Key",
                table: "RuntimeSettings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeSettings");
        }
    }
}
