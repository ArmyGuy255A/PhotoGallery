using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4ResilientRetries : Migration
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
                type: "TEXT",
                nullable: true);

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
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);
        }
    }
}
