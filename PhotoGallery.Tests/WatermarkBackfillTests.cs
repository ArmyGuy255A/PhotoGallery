using Xunit;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Services;
using PhotoGallery.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoGallery.Tests;

/// <summary>
/// Tests the on-demand watermark backfill in <see cref="PhotoVersionUrlService.GenerateShortLivedUrlAsync"/>.
/// Reference: D009 (Watermark Pipeline), bug #6 — legacy albums uploaded before PR #21
/// must self-heal their watermarked variants on first request from a public viewer.
/// </summary>
public class WatermarkBackfillTests
{
    private readonly Mock<IStorageProvider> _storage = new();
    private readonly Mock<IPhotoVersionUrlRepository> _urlRepo = new();
    private readonly Mock<IPhotoRepository> _photoRepo = new();
    private readonly Mock<ILogger<PhotoVersionUrlService>> _logger = new();
    private readonly Mock<ILogger<WatermarkService>> _watermarkLogger = new();
    private readonly IConfiguration _config;
    private readonly WatermarkService _watermarkService;
    private readonly PhotoVersionUrlService _service;

    public WatermarkBackfillTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"BlobStorage:PreSignedUrlTTLDays", "7"},
                {"BlobStorage:PreSignedUrlRefreshWindowDays", "5"},
                {"BlobStorage:CachedQualities:0", "Thumbnail"},
                {"BlobStorage:CachedQualities:1", "Medium"},
                {"BlobStorage:VerifyCachedUrls", "true"},
            })
            .Build();

        _watermarkService = new WatermarkService(_watermarkLogger.Object);

        _service = new PhotoVersionUrlService(
            _storage.Object,
            _urlRepo.Object,
            _photoRepo.Object,
            _config,
            _logger.Object,
            _watermarkService);
    }

    private static MemoryStream CreateJpegStream(int width = 200, int height = 200)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 160, 200));
        var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task GenerateShortLivedUrlAsync_BackfillsMissingWatermarkedThumbnail_ThenCachesForSubsequentCalls()
    {
        // Legacy album scenario: thumbnail.jpg exists in storage from an old upload, but
        // thumbnail-watermarked.jpg was never produced because the watermark pipeline didn't
        // exist when the photo was uploaded. The first GenerateShortLivedUrlAsync call must
        // generate-and-upload the watermarked variant; the second must reuse it.
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "legacy.jpg", UploadedBy = "Tester" };

        var unwatermarkedKey = $"photogallery/{albumId}/{photoId}/thumbnail.jpg";
        var watermarkedKey = $"photogallery/{albumId}/{photoId}/thumbnail-watermarked.jpg";

        _photoRepo.Setup(r => r.GetByIdAsync(photoId)).ReturnsAsync(photo);

        // Simulate storage state: watermarked missing initially, then present after upload.
        var watermarkedExists = false;
        _storage.Setup(s => s.ExistsAsync(watermarkedKey))
            .ReturnsAsync(() => watermarkedExists);
        _storage.Setup(s => s.ExistsAsync(unwatermarkedKey))
            .ReturnsAsync(true);

        _storage.Setup(s => s.DownloadAsync(unwatermarkedKey))
            .ReturnsAsync(() => CreateJpegStream());

        _storage.Setup(s => s.UploadAsync(watermarkedKey, It.IsAny<Stream>(), "image/jpeg"))
            .Callback(() => watermarkedExists = true)
            .ReturnsAsync(watermarkedKey);

        _storage.Setup(s => s.GetUrlAsync(watermarkedKey, It.IsAny<int>()))
            .ReturnsAsync($"http://minio/{watermarkedKey}?sig=fresh");

        // First call: backfill happens
        var firstUrl = await _service.GenerateShortLivedUrlAsync(
            photoId, QualityType.Thumbnail, ttlMinutes: 15, watermarked: true);

        Assert.NotNull(firstUrl);
        Assert.Contains("thumbnail-watermarked.jpg", firstUrl);
        _storage.Verify(s => s.DownloadAsync(unwatermarkedKey), Times.Once,
            "first call must download the unwatermarked source to backfill");
        _storage.Verify(s => s.UploadAsync(watermarkedKey, It.IsAny<Stream>(), "image/jpeg"), Times.Once,
            "first call must upload the freshly-watermarked variant");

        // Second call: the watermarked variant already exists; no re-generation
        var secondUrl = await _service.GenerateShortLivedUrlAsync(
            photoId, QualityType.Thumbnail, ttlMinutes: 15, watermarked: true);

        Assert.NotNull(secondUrl);
        Assert.Contains("thumbnail-watermarked.jpg", secondUrl);
        _storage.Verify(s => s.DownloadAsync(unwatermarkedKey), Times.Once,
            "second call must NOT re-download — cached variant should be reused");
        _storage.Verify(s => s.UploadAsync(watermarkedKey, It.IsAny<Stream>(), "image/jpeg"), Times.Once,
            "second call must NOT re-upload — cached variant should be reused");
    }

    [Fact]
    public async Task GenerateShortLivedUrlAsync_BackfillsMissingWatermarkedMedium_ThenCachesForSubsequentCalls()
    {
        // Same self-healing flow as the thumbnail case but for Medium, which is the original
        // shape of the pipeline (PR #21). Asserting both qualities ensures the parameterised
        // BuildWatermarkedStorageKey path works for either.
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "legacy.jpg", UploadedBy = "Tester" };

        var unwatermarkedKey = $"photogallery/{albumId}/{photoId}/medium.jpg";
        var watermarkedKey = $"photogallery/{albumId}/{photoId}/medium-watermarked.jpg";

        _photoRepo.Setup(r => r.GetByIdAsync(photoId)).ReturnsAsync(photo);

        var watermarkedExists = false;
        _storage.Setup(s => s.ExistsAsync(watermarkedKey)).ReturnsAsync(() => watermarkedExists);
        _storage.Setup(s => s.ExistsAsync(unwatermarkedKey)).ReturnsAsync(true);
        _storage.Setup(s => s.DownloadAsync(unwatermarkedKey)).ReturnsAsync(() => CreateJpegStream(800, 600));
        _storage.Setup(s => s.UploadAsync(watermarkedKey, It.IsAny<Stream>(), "image/jpeg"))
            .Callback(() => watermarkedExists = true)
            .ReturnsAsync(watermarkedKey);
        _storage.Setup(s => s.GetUrlAsync(watermarkedKey, It.IsAny<int>()))
            .ReturnsAsync($"http://minio/{watermarkedKey}?sig=fresh");

        await _service.GenerateShortLivedUrlAsync(photoId, QualityType.Medium, 15, watermarked: true);
        await _service.GenerateShortLivedUrlAsync(photoId, QualityType.Medium, 15, watermarked: true);

        _storage.Verify(s => s.UploadAsync(watermarkedKey, It.IsAny<Stream>(), "image/jpeg"), Times.Once);
    }

    [Fact]
    public async Task GenerateShortLivedUrlAsync_NeverWatermarksOriginalQuality_DefenseInDepth()
    {
        // PR-B is adding QualityType.Original. Even though we don't have access to that enum
        // value here, we cover the same defense-in-depth concern for High quality which
        // should also never be watermarked. The contract: only Thumbnail + Medium can be
        // watermarked. For any other quality, watermarked: true is silently ignored and the
        // service returns the unwatermarked URL.
        //
        // This test will keep working when PR-B's QualityType.Original lands, because the
        // High path exercises the same `quality != Medium && quality != Thumbnail` branch.
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "p.jpg", UploadedBy = "Tester" };

        var unwatermarkedKey = $"photogallery/{albumId}/{photoId}/high.jpg";
        var watermarkedKeyThatMustNotBeAccessed = $"photogallery/{albumId}/{photoId}/high-watermarked.jpg";

        _photoRepo.Setup(r => r.GetByIdAsync(photoId)).ReturnsAsync(photo);
        _storage.Setup(s => s.ExistsAsync(unwatermarkedKey)).ReturnsAsync(true);
        _storage.Setup(s => s.GetUrlAsync(unwatermarkedKey, It.IsAny<int>()))
            .ReturnsAsync($"http://minio/{unwatermarkedKey}?sig=fresh");

        var url = await _service.GenerateShortLivedUrlAsync(
            photoId, QualityType.High, ttlMinutes: 15, watermarked: true);

        Assert.NotNull(url);
        Assert.Contains("high.jpg", url);
        Assert.DoesNotContain("watermarked", url);

        // Crucially: no watermark generation attempted, no upload to a watermarked key,
        // even though the caller asked for watermarked: true.
        _storage.Verify(s => s.ExistsAsync(watermarkedKeyThatMustNotBeAccessed), Times.Never);
        _storage.Verify(s => s.DownloadAsync(It.IsAny<string>()), Times.Never);
        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
    }
}
