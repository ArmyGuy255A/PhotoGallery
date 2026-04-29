using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Controllers;

/// <summary>
/// API endpoints for statistics (authenticated users only)
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly IRepository<Photo> _photoRepository;
    private readonly IAccessCodeRepository _accessCodeRepository;
    private readonly ILogger<StatsController> _logger;

    public StatsController(
        IRepository<Photo> photoRepository,
        IAccessCodeRepository accessCodeRepository,
        ILogger<StatsController> logger)
    {
        _photoRepository = photoRepository;
        _accessCodeRepository = accessCodeRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get total count of photos
    /// </summary>
    /// <returns>Total photo count</returns>
    [HttpGet("photos")]
    public async Task<ActionResult<StatsCountDto>> GetPhotosCount()
    {
        var allPhotos = await _photoRepository.GetAllAsync();
        var count = allPhotos.Count();

        _logger.LogInformation("Retrieved total photo count: {PhotoCount}", count);
        return Ok(new StatsCountDto { Count = count });
    }

    /// <summary>
    /// Get count of active (non-expired) access codes
    /// </summary>
    /// <returns>Active access code count</returns>
    [HttpGet("access-codes")]
    public async Task<ActionResult<StatsCountDto>> GetActiveAccessCodesCount()
    {
        var allCodes = await _accessCodeRepository.GetAllAsync();
        var activeCodes = allCodes
            .Where(c => !c.ExpirationDate.HasValue || c.ExpirationDate > DateTime.UtcNow)
            .ToList();

        var count = activeCodes.Count;

        _logger.LogInformation("Retrieved active access code count: {CodeCount}", count);
        return Ok(new StatsCountDto { Count = count });
    }
}

/// <summary>
/// DTO for statistics count response
/// </summary>
public class StatsCountDto
{
    public int Count { get; set; }
}
