using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Controllers;
using PhotoGallery.Data;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Storage;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Verifies <c>AlbumsController</c> populates <c>CreatedByDisplayName</c> via
/// <see cref="IUserDisplayNameResolver"/> so the FE album header reads
/// "by Phillip Dieppa" instead of "by 08a0e965-…".
///
/// Three resolution paths are exercised end-to-end through the controller:
/// 1. user has first + last name → "First Last"
/// 2. user has email only → email-local-part
/// 3. user is deleted / unknown → "Photo Gallery"
/// </summary>
public class AlbumsControllerDisplayNameTests
{
    private const string OwnerId = "owner-1";

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
        var urlRepo = new Mock<IPhotoVersionUrlRepository>();
        var photoRepo = new Mock<IPhotoRepository>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        return new PhotoVersionUrlService(
            storage.Object, urlRepo.Object, photoRepo.Object, config,
            NullLogger<PhotoVersionUrlService>.Instance);
    }

    /// <summary>
    /// Test double for <see cref="IUserDisplayNameResolver"/> that walks a
    /// supplied user table through the same fallback chain as the production
    /// implementation, without dragging UserManager + a full Identity store
    /// into the test fixture.
    /// </summary>
    private sealed class FakeResolver : IUserDisplayNameResolver
    {
        private readonly Dictionary<string, User> _users;

        public FakeResolver(IEnumerable<User> users)
        {
            _users = users.ToDictionary(u => u.Id, StringComparer.Ordinal);
        }

        public Task<string> ResolveAsync(string? userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || !_users.TryGetValue(userId, out var u))
            {
                return Task.FromResult(UserDisplayNameResolver.DefaultDisplayName);
            }
            return Task.FromResult(Strip(WatermarkService.FormatDisplayName(u)));
        }

        public Task<IReadOnlyDictionary<string, string>> ResolveManyAsync(
            IEnumerable<string?> ids, CancellationToken ct = default)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                map[id!] = _users.TryGetValue(id!, out var u)
                    ? Strip(WatermarkService.FormatDisplayName(u))
                    : UserDisplayNameResolver.DefaultDisplayName;
            }
            return Task.FromResult<IReadOnlyDictionary<string, string>>(map);
        }

        private static string Strip(string s) =>
            s.StartsWith("© ", StringComparison.Ordinal) ? s[2..] : s;
    }

    private static AlbumsController NewController(
        ApplicationDbContext db, string userId, IUserDisplayNameResolver resolver, bool admin = false)
    {
        var albumRepo = new Mock<IAlbumRepository>();
        albumRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(() => db.Albums.ToList());
        albumRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => db.Albums.FirstOrDefault(a => a.Id == id));

        var controller = new AlbumsController(
            albumRepo.Object,
            new Mock<IPhotoRepository>().Object,
            new Mock<IAccessCodeRepository>().Object,
            NewUrlService(),
            resolver,
            NullLogger<AlbumsController>.Instance);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var identity = new ClaimsIdentity(claims, "Test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private static Album SeedAlbum(ApplicationDbContext db, string createdBy)
    {
        var album = new Album
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            OwnerId = OwnerId,
            CreatedBy = createdBy,
            CreatedDate = DateTime.UtcNow
        };
        db.Albums.Add(album);
        db.SaveChanges();
        return album;
    }

    [Fact]
    public async Task GetAlbums_FirstAndLastNameUser_ReturnsFullName()
    {
        using var db = NewContext();
        SeedAlbum(db, createdBy: OwnerId);
        var resolver = new FakeResolver(new[]
        {
            new User { Id = OwnerId, FirstName = "Phillip", LastName = "Dieppa", Email = "phil@example.com" }
        });

        var controller = NewController(db, OwnerId, resolver);
        var result = await controller.GetAlbums();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<AlbumListDto>>(ok.Value);

        Assert.Single(list);
        Assert.Equal("Phillip Dieppa", list[0].CreatedByDisplayName);
        Assert.Equal(OwnerId, list[0].CreatedBy);
    }

    [Fact]
    public async Task GetAlbums_EmailOnlyUser_ReturnsEmailLocalPart()
    {
        using var db = NewContext();
        SeedAlbum(db, createdBy: OwnerId);
        var resolver = new FakeResolver(new[]
        {
            new User { Id = OwnerId, Email = "phil@example.com" }
        });

        var controller = NewController(db, OwnerId, resolver);
        var result = await controller.GetAlbums();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<AlbumListDto>>(ok.Value);

        Assert.Equal("phil", list[0].CreatedByDisplayName);
    }

    [Fact]
    public async Task GetAlbums_DeletedUser_ReturnsPhotoGalleryFallback()
    {
        using var db = NewContext();
        SeedAlbum(db, createdBy: "ghost-user-id");
        // Empty resolver -> id not found -> default fallback.
        var resolver = new FakeResolver(Array.Empty<User>());

        var controller = NewController(db, OwnerId, resolver, admin: true);
        var result = await controller.GetAlbums();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<AlbumListDto>>(ok.Value);

        Assert.Equal("Photo Gallery", list[0].CreatedByDisplayName);
    }

    [Fact]
    public async Task GetAlbumById_PopulatesCreatedByDisplayName()
    {
        using var db = NewContext();
        var album = SeedAlbum(db, createdBy: OwnerId);
        var resolver = new FakeResolver(new[]
        {
            new User { Id = OwnerId, FirstName = "Phillip", LastName = "Dieppa" }
        });

        var controller = NewController(db, OwnerId, resolver);
        var result = await controller.GetAlbumById(album.Id.ToString());
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AlbumDetailDto>(ok.Value);

        Assert.Equal("Phillip Dieppa", dto.CreatedByDisplayName);
    }
}
