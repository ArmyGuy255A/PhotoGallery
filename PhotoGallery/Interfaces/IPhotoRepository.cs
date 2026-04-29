using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

public interface IPhotoRepository : IRepository<Photo>
{
    Task<List<Photo>> GetAlbumPhotosAsync(Guid albumId);
    Task<Photo?> GetWithVersionsAsync(Guid photoId);
    Task<List<Photo>> GetUnprocessedPhotosAsync();
}
