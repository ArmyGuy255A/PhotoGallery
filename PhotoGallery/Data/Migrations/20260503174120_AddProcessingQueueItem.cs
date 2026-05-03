using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingQueueItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessingQueues_QueuedDate",
                table: "ProcessingQueues");

            migrationBuilder.DropIndex(
                name: "IX_ProcessingQueues_Status",
                table: "ProcessingQueues");

            migrationBuilder.DropColumn(
                name: "QueuedDate",
                table: "ProcessingQueues");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "ProcessingQueues");

            migrationBuilder.RenameColumn(
                name: "ProcessedDate",
                table: "ProcessingQueues",
                newName: "CompletedAt");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "ProcessingQueues",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.CreateTable(
                name: "ProcessingQueueItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessingQueueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    MaxRetries = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    NextRetryTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingQueueItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingQueueItems_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessingQueueItems_ProcessingQueues_ProcessingQueueId",
                        column: x => x.ProcessingQueueId,
                        principalTable: "ProcessingQueues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueues_CreatedAt",
                table: "ProcessingQueues",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueues_Status",
                table: "ProcessingQueues",
                column: "Status",
                filter: "[Status] = 0 OR [Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueueItems_PhotoId",
                table: "ProcessingQueueItems",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueueItems_ProcessingQueueId",
                table: "ProcessingQueueItems",
                column: "ProcessingQueueId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueueItems_ProcessingQueueId_Quality",
                table: "ProcessingQueueItems",
                columns: new[] { "ProcessingQueueId", "Quality" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueueItems_Quality",
                table: "ProcessingQueueItems",
                column: "Quality");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueueItems_Status",
                table: "ProcessingQueueItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueueItems_Status_NextRetryTime",
                table: "ProcessingQueueItems",
                columns: new[] { "Status", "NextRetryTime" },
                filter: "[Status] = 3 AND [NextRetryTime] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessingQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_ProcessingQueues_CreatedAt",
                table: "ProcessingQueues");

            migrationBuilder.DropIndex(
                name: "IX_ProcessingQueues_Status",
                table: "ProcessingQueues");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ProcessingQueues");

            migrationBuilder.RenameColumn(
                name: "CompletedAt",
                table: "ProcessingQueues",
                newName: "ProcessedDate");

            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedDate",
                table: "ProcessingQueues",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "ProcessingQueues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueues_QueuedDate",
                table: "ProcessingQueues",
                column: "QueuedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingQueues_Status",
                table: "ProcessingQueues",
                column: "Status");
        }
    }
}
