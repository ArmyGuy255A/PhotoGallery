using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Controllers;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Storage;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Phase 6 — paginated photo-listing contract.
///
/// Validates the response envelope (<c>items</c>, <c>page</c>, <c>pageSize</c>,
/// <c>totalCount</c>, <c>hasMore</c>), the FileName-ASC sort that keeps page
/// boundaries stable, and the no-params default (page=1, size=20) which replaces
/// the old "return everything" behaviour.
/// </summary>
public class AlbumsControllerPaginationTests
{
    private const string Owner = "owner-user";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static PhotoVersionUrlService NewUrlService()
    {
        var storage = new Mock<IStorageProvider>();
        storage.Setup(s => s.ExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        var urlRepo = new Mock<IPhotoVersionUrlRepository>();
        var photoRepo = new Mock<IPhotoRepository>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        return new PhotoVersionUrlService(
            storage.Object, urlRepo.Object, photoRepo.Object, config,
            NullLogger<PhotoVersionUrlService>.Instance);
    }

    private class TestPhotoRepository : Repository<Photo>, IPhotoRepository
    {
        public TestPhotoRepository(ApplicationDbContext ctx) : base(ctx) { }
        public Task<List<Photo>> GetAlbumPhotosAsync(Guid albumId) =>
            _context.Photos.Where(p => p.AlbumId == albumId).ToListAsync();
        public Task<Photo?> GetWithVersionsAsync(Guid photoId) =>
            _context.Photos.Include(p => p.PhotoVersions).FirstOrDefaultAsync(p => p.Id == photoId);
        public Task<List<Photo>> GetUnprocessedPhotosAsync() =>
            _context.Photos.Where(p => !p.ProcessingComplete).ToListAsync();
        public async Task<HashSet<string>> GetExistingFileNamesAsync(Guid albumId)
        {
            var names = await _context.Photos.Where(p => p.AlbumId == albumId)
                .Select(p => p.FileName).ToListAsync();
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }
        public async Task<Dictionary<string, ExistingPhotoSummary>> GetExistingPhotoSummariesByNameAsync(Guid albumId)
        {
            var rows = await _context.Photos.Where(p => p.AlbumId == albumId)
                .Select(p => new { p.FileName, p.Id, p.ProcessingStatus }).ToListAsync();
            var map = new Dictionary<string, ExistingPhotoSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows) map[r.FileName] = new ExistingPhotoSummary(r.Id, r.ProcessingStatus);
            return map;
        }
    }

    private class TestRepository<T> : Repository<T> where T : class
    {
        public TestRepository(ApplicationDbContext ctx) : base(ctx) { }
    }

    private static AlbumsController NewController(ApplicationDbContext db, string userId)
    {
        var photoRepo = new TestPhotoRepository(db);

        // Stub IAlbumRepository.GetByIdAsync to return the album from the in-memory DB.
        var albumRepo = new Mock<IAlbumRepository>();
        albumRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => db.Albums.FirstOrDefault(a => a.Id == id));

        var codeRepo = new Mock<IAccessCodeRepository>();

        var displayNames = new Mock<IUserDisplayNameResolver>();
        displayNames.Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserDisplayNameResolver.DefaultDisplayName);
        displayNames.Setup(r => r.ResolveManyAsync(It.IsAny<IEnumerable<string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string?> ids, CancellationToken _) =>
                (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());

        var controller = new AlbumsController(
            albumRepo.Object, photoRepo, codeRepo.Object, NewUrlService(),
            displayNames.Object,
            NullLogger<AlbumsController>.Instance);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims, "Test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private static (Album album, List<Photo> photos) Seed(ApplicationDbContext db, int count)
    {
        var user = new User { Id = Owner, UserName = "o@example.com", Email = "o@example.com" };
        var album = new Album { Id = Guid.NewGuid(), Title = "A", OwnerId = Owner };
        db.Users.Add(user);
        db.Albums.Add(album);

        var photos = new List<Photo>();
        // Insert in shuffled order so the FileName ASC contract is observable.
        var rng = new Random(7);
        var order = Enumerable.Range(0, count).OrderBy(_ => rng.Next()).ToList();
        foreach (var i in order)
        {
            var p = new Photo
            {
                Id = Guid.NewGuid(),
                AlbumId = album.Id,
                FileName = $"DSC_{8000 + i:D4}.JPG",
                UploadDate = DateTime.UtcNow.AddSeconds(-i)
            };
            photos.Add(p);
            db.Photos.Add(p);
        }
        db.SaveChanges();
        return (album, photos);
    }

    [Fact]
    public async Task GetAlbumPhotos_OmittedParams_ReturnsFirstPageOf20_WithHasMoreTrue()
    {
        using var db = NewContext();
        var (album, _) = Seed(db, count: 25);
        var controller = NewController(db, Owner);

        var result = await controller.GetAlbumPhotos(album.Id.ToString());

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PaginatedPhotosResponse>(ok.Value);
        Assert.Equal(1, body.Page);
        Assert.Equal(20, body.PageSize);
        Assert.Equal(25, body.TotalCount);
        Assert.Equal(20, body.Items.Count);
        Assert.True(body.HasMore);
    }

    [Fact]
    public async Task GetAlbumPhotos_OrdersByFileNameAscending_AcrossPages()
    {
        using var db = NewContext();
        var (album, _) = Seed(db, count: 25);
        var controller = NewController(db, Owner);

        var page1 = (PaginatedPhotosResponse)((OkObjectResult)
            (await controller.GetAlbumPhotos(album.Id.ToString(), page: 1, pageSize: 10)).Result!).Value!;
        var page2 = (PaginatedPhotosResponse)((OkObjectResult)
            (await controller.GetAlbumPhotos(album.Id.ToString(), page: 2, pageSize: 10)).Result!).Value!;
        var page3 = (PaginatedPhotosResponse)((OkObjectResult)
            (await controller.GetAlbumPhotos(album.Id.ToString(), page: 3, pageSize: 10)).Result!).Value!;

        Assert.Equal("DSC_8000.JPG", page1.Items.First().FileName);
        Assert.Equal("DSC_8009.JPG", page1.Items.Last().FileName);
        Assert.Equal("DSC_8010.JPG", page2.Items.First().FileName);
        Assert.Equal("DSC_8019.JPG", page2.Items.Last().FileName);
        Assert.Equal("DSC_8020.JPG", page3.Items.First().FileName);
        Assert.Equal(5, page3.Items.Count);
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task GetAlbumPhotos_PageSizeCappedAt100()
    {
        using var db = NewContext();
        var (album, _) = Seed(db, count: 150);
        var controller = NewController(db, Owner);

        var result = await controller.GetAlbumPhotos(album.Id.ToString(), page: 1, pageSize: 500);

        var body = (PaginatedPhotosResponse)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(100, body.PageSize);
        Assert.Equal(100, body.Items.Count);
        Assert.True(body.HasMore);
    }

    [Fact]
    public async Task GetAlbumPhotos_LastPage_HasMoreIsFalse()
    {
        using var db = NewContext();
        var (album, _) = Seed(db, count: 25);
        var controller = NewController(db, Owner);

        var result = await controller.GetAlbumPhotos(album.Id.ToString(), page: 2, pageSize: 20);

        var body = (PaginatedPhotosResponse)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(5, body.Items.Count);
        Assert.False(body.HasMore);
    }
}
