using Xunit;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace PhotoGallery.Tests;

public class PhotoVersionUrlRepositoryTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private (Album album, Photo photo) SetupPhotoWithAlbum(ApplicationDbContext context)
    {
        var user = new User { Id = Guid.NewGuid().ToString(), UserName = "testuser@example.com", Email = "testuser@example.com" };
        var album = new Album { Id = Guid.NewGuid(), Title = "Test Album", OwnerId = user.Id };
        var photo = new Photo { Id = Guid.NewGuid(), AlbumId = album.Id, FileName = "test.jpg", UploadDate = DateTime.UtcNow };

        context.Users.Add(user);
        context.Albums.Add(album);
        context.Photos.Add(photo);
        context.SaveChanges();

        return (album, photo);
    }

    [Fact]
    public async Task GetByPhotoAndQualityAsync_Should_Return_Active_Url()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);

        var now = DateTime.UtcNow;
        var url = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/presigned-url-1",
            ExpiresAt = now.AddDays(7),
            GeneratedAt = now,
            IsActive = true
        };

        context.PhotoVersionUrls.Add(url);
        await context.SaveChangesAsync();

        var repository = new PhotoVersionUrlRepository(context);

        // Act
        var result = await repository.GetByPhotoAndQualityAsync(photo.Id, QualityType.Thumbnail);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(url.Id, result.Id);
        Assert.Equal(QualityType.Thumbnail, result.Quality);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task GetByPhotoAndQualityAsync_Should_Not_Return_Inactive_Url()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);

        var now = DateTime.UtcNow;
        var url = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/presigned-url-1",
            ExpiresAt = now.AddDays(7),
            GeneratedAt = now,
            IsActive = false  // Inactive
        };

        context.PhotoVersionUrls.Add(url);
        await context.SaveChangesAsync();

        var repository = new PhotoVersionUrlRepository(context);

        // Act
        var result = await repository.GetByPhotoAndQualityAsync(photo.Id, QualityType.Thumbnail);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByPhotoIdAsync_Should_Return_All_Active_Urls_For_Photo()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);

        var now = DateTime.UtcNow;
        var urls = new[]
        {
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Thumbnail, PresignedUrl = "url1", ExpiresAt = now.AddDays(7), GeneratedAt = now, IsActive = true },
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Medium, PresignedUrl = "url2", ExpiresAt = now.AddDays(7), GeneratedAt = now, IsActive = true },
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Low, PresignedUrl = "url3", ExpiresAt = now.AddDays(7), GeneratedAt = now, IsActive = false }
        };

        context.PhotoVersionUrls.AddRange(urls);
        await context.SaveChangesAsync();

        var repository = new PhotoVersionUrlRepository(context);

        // Act
        var result = await repository.GetByPhotoIdAsync(photo.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);  // Only active ones
        Assert.All(result, url => Assert.True(url.IsActive));
    }

    [Fact]
    public async Task GetExpiringAsync_Should_Return_Urls_Expiring_Before_Date()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);

        var now = DateTime.UtcNow;
        var cutoffDate = now.AddDays(5);

        var urls = new[]
        {
            // Expiring soon (before cutoff)
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Thumbnail, PresignedUrl = "url1", ExpiresAt = cutoffDate.AddHours(-1), GeneratedAt = now, IsActive = true },
            // Not expiring yet (after cutoff)
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Medium, PresignedUrl = "url2", ExpiresAt = cutoffDate.AddHours(1), GeneratedAt = now, IsActive = true },
            // Inactive (should not be returned)
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Low, PresignedUrl = "url3", ExpiresAt = cutoffDate.AddHours(-1), GeneratedAt = now, IsActive = false }
        };

        context.PhotoVersionUrls.AddRange(urls);
        await context.SaveChangesAsync();

        var repository = new PhotoVersionUrlRepository(context);

        // Act
        var result = await repository.GetExpiringAsync(cutoffDate);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(QualityType.Thumbnail, result.First().Quality);
        Assert.True(result.First().IsActive);
    }

    [Fact]
    public async Task GetExpiredAsync_Should_Return_Expired_Urls()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);

        var now = DateTime.UtcNow;
        var urls = new[]
        {
            // Expired
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Thumbnail, PresignedUrl = "url1", ExpiresAt = now.AddDays(-1), GeneratedAt = now.AddDays(-7), IsActive = true },
            // Still valid
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Medium, PresignedUrl = "url2", ExpiresAt = now.AddDays(1), GeneratedAt = now, IsActive = true }
        };

        context.PhotoVersionUrls.AddRange(urls);
        await context.SaveChangesAsync();

        var repository = new PhotoVersionUrlRepository(context);

        // Act
        var result = await repository.GetExpiredAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(QualityType.Thumbnail, result.First().Quality);
    }

    [Fact]
    public async Task InvalidateByPhotoIdAsync_Should_Mark_All_Photo_Urls_Inactive()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);

        var now = DateTime.UtcNow;
        var urls = new[]
        {
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Thumbnail, PresignedUrl = "url1", ExpiresAt = now.AddDays(7), GeneratedAt = now, IsActive = true },
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Medium, PresignedUrl = "url2", ExpiresAt = now.AddDays(7), GeneratedAt = now, IsActive = true }
        };

        context.PhotoVersionUrls.AddRange(urls);
        await context.SaveChangesAsync();

        var repository = new PhotoVersionUrlRepository(context);

        // Act
        await repository.InvalidateByPhotoIdAsync(photo.Id);

        // Assert
        var allUrls = await repository.GetByPhotoIdAsync(photo.Id);
        Assert.Empty(allUrls);  // All should be inactive now

        // Verify in context
        var directCheck = await context.PhotoVersionUrls
            .Where(pvu => pvu.PhotoId == photo.Id)
            .ToListAsync();
        Assert.Equal(2, directCheck.Count);
        Assert.All(directCheck, url => Assert.False(url.IsActive));
    }

    [Fact]
    public async Task InvalidateByAlbumIdAsync_Should_Mark_All_Album_Urls_Inactive()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);
        var anotherPhoto = new Photo { Id = Guid.NewGuid(), AlbumId = album.Id, FileName = "test2.jpg", UploadDate = DateTime.UtcNow };
        context.Photos.Add(anotherPhoto);
        await context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var urls = new[]
        {
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = photo.Id, Quality = QualityType.Thumbnail, PresignedUrl = "url1", ExpiresAt = now.AddDays(7), GeneratedAt = now, IsActive = true },
            new PhotoVersionUrl { Id = Guid.NewGuid(), PhotoId = anotherPhoto.Id, Quality = QualityType.Medium, PresignedUrl = "url2", ExpiresAt = now.AddDays(7), GeneratedAt = now, IsActive = true }
        };

        context.PhotoVersionUrls.AddRange(urls);
        await context.SaveChangesAsync();

        var repository = new PhotoVersionUrlRepository(context);

        // Act
        await repository.InvalidateByAlbumIdAsync(album.Id);

        // Assert
        var allUrls = await context.PhotoVersionUrls.ToListAsync();
        Assert.Equal(2, allUrls.Count);
        Assert.All(allUrls, url => Assert.False(url.IsActive));
    }

    [Fact]
    public async Task AddAsync_Should_Create_New_Url()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);

        var now = DateTime.UtcNow;
        var url = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/presigned-url",
            ExpiresAt = now.AddDays(7),
            GeneratedAt = now,
            IsActive = true
        };

        var repository = new PhotoVersionUrlRepository(context);

        // Act
        await repository.AddAsync(url);
        await repository.SaveChangesAsync();

        // Assert
        var stored = await repository.GetByPhotoAndQualityAsync(photo.Id, QualityType.Thumbnail);
        Assert.NotNull(stored);
        Assert.Equal(url.Id, stored.Id);
    }

    [Fact]
    public async Task UpdateAsync_Should_Modify_Existing_Url()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var (album, photo) = SetupPhotoWithAlbum(context);

        var now = DateTime.UtcNow;
        var url = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/old-url",
            ExpiresAt = now.AddDays(7),
            GeneratedAt = now,
            IsActive = true
        };

        context.PhotoVersionUrls.Add(url);
        await context.SaveChangesAsync();

        var repository = new PhotoVersionUrlRepository(context);
        url.PresignedUrl = "http://minio/new-url";
        url.ExpiresAt = now.AddDays(8);

        // Act
        await repository.UpdateAsync(url);
        await repository.SaveChangesAsync();

        // Assert
        var updated = await repository.GetByPhotoAndQualityAsync(photo.Id, QualityType.Thumbnail);
        Assert.NotNull(updated);
        Assert.Equal("http://minio/new-url", updated.PresignedUrl);
        Assert.Equal(now.AddDays(8).Date, updated.ExpiresAt.Date);  // Compare dates due to precision
    }
}
