using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
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
            // Capture the doomed Photo Ids into a temp table so we can also
            // clean up child rows whose FK uses ON DELETE RESTRICT (Downloads,
            // ProcessingQueues, ProcessingQueueItems, UserCartItems). The
            // cascade-configured children (PhotoFiles, PhotoVersions,
            // PhotoVersionUrls) clean themselves up when the parent row goes.
            migrationBuilder.Sql(@"
                IF OBJECT_ID('tempdb..#DupePhotos') IS NOT NULL DROP TABLE #DupePhotos;
                ;WITH ranked AS (
                    SELECT Id, ROW_NUMBER() OVER (
                        PARTITION BY AlbumId, FileName
                        ORDER BY UploadDate ASC, Id ASC
                    ) AS rn
                    FROM Photos
                    WHERE ProcessingStatus <> 4
                )
                SELECT Id INTO #DupePhotos FROM ranked WHERE rn > 1;

                DELETE FROM Downloads            WHERE PhotoId IN (SELECT Id FROM #DupePhotos);
                DELETE FROM UserCartItems        WHERE PhotoId IN (SELECT Id FROM #DupePhotos);
                DELETE FROM ProcessingQueueItems WHERE PhotoId IN (SELECT Id FROM #DupePhotos);
                DELETE FROM ProcessingQueues     WHERE PhotoId IN (SELECT Id FROM #DupePhotos);
                DELETE FROM Photos               WHERE Id      IN (SELECT Id FROM #DupePhotos);

                DROP TABLE #DupePhotos;
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
