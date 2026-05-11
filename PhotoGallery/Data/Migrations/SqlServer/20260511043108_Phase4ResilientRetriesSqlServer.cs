using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class Phase4ResilientRetriesSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxRetries",
                table: "ProcessingQueueItems");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "ProcessingQueueItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "Albums",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueueItems_Status_LeaseExpiresAt",
                table: "ProcessingQueueItems",
                columns: new[] { "Status", "LeaseExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessingQueueItems_Status_LeaseExpiresAt",
                table: "ProcessingQueueItems");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "ProcessingQueueItems");

            migrationBuilder.AddColumn<int>(
                name: "MaxRetries",
                table: "ProcessingQueueItems",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "Albums",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);
        }
    }
}
