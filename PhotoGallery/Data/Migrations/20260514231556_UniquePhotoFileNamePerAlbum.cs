using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    /// <inheritdoc />
    /// <remarks>
    /// Adds a UNIQUE index on (AlbumId, FileName) excluding rows still in
    /// PhotoProcessingStatus.Uploading (=4). The controller-level check in
    /// PhotosController is the primary line of defence, this index is the
    /// belt-and-braces guarantee at the database level.
    ///
    /// DESTRUCTIVE BACKFILL: before creating the unique index, any pre-existing
    /// duplicate (AlbumId, FileName) tuples are collapsed by deleting all but
    /// the oldest row per group (ordered by UploadDate ASC, Id ASC tiebreak).
    /// Cascade deletes configured on Photo remove the dependent PhotoVersions,
    /// PhotoFiles, and ProcessingQueue rows. Orphaned blob paths left behind
    /// by the deletes are reclaimed by OrphanedBlobReaperService on its next
    /// pass, so the migration intentionally does not touch storage. This
    /// step is required because the deployed Azure SQL `photogallery` DB
    /// already contains duplicate-name photos from sessions before the fix;
    /// without the backfill, CREATE UNIQUE INDEX would fail at deploy time.
    /// </remarks>
    public partial class UniquePhotoFileNamePerAlbum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sqlite supports window functions (>= 3.25) so the same shape
            // works here as on SqlServer. Local-dev DBs are usually empty,
            // making this a no-op, but we keep the step so the migration
            // behaves identically on every provider.
            migrationBuilder.Sql(@"
                DELETE FROM Photos
                WHERE Id IN (
                    SELECT Id FROM (
                        SELECT Id, ROW_NUMBER() OVER (
                            PARTITION BY AlbumId, FileName
                            ORDER BY UploadDate ASC, Id ASC
                        ) AS rn
                        FROM Photos
                        WHERE ProcessingStatus <> 4
                    ) ranked
                    WHERE rn > 1
                );
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
