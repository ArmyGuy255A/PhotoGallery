using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    /// <inheritdoc />
    /// <remarks>
    /// SqlServer counterpart of UniquePhotoFileNamePerAlbum. Same shape,
    /// same destructive backfill (delete all-but-oldest duplicate rows
    /// before CREATE UNIQUE INDEX). The deployed Azure SQL `photogallery`
    /// DB has pre-existing duplicate-name photos from sessions before this
    /// fix shipped; without the backfill, the unique-index creation would
    /// fail at deploy-time. Orphan blob paths left after the row deletes
    /// are reclaimed by OrphanedBlobReaperService.
    /// </remarks>
    public partial class UniquePhotoFileNamePerAlbumSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ;WITH ranked AS (
                    SELECT Id, ROW_NUMBER() OVER (
                        PARTITION BY AlbumId, FileName
                        ORDER BY UploadDate ASC, Id ASC
                    ) AS rn
                    FROM Photos
                    WHERE ProcessingStatus <> 4
                )
                DELETE FROM Photos
                WHERE Id IN (SELECT Id FROM ranked WHERE rn > 1);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_AlbumId_FileName",
                table: "Photos",
                columns: new[] { "AlbumId", "FileName" },
                unique: true,
                filter: "[ProcessingStatus] <> 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_AlbumId_FileName",
                table: "Photos");
        }
    }
}
