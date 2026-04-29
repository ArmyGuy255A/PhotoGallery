using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

public interface IAccessCodeRepository : IRepository<AccessCode>
{
    Task<AccessCode?> GetByCodeAsync(string code);
    Task<List<AccessCode>> GetAlbumCodesAsync(Guid albumId);
    Task<List<AccessCode>> GetValidCodesAsync(Guid albumId);
    Task<bool> IsCodeValidAsync(string code);
}
