using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminJobSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AlbumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByInstanceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminJobs_JobType_Status",
                table: "AdminJobs",
                columns: new[] { "JobType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminJobs_RequestedAt",
                table: "AdminJobs",
                column: "RequestedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminJobs");
        }
    }
}
