using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Controllers;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// API-level tests for <c>POST /api/code/{code}/cart/manifest</c>.
/// Replaces <c>ZipDownloadServiceTests</c> in the manifest-based world.
///
/// Coverage:
/// - Happy path returns manifest with album title, prefix, items
/// - Dedupe on (photoId, quality)
/// - Expired access code → 403
/// - Missing access code → 404
/// - Missing album → 404
/// - Thumbnail rejected
/// - 100-cap enforced
/// - Cross-album photo (ownership scope) silently dropped
/// - Original quality accepted (PR-B added the enum value)
/// - Per-item Download analytics row is written when URL is issued
/// </summary>
public class CartManifestApiTests
{
    private readonly Mock<IAccessCodeRepository> _accessCodes = new();
    private readonly Mock<IPhotoRepository> _photos = new();
    private readonly Mock<IRepository<PhotoVersion>> _photoVersions = new();
    private readonly Mock<IRepository<Album>> _albums = new();
    private readonly Mock<IStorageProvider> _storage = new();
    private readonly Mock<IImageProcessor> _imageProcessor = new();
    private readonly Mock<IPhotoVersionUrlRepository> _urlRepo = new();
    private readonly Mock<IRepository<Download>> _downloads = new();
    private readonly PhotoVersionUrlService _urlService;
    private readonly ZipDownloadService _zipService; // legacy — controller still requires it
    private readonly Mock<ILogger<AccessCodeController>> _logger = new();

    public CartManifestApiTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"BlobStorage:PreSignedUrlTTLDays", "7"},
                {"BlobStorage:PreSignedUrlRefreshWindowDays", "5"},
                {"BlobStorage:CachedQualities:0", "Thumbnail"},
                {"BlobStorage:CachedQualities:1", "Medium"},
                {"BlobStorage:VerifyCachedUrls", "false"},
            })
            .Build();

        _urlService = new PhotoVersionUrlService(
            _storage.Object,
            _urlRepo.Object,
            _photos.Object,
            configuration,
            new NullLogger<PhotoVersionUrlService>());

        _zipService = new ZipDownloadService(
            _storage.Object,
            _downloads.Object,
            _photos.Object,
            new NullLogger<ZipDownloadService>());
    }

    private AccessCodeController NewController(string remoteIp = "203.0.113.5")
    {
        var controller = new AccessCodeController(
            _accessCodes.Object,
            _photos.Object,
            _photoVersions.Object,
            _albums.Object,
            _storage.Object,
            _imageProcessor.Object,
            _urlService,
            _zipService,
            _downloads.Object,
            _logger.Object);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        return controller;
    }

    private (AccessCode code, Album album) SeedValidCode(string code = "VALIDCODE")
    {
        var album = new Album { Id = Guid.NewGuid(), Title = "Wedding 2026" };
        var accessCode = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            Code = code,
            CreatedDate = DateTime.UtcNow,
            ExpirationDate = null
        };
        _accessCodes.Setup(r => r.GetByCodeAsync(code)).ReturnsAsync(accessCode);
        _albums.Setup(r => r.GetByIdAsync(album.Id)).ReturnsAsync(album);
        return (accessCode, album);
    }

    private Photo SeedPhoto(Guid albumId, string fileName = "IMG_0001.jpg")
    {
        var photo = new Photo { Id = Guid.NewGuid(), AlbumId = albumId, FileName = fileName };
        _photos.Setup(r => r.GetByIdAsync(photo.Id)).ReturnsAsync(photo);
        return photo;
    }

    /// <summary>Make storage report that *every* requested key exists and presigned URL is constructible.</summary>
    private void StorageHasEverything()
    {
        _storage.Setup(s => s.ExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        _storage.Setup(s => s.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync((string key, int _) => $"https://minio.test/{key}?X-Amz-Signature=fake");
    }

    [Fact]
    public async Task CartManifest_HappyPath_ReturnsManifestWithAlbumTitleAndItems()
    {
        var (code, album) = SeedValidCode();
        var photo1 = SeedPhoto(album.Id, "IMG_0001.jpg");
        var photo2 = SeedPhoto(album.Id, "IMG_0002.jpg");
        StorageHasEverything();

        var controller = NewController();
        var result = await controller.CartManifest(code.Code, new CartManifestRequest
        {
            Items =
            {
                new() { PhotoId = photo1.Id.ToString(), Quality = "Medium" },
                new() { PhotoId = photo2.Id.ToString(), Quality = "High" },
            }
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var manifest = Assert.IsType<CartManifestResponse>(ok.Value);
        Assert.Equal("Wedding 2026", manifest.AlbumTitle);
        Assert.StartsWith("Wedding 2026-", manifest.FileNamePrefix);
        Assert.Equal(2, manifest.Items.Count);
        Assert.All(manifest.Items, item =>
        {
            Assert.False(string.IsNullOrEmpty(item.Url));
            Assert.False(string.IsNullOrEmpty(item.FileName));
        });
        Assert.Contains(manifest.Items, e => e.FileName == "Medium/IMG_0001.jpg");
        Assert.Contains(manifest.Items, e => e.FileName == "High/IMG_0002.jpg");
    }

    [Fact]
    public async Task CartManifest_DedupesIdenticalPhotoQualityPairs()
    {
        var (code, album) = SeedValidCode();
        var photo = SeedPhoto(album.Id);
        StorageHasEverything();

        var controller = NewController();
        var result = await controller.CartManifest(code.Code, new CartManifestRequest
        {
            Items =
            {
                new() { PhotoId = photo.Id.ToString(), Quality = "Medium" },
                new() { PhotoId = photo.Id.ToString(), Quality = "Medium" }, // duplicate
                new() { PhotoId = photo.Id.ToString(), Quality = "High" },   // distinct quality, kept
            }
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var manifest = Assert.IsType<CartManifestResponse>(ok.Value);
        Assert.Equal(2, manifest.Items.Count);
    }

    [Fact]
    public async Task CartManifest_RejectsExpiredAccessCode()
    {
        var album = new Album { Id = Guid.NewGuid(), Title = "Expired" };
        var expiredCode = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            Code = "EXPIRED",
            ExpirationDate = DateTime.UtcNow.AddDays(-1)
        };
        _accessCodes.Setup(r => r.GetByCodeAsync("EXPIRED")).ReturnsAsync(expiredCode);

        var controller = NewController();
        var result = await controller.CartManifest("EXPIRED", new CartManifestRequest
        {
            Items = { new() { PhotoId = Guid.NewGuid().ToString(), Quality = "Medium" } }
        });

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task CartManifest_ReturnsNotFoundForUnknownCode()
    {
        _accessCodes.Setup(r => r.GetByCodeAsync(It.IsAny<string>())).ReturnsAsync((AccessCode?)null);

        var controller = NewController();
        var result = await controller.CartManifest("NOPE", new CartManifestRequest
        {
            Items = { new() { PhotoId = Guid.NewGuid().ToString(), Quality = "Medium" } }
        });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CartManifest_ReturnsNotFoundWhenAlbumMissing()
    {
        var accessCode = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = Guid.NewGuid(),
            Code = "ORPHAN"
        };
        _accessCodes.Setup(r => r.GetByCodeAsync("ORPHAN")).ReturnsAsync(accessCode);
        _albums.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Album?)null);

        var controller = NewController();
        var result = await controller.CartManifest("ORPHAN", new CartManifestRequest
        {
            Items = { new() { PhotoId = Guid.NewGuid().ToString(), Quality = "Medium" } }
        });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CartManifest_RejectsThumbnailQuality()
    {
        var (code, album) = SeedValidCode();
        var photo = SeedPhoto(album.Id);
        StorageHasEverything();

        var controller = NewController();
        var result = await controller.CartManifest(code.Code, new CartManifestRequest
        {
            Items = { new() { PhotoId = photo.Id.ToString(), Quality = "Thumbnail" } }
        });

        // Thumbnail is filtered before validation reaches the URL builder, so the request
        // is rejected with BadRequest "no valid items".
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CartManifest_RejectsCartExceeding100Items()
    {
        var (code, _) = SeedValidCode();

        var items = Enumerable.Range(0, AccessCodeController.MaxItemsPerCart + 1)
            .Select(_ => new CartManifestItem
            {
                PhotoId = Guid.NewGuid().ToString(),
                Quality = "Medium"
            })
            .ToList();

        var controller = NewController();
        var result = await controller.CartManifest(code.Code, new CartManifestRequest { Items = items });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("100", bad.Value?.ToString());
    }

    [Fact]
    public async Task CartManifest_DropsPhotosFromOtherAlbums()
    {
        var (code, album) = SeedValidCode();
        var ourPhoto = SeedPhoto(album.Id, "ours.jpg");
        // Foreign photo lives in a different album → security check should drop it.
        var foreignAlbumId = Guid.NewGuid();
        var foreignPhoto = SeedPhoto(foreignAlbumId, "stolen.jpg");
        StorageHasEverything();

        var controller = NewController();
        var result = await controller.CartManifest(code.Code, new CartManifestRequest
        {
            Items =
            {
                new() { PhotoId = ourPhoto.Id.ToString(), Quality = "Medium" },
                new() { PhotoId = foreignPhoto.Id.ToString(), Quality = "Medium" },
            }
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var manifest = Assert.IsType<CartManifestResponse>(ok.Value);
        Assert.Single(manifest.Items);
        Assert.Equal(ourPhoto.Id.ToString(), manifest.Items[0].PhotoId);
    }

    [Fact]
    public async Task CartManifest_AcceptsOriginalQuality()
    {
        // PR-B added Original to QualityType. The manifest endpoint should accept it
        // and not coerce it to Medium / fall through to Thumbnail rejection.
        var (code, album) = SeedValidCode();
        var photo = SeedPhoto(album.Id, "RAW_0001.jpg");
        StorageHasEverything();

        var controller = NewController();
        var result = await controller.CartManifest(code.Code, new CartManifestRequest
        {
            Items = { new() { PhotoId = photo.Id.ToString(), Quality = "Original" } }
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var manifest = Assert.IsType<CartManifestResponse>(ok.Value);
        var entry = Assert.Single(manifest.Items);
        Assert.Equal("Original", entry.Quality);
        Assert.Equal("Original/RAW_0001.jpg", entry.FileName);
        // PR-B + url-service guard: Original is *never* watermarked. The presigned URL
        // therefore points to the unwatermarked original.jpg storage object.
        Assert.Contains("original.jpg", entry.Url);
        Assert.DoesNotContain("watermarked", entry.Url);
    }

    [Fact]
    public async Task CartManifest_LogsOneDownloadRowPerIssuedItem()
    {
        var (code, album) = SeedValidCode();
        var photo1 = SeedPhoto(album.Id, "a.jpg");
        var photo2 = SeedPhoto(album.Id, "b.jpg");
        StorageHasEverything();

        var captured = new List<Download>();
        _downloads.Setup(r => r.AddAsync(It.IsAny<Download>())).Callback<Download>(d => captured.Add(d));

        var controller = NewController("198.51.100.7");
        var result = await controller.CartManifest(code.Code, new CartManifestRequest
        {
            Items =
            {
                new() { PhotoId = photo1.Id.ToString(), Quality = "Medium" },
                new() { PhotoId = photo2.Id.ToString(), Quality = "High" },
            }
        });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, captured.Count);
        Assert.All(captured, d =>
        {
            Assert.Equal(code.Id, d.AccessCodeId);
            Assert.Equal(64, d.IpHash.Length); // SHA256 hex
            Assert.NotEqual("198.51.100.7", d.IpHash); // not plaintext
        });
        _downloads.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CartManifest_HandlesDuplicateFileNames_WithCollisionSuffix()
    {
        // Two distinct photos with identical filenames at the same quality should land
        // on distinct ZIP entry names (mirrors ZipDownloadService.BuildEntryName).
        var (code, album) = SeedValidCode();
        var p1 = SeedPhoto(album.Id, "vacation.jpg");
        var p2 = SeedPhoto(album.Id, "vacation.jpg");
        StorageHasEverything();

        var controller = NewController();
        var result = await controller.CartManifest(code.Code, new CartManifestRequest
        {
            Items =
            {
                new() { PhotoId = p1.Id.ToString(), Quality = "Medium" },
                new() { PhotoId = p2.Id.ToString(), Quality = "Medium" },
            }
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var manifest = Assert.IsType<CartManifestResponse>(ok.Value);
        Assert.Equal(2, manifest.Items.Count);
        Assert.Equal(2, manifest.Items.Select(i => i.FileName).Distinct().Count());
    }

    [Fact]
    public async Task CartManifest_EmptyCart_Returns400()
    {
        var (code, _) = SeedValidCode();
        var controller = NewController();
        var result = await controller.CartManifest(code.Code, new CartManifestRequest { Items = new() });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
