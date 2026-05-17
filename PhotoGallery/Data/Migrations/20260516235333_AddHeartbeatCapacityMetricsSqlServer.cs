using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHeartbeatCapacityMetricsSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CpuPercent",
                table: "WorkerHeartbeats",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ManagedHeapBytes",
                table: "WorkerHeartbeats",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WorkingSetBytes",
                table: "WorkerHeartbeats",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuPercent",
                table: "WorkerHeartbeats");

            migrationBuilder.DropColumn(
                name: "ManagedHeapBytes",
                table: "WorkerHeartbeats");

            migrationBuilder.DropColumn(
                name: "WorkingSetBytes",
                table: "WorkerHeartbeats");
        }
    }
}
