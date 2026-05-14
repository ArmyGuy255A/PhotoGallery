using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

public interface IPhotoRepository : IRepository<Photo>
{
    Task<List<Photo>> GetAlbumPhotosAsync(Guid albumId);
    Task<Photo?> GetWithVersionsAsync(Guid photoId);
    Task<List<Photo>> GetUnprocessedPhotosAsync();

    /// <summary>
    /// Returns the set of file names currently occupied within the given
    /// album, used by the upload paths to reject duplicate names before
    /// minting a SAS or staging a multipart write. Includes rows in any
    /// processing status, including <see cref="PhotoProcessingStatus.Uploading"/>,
    /// so concurrent uploads of the same name can't both win. Abandoned
    /// Uploading rows are swept by <c>OrphanedBlobReaperService</c> within
    /// <c>Storage:OrphanReapGraceMinutes</c>, after which the name becomes
    /// available again.
    /// </summary>
    Task<HashSet<string>> GetExistingFileNamesAsync(Guid albumId);
}
