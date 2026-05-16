using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkerHeartbeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkerName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRanAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ItemsProcessedTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemsInFlight = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerHeartbeats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerHeartbeats_LastHeartbeatAt",
                table: "WorkerHeartbeats",
                column: "LastHeartbeatAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerHeartbeats_WorkerName_InstanceId",
                table: "WorkerHeartbeats",
                columns: new[] { "WorkerName", "InstanceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerHeartbeats");
        }
    }
}
