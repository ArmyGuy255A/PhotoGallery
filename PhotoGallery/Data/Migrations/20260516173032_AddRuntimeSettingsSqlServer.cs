using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSettingsSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuntimeSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
