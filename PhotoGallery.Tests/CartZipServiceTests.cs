using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Moq;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Storage;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Unit tests for the shared <see cref="CartZipService"/> — streams a ZIP archive
/// from blob storage and logs each photo to the Download table.
///
/// Note: the service operates on pre-authorised <see cref="CartZipItem"/>s. Album
/// membership and authorisation are enforced by the calling controller, so the
/// tests here focus on streaming/skipping/logging behaviour.
/// </summary>
public class CartZipServiceTests
{
    private readonly Mock<IStorageProvider> _storage;
    private readonly Mock<IRepository<Download>> _downloadRepo;
    private readonly Mock<ILogger<CartZipService>> _logger;
    private readonly CartZipService _service;

    public CartZipServiceTests()
    {
        _storage = new Mock<IStorageProvider>();
        _downloadRepo = new Mock<IRepository<Download>>();
        _logger = new Mock<ILogger<CartZipService>>();
        _service = new CartZipService(_storage.Object, _downloadRepo.Object, _logger.Object);
    }

    private static CartZipItem Item(Guid albumId, string fileName, QualityType q, Guid? photoId = null) => new()
    {
        PhotoId = photoId ?? Guid.NewGuid(),
        AlbumId = albumId,
        FileName = fileName,
        Quality = q
    };

    private void SetupStorageHasFile(string contents = "fake-image-bytes")
    {
        _storage.Setup(s => s.ExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        _storage.Setup(s => s.DownloadAsync(It.IsAny<string>()))
            .ReturnsAsync(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contents)));
    }

    [Fact]
    public async Task StreamCartZipAsync_AddsValidPhotosToZip()
    {
        var albumId = Guid.NewGuid();
        SetupStorageHasFile();

        var items = new List<CartZipItem>
        {
            Item(albumId, "photo1.jpg", QualityType.Medium),
            Item(albumId, "photo2.jpg", QualityType.High)
        };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(items, output, Guid.NewGuid(), "192.168.1.1");

        Assert.Equal(2, added);

        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Contains(archive.Entries, e => e.FullName == "Medium/photo1.jpg");
        Assert.Contains(archive.Entries, e => e.FullName == "High/photo2.jpg");
    }

    [Fact]
    public async Task StreamCartZipAsync_LogsEachAddedPhotoToDownloadTable()
    {
        var albumId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        SetupStorageHasFile();

        var items = new List<CartZipItem>
        {
            Item(albumId, "photo.jpg", QualityType.Medium)
        };

        using var output = new MemoryStream();
        await _service.StreamCartZipAsync(items, output, codeId, "10.0.0.1");

        _downloadRepo.Verify(r => r.AddAsync(It.Is<Download>(d =>
            d.AccessCodeId == codeId &&
            d.Quality == QualityType.Medium &&
            !string.IsNullOrEmpty(d.IpHash))), Times.Once);
        _downloadRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task StreamCartZipAsync_SupportsNullAccessCodeId_ForUserCartFlow()
    {
        var albumId = Guid.NewGuid();
        SetupStorageHasFile();

        var items = new List<CartZipItem> { Item(albumId, "p.jpg", QualityType.High) };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(items, output, accessCodeId: null, remoteIp: null);

        Assert.Equal(1, added);
        _downloadRepo.Verify(r => r.AddAsync(It.Is<Download>(d => d.AccessCodeId == null)), Times.Once);
    }

    [Fact]
    public async Task StreamCartZipAsync_SkipsItemWhenStorageObjectMissing()
    {
        var albumId = Guid.NewGuid();
        _storage.Setup(s => s.ExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var items = new List<CartZipItem> { Item(albumId, "p.jpg", QualityType.Medium) };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(items, output, Guid.NewGuid(), null);

        Assert.Equal(0, added);
        _downloadRepo.Verify(r => r.AddAsync(It.IsAny<Download>()), Times.Never);
        _downloadRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task StreamCartZipAsync_DeduplicatesCollidingFilenames()
    {
        var albumId = Guid.NewGuid();
        SetupStorageHasFile();

        // Same filename at the same quality → ZIP should auto-suffix the second entry.
        var items = new List<CartZipItem>
        {
            Item(albumId, "photo.jpg", QualityType.Medium),
            Item(albumId, "photo.jpg", QualityType.Medium)
        };

        using var output = new MemoryStream();
        await _service.StreamCartZipAsync(items, output, Guid.NewGuid(), null);

        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Contains(archive.Entries, e => e.FullName == "Medium/photo.jpg");
        Assert.Contains(archive.Entries, e => e.FullName == "Medium/photo_1.jpg");
    }

    [Fact]
    public async Task StreamCartZipAsync_ThrowsWhenExceedsMaxItems()
    {
        var albumId = Guid.NewGuid();
        var items = Enumerable.Range(0, CartZipService.MaxItemsPerCartConst + 1)
            .Select(i => Item(albumId, $"p{i}.jpg", QualityType.Medium))
            .ToList();

        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StreamCartZipAsync(items, output, null, null));
    }

    [Fact]
    public async Task StreamCartZipAsync_HashesIpAddress()
    {
        var albumId = Guid.NewGuid();
        SetupStorageHasFile();

        var items = new List<CartZipItem> { Item(albumId, "p.jpg", QualityType.Medium) };

        Download? captured = null;
        _downloadRepo.Setup(r => r.AddAsync(It.IsAny<Download>()))
            .Callback<Download>(d => captured = d)
            .Returns(Task.CompletedTask);

        using var output = new MemoryStream();
        await _service.StreamCartZipAsync(items, output, null, "192.168.1.100");

        Assert.NotNull(captured);
        Assert.NotEqual("192.168.1.100", captured!.IpHash); // hashed, not stored raw
        Assert.Equal(64, captured.IpHash.Length); // SHA256 hex
    }
}
