using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ActorEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Action",
                table: "AuditLogEntries",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Timestamp",
                table: "AuditLogEntries",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogEntries");
        }
    }
}
