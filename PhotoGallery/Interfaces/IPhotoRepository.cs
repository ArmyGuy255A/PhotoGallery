using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

public interface IPhotoRepository : IRepository<Photo>
{
    Task<List<Photo>> GetAlbumPhotosAsync(Guid albumId);
    Task<Photo?> GetWithVersionsAsync(Guid photoId);
    Task<List<Photo>> GetUnprocessedPhotosAsync();

    /// <summary>
    /// Returns the set of file names currently occupied within the given
    /// album. Kept for the legacy multipart upload path; new callers should
    /// prefer <see cref="GetExistingPhotoSummariesByNameAsync"/> which also
    /// surfaces the existing Photo Id and processing status so the
    /// upload-tickets endpoint can short-circuit duplicates into an
    /// "already complete" response or recycle an abandoned Uploading row.
    /// </summary>
    Task<HashSet<string>> GetExistingFileNamesAsync(Guid albumId);

    /// <summary>
    /// Map of file name -> <see cref="ExistingPhotoSummary"/> (Id +
    /// processing status) for the album, used by
    /// <c>POST /api/photos/albums/{id}/upload-tickets</c>. Comparison is
    /// OrdinalIgnoreCase. Includes rows in every status; the controller
    /// recycles rows in <see cref="PhotoProcessingStatus.Uploading"/> (orphan
    /// from a prior failed ticket attempt) and treats every other status as
    /// "already complete from the SPA's point of view".
    /// </summary>
    Task<Dictionary<string, ExistingPhotoSummary>> GetExistingPhotoSummariesByNameAsync(Guid albumId);
}

public readonly record struct ExistingPhotoSummary(Guid Id, PhotoProcessingStatus Status);