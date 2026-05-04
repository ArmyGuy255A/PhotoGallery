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
/// Unit tests for ZipDownloadService — streams a ZIP archive from blob storage
/// and logs each photo to the Download table.
/// </summary>
public class ZipDownloadServiceTests
{
    private readonly Mock<IStorageProvider> _storage;
    private readonly Mock<IRepository<Download>> _downloadRepo;
    private readonly Mock<IRepository<Photo>> _photoRepo;
    private readonly Mock<ILogger<ZipDownloadService>> _logger;
    private readonly ZipDownloadService _service;

    public ZipDownloadServiceTests()
    {
        _storage = new Mock<IStorageProvider>();
        _downloadRepo = new Mock<IRepository<Download>>();
        _photoRepo = new Mock<IRepository<Photo>>();
        _logger = new Mock<ILogger<ZipDownloadService>>();
        _service = new ZipDownloadService(_storage.Object, _downloadRepo.Object, _photoRepo.Object, _logger.Object);
    }

    private Photo MakePhoto(Guid id, Guid albumId, string fileName)
        => new Photo { Id = id, AlbumId = albumId, FileName = fileName };

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
        var photo1 = MakePhoto(Guid.NewGuid(), albumId, "photo1.jpg");
        var photo2 = MakePhoto(Guid.NewGuid(), albumId, "photo2.jpg");

        _photoRepo.Setup(r => r.GetByIdAsync(photo1.Id)).ReturnsAsync(photo1);
        _photoRepo.Setup(r => r.GetByIdAsync(photo2.Id)).ReturnsAsync(photo2);
        SetupStorageHasFile();

        var items = new List<CartItem>
        {
            new() { PhotoId = photo1.Id, Quality = QualityType.Medium },
            new() { PhotoId = photo2.Id, Quality = QualityType.High }
        };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, "192.168.1.1");

        Assert.Equal(2, added);

        // Verify ZIP is readable and contains both files
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
        var photo = MakePhoto(Guid.NewGuid(), albumId, "photo.jpg");
        var codeId = Guid.NewGuid();

        _photoRepo.Setup(r => r.GetByIdAsync(photo.Id)).ReturnsAsync(photo);
        SetupStorageHasFile();

        var items = new List<CartItem> { new() { PhotoId = photo.Id, Quality = QualityType.Medium } };

        using var output = new MemoryStream();
        await _service.StreamCartZipAsync(albumId, codeId, items, output, "10.0.0.1");

        _downloadRepo.Verify(r => r.AddAsync(It.Is<Download>(d =>
            d.PhotoId == photo.Id &&
            d.AccessCodeId == codeId &&
            d.Quality == QualityType.Medium &&
            d.IpHash.Length == 64)), // SHA256 hex = 64 chars
            Times.Once);
        _downloadRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task StreamCartZipAsync_HashesIp_NotPlainText()
    {
        var albumId = Guid.NewGuid();
        var photo = MakePhoto(Guid.NewGuid(), albumId, "photo.jpg");
        _photoRepo.Setup(r => r.GetByIdAsync(photo.Id)).ReturnsAsync(photo);
        SetupStorageHasFile();

        var items = new List<CartItem> { new() { PhotoId = photo.Id, Quality = QualityType.Medium } };

        Download? captured = null;
        _downloadRepo.Setup(r => r.AddAsync(It.IsAny<Download>())).Callback<Download>(d => captured = d);

        using var output = new MemoryStream();
        await _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, "203.0.113.5");

        Assert.NotNull(captured);
        Assert.NotEqual("203.0.113.5", captured.IpHash); // not plain text
        Assert.Equal(64, captured.IpHash.Length); // SHA256 hex length
        Assert.Matches("^[a-f0-9]{64}$", captured.IpHash);
    }

    [Fact]
    public async Task StreamCartZipAsync_SkipsPhotoFromDifferentAlbum()
    {
        var albumId = Guid.NewGuid();
        var otherAlbumId = Guid.NewGuid();
        var photoInOtherAlbum = MakePhoto(Guid.NewGuid(), otherAlbumId, "stolen.jpg");

        _photoRepo.Setup(r => r.GetByIdAsync(photoInOtherAlbum.Id)).ReturnsAsync(photoInOtherAlbum);
        SetupStorageHasFile();

        var items = new List<CartItem> { new() { PhotoId = photoInOtherAlbum.Id, Quality = QualityType.Medium } };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, "1.1.1.1");

        Assert.Equal(0, added);
        _downloadRepo.Verify(r => r.AddAsync(It.IsAny<Download>()), Times.Never);
    }

    [Fact]
    public async Task StreamCartZipAsync_SkipsMissingPhoto()
    {
        var albumId = Guid.NewGuid();
        var missingId = Guid.NewGuid();

        _photoRepo.Setup(r => r.GetByIdAsync(missingId)).ReturnsAsync((Photo?)null);

        var items = new List<CartItem> { new() { PhotoId = missingId, Quality = QualityType.Medium } };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, "1.1.1.1");

        Assert.Equal(0, added);
        _downloadRepo.Verify(r => r.AddAsync(It.IsAny<Download>()), Times.Never);
    }

    [Fact]
    public async Task StreamCartZipAsync_SkipsItemsMissingFromStorage()
    {
        var albumId = Guid.NewGuid();
        var photo = MakePhoto(Guid.NewGuid(), albumId, "photo.jpg");
        _photoRepo.Setup(r => r.GetByIdAsync(photo.Id)).ReturnsAsync(photo);
        _storage.Setup(s => s.ExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var items = new List<CartItem> { new() { PhotoId = photo.Id, Quality = QualityType.Medium } };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, "1.1.1.1");

        Assert.Equal(0, added);
    }

    [Fact]
    public async Task StreamCartZipAsync_ContinuesAfterPartialFailure()
    {
        var albumId = Guid.NewGuid();
        var goodPhoto = MakePhoto(Guid.NewGuid(), albumId, "good.jpg");
        var brokenPhoto = MakePhoto(Guid.NewGuid(), albumId, "broken.jpg");

        _photoRepo.Setup(r => r.GetByIdAsync(goodPhoto.Id)).ReturnsAsync(goodPhoto);
        _photoRepo.Setup(r => r.GetByIdAsync(brokenPhoto.Id)).ReturnsAsync(brokenPhoto);

        // Both exist, but one throws on download
        _storage.Setup(s => s.ExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        _storage.Setup(s => s.DownloadAsync(It.Is<string>(k => k.Contains(goodPhoto.Id.ToString()))))
            .ReturnsAsync(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes("good-bytes")));
        _storage.Setup(s => s.DownloadAsync(It.Is<string>(k => k.Contains(brokenPhoto.Id.ToString()))))
            .ThrowsAsync(new IOException("Storage error"));

        var items = new List<CartItem>
        {
            new() { PhotoId = goodPhoto.Id, Quality = QualityType.Medium },
            new() { PhotoId = brokenPhoto.Id, Quality = QualityType.Medium }
        };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, "1.1.1.1");

        Assert.Equal(1, added);
    }

    [Fact]
    public async Task StreamCartZipAsync_RejectsCartExceedingMaxItems()
    {
        var albumId = Guid.NewGuid();
        var items = Enumerable.Range(0, ZipDownloadService.MaxItemsPerCart + 1)
            .Select(_ => new CartItem { PhotoId = Guid.NewGuid(), Quality = QualityType.Medium })
            .ToList();

        using var output = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, "1.1.1.1"));
    }

    [Fact]
    public async Task StreamCartZipAsync_HandlesDuplicateFileNames()
    {
        var albumId = Guid.NewGuid();
        var photo1 = MakePhoto(Guid.NewGuid(), albumId, "vacation.jpg");
        var photo2 = MakePhoto(Guid.NewGuid(), albumId, "vacation.jpg"); // SAME name, different photo

        _photoRepo.Setup(r => r.GetByIdAsync(photo1.Id)).ReturnsAsync(photo1);
        _photoRepo.Setup(r => r.GetByIdAsync(photo2.Id)).ReturnsAsync(photo2);
        SetupStorageHasFile();

        var items = new List<CartItem>
        {
            new() { PhotoId = photo1.Id, Quality = QualityType.Medium },
            new() { PhotoId = photo2.Id, Quality = QualityType.Medium }
        };

        using var output = new MemoryStream();
        var added = await _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, "1.1.1.1");

        Assert.Equal(2, added);

        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        // Names should be unique
        Assert.Equal(2, archive.Entries.Select(e => e.FullName).Distinct().Count());
    }

    [Fact]
    public async Task StreamCartZipAsync_EmptyIp_HashesEmpty()
    {
        var albumId = Guid.NewGuid();
        var photo = MakePhoto(Guid.NewGuid(), albumId, "photo.jpg");
        _photoRepo.Setup(r => r.GetByIdAsync(photo.Id)).ReturnsAsync(photo);
        SetupStorageHasFile();

        Download? captured = null;
        _downloadRepo.Setup(r => r.AddAsync(It.IsAny<Download>())).Callback<Download>(d => captured = d);

        var items = new List<CartItem> { new() { PhotoId = photo.Id, Quality = QualityType.Medium } };

        using var output = new MemoryStream();
        await _service.StreamCartZipAsync(albumId, Guid.NewGuid(), items, output, null);

        Assert.NotNull(captured);
        Assert.Equal(string.Empty, captured.IpHash); // null IP → empty hash
    }
}
