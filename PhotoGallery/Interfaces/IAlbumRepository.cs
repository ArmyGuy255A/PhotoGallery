using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

public interface IAlbumRepository : IRepository<Album>
{
    Task<List<Album>> GetUserAlbumsAsync(Guid userId);
    Task<Album?> GetByIdWithPhotosAsync(Guid albumId);
    Task<Album?> GetByIdWithAccessCodesAsync(Guid albumId);
    Task<AccessCode?> GetAccessCodeByCodeAsync(string code);
    Task<bool> AccessCodeExistsAsync(string code);
}
