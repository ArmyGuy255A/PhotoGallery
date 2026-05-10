using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Controllers;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Storage;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// EPIC May 2026 / Bug #9 — controller-level tests for /api/cart.
/// Validates auth, quality validation, owner / saved-code / admin authz,
/// 100-item cap response shape, multi-album ZIP path with download-time
/// re-authorisation + the X-Skipped-Photo-Ids header, and the all-unauthorised
/// → 403 path.
/// </summary>
public class CartControllerTests
{
    private const string Owner = "owner-user";
    private const string Stranger = "stranger-user";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static PhotoVersionUrlService NewUrlService()
    {
        // The controller calls GenerateShortLivedUrlAsync — we don't care about its
        // output here; storage exists=false means it returns null and the controller
        // continues. That's exactly the behaviour we want under test.
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

    private static CartController NewController(
        ApplicationDbContext db,
        string userId,
        bool isAdmin = false,
        Mock<ICartZipService>? cartZipMock = null)
    {
        // Use the real repositories against the in-memory DB so authz checks
        // (which read SavedAccessCodes + Albums) work end-to-end.
        var cartRepo = new UserCartRepository(db);
        var photoRepo = new TestPhotoRepository(db);
        var albumRepo = new TestRepository<Album>(db);
        var cartZip = cartZipMock?.Object ?? Mock.Of<ICartZipService>();

        var controller = new CartController(
            cartRepo, photoRepo, albumRepo, db, NewUrlService(), cartZip,
            NullLogger<CartController>.Instance);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var identity = new ClaimsIdentity(claims, "Test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
                Response = { Body = new MemoryStream() }
            }
        };
        return controller;
    }

    /// <summary>Lightweight IPhotoRepository implementation backed by the in-memory DbSet.</summary>
    private class TestPhotoRepository : Repository<Photo>, IPhotoRepository
    {
        public TestPhotoRepository(ApplicationDbContext ctx) : base(ctx) { }
        public Task<List<Photo>> GetAlbumPhotosAsync(Guid albumId) =>
            _context.Photos.Where(p => p.AlbumId == albumId).ToListAsync();
        public Task<Photo?> GetWithVersionsAsync(Guid photoId) =>
            _context.Photos.Include(p => p.PhotoVersions).FirstOrDefaultAsync(p => p.Id == photoId);
        public Task<List<Photo>> GetUnprocessedPhotosAsync() =>
            _context.Photos.Where(p => !p.ProcessingComplete).ToListAsync();
    }

    private class TestRepository<T> : Repository<T> where T : class
    {
        public TestRepository(ApplicationDbContext ctx) : base(ctx) { }
    }

    private static (User user, Album album, Photo photo) Seed(
        ApplicationDbContext db,
        string ownerId,
        string suffix = "")
    {
        var user = new User
        {
            Id = ownerId,
            UserName = $"u{suffix}@example.com",
            Email = $"u{suffix}@example.com"
        };
        var album = new Album { Id = Guid.NewGuid(), Title = "Album" + suffix, OwnerId = ownerId };
        var photo = new Photo
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            FileName = $"p{suffix}.jpg",
            UploadDate = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Albums.Add(album);
        db.Photos.Add(photo);
        db.SaveChanges();
        return (user, album, photo);
    }

    // ───────── auth / quality validation ─────────

    [Fact]
    public async Task AddToCart_ReturnsUnauthorized_WhenNoUserClaim()
    {
        using var db = NewContext();
        var controller = NewController(db, userId: "");
        // Strip claims.
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = Guid.NewGuid().ToString(),
            Quality = "Medium"
        });
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task AddToCart_RejectsThumbnailQuality_With400()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        var controller = NewController(db, Owner);

        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = photo.Id.ToString(),
            Quality = "Thumbnail",
            SourceAlbumId = album.Id.ToString()
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddToCart_RejectsUnknownQuality_With400()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        var controller = NewController(db, Owner);

        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = photo.Id.ToString(),
            Quality = "Garbage",
            SourceAlbumId = album.Id.ToString()
        });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ───────── authorisation paths ─────────

    [Fact]
    public async Task AddToCart_AllowsOwner()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        var controller = NewController(db, Owner);

        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = photo.Id.ToString(),
            Quality = "Medium",
            SourceAlbumId = album.Id.ToString()
        });
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CartItemDto>(ok.Value);
        // Issue #110: AddToCart must populate SourceAlbumTitle on the response
        // so the FE drawer can group new items under their album immediately
        // (instead of falling into the "Other" bucket until the next GET).
        Assert.Equal(album.Title, dto.SourceAlbumTitle);
        Assert.Equal(album.Id.ToString(), dto.SourceAlbumId);
        Assert.Single(db.UserCartItems);
    }

    [Fact]
    public async Task AddToCart_AllowsAdmin_OnSomeoneElsesAlbum()
    {
        using var db = NewContext();
        var (_, _, photo) = Seed(db, Owner);
        var controller = NewController(db, "admin-user", isAdmin: true);

        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = photo.Id.ToString(),
            Quality = "High"
        });
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddToCart_AllowsUserWithSavedNonExpiredAccessCode()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        var stranger = new User { Id = Stranger, UserName = "s@e.com", Email = "s@e.com" };
        var code = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            Code = "ABCD1234",
            CreatedDate = DateTime.UtcNow,
            CreatedBy = Owner,
            ExpirationDate = DateTime.UtcNow.AddDays(7)
        };
        var saved = new SavedAccessCode
        {
            Id = Guid.NewGuid(),
            UserId = Stranger,
            AccessCodeId = code.Id,
            SavedAt = DateTime.UtcNow
        };
        db.Users.Add(stranger);
        db.AccessCodes.Add(code);
        db.SavedAccessCodes.Add(saved);
        db.SaveChanges();

        var controller = NewController(db, Stranger);

        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = photo.Id.ToString(),
            Quality = "Medium"
        });
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddToCart_Forbids_WhenNoOwnershipAdminOrSavedCode()
    {
        using var db = NewContext();
        var (_, _, photo) = Seed(db, Owner);
        var stranger = new User { Id = Stranger, UserName = "s@e.com", Email = "s@e.com" };
        db.Users.Add(stranger); db.SaveChanges();

        var controller = NewController(db, Stranger);

        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = photo.Id.ToString(),
            Quality = "Medium"
        });
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task AddToCart_Forbids_WhenSavedCodeExpired()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        var stranger = new User { Id = Stranger, UserName = "s@e.com", Email = "s@e.com" };
        var code = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            Code = "EXP1234",
            CreatedDate = DateTime.UtcNow.AddDays(-30),
            CreatedBy = Owner,
            ExpirationDate = DateTime.UtcNow.AddDays(-1) // expired
        };
        db.Users.Add(stranger);
        db.AccessCodes.Add(code);
        db.SavedAccessCodes.Add(new SavedAccessCode
        {
            Id = Guid.NewGuid(),
            UserId = Stranger,
            AccessCodeId = code.Id,
            SavedAt = DateTime.UtcNow.AddDays(-10)
        });
        db.SaveChanges();

        var controller = NewController(db, Stranger);
        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = photo.Id.ToString(),
            Quality = "Medium"
        });
        Assert.IsType<ForbidResult>(result.Result);
    }

    // ───────── cap ─────────

    [Fact]
    public async Task AddToCart_Returns409_WithCapReachedBody_WhenCapHit()
    {
        using var db = NewContext();
        var (_, album, _) = Seed(db, Owner);
        // Pre-fill 100 items for the user (different photos).
        for (var i = 0; i < CartController.MaxCartItems; i++)
        {
            var p = new Photo
            {
                Id = Guid.NewGuid(),
                AlbumId = album.Id,
                FileName = $"f{i}.jpg",
                UploadDate = DateTime.UtcNow
            };
            db.Photos.Add(p);
            db.UserCartItems.Add(new UserCartItem
            {
                Id = Guid.NewGuid(),
                UserId = Owner,
                PhotoId = p.Id,
                Quality = QualityType.Medium,
                SourceAlbumId = album.Id,
                AddedAt = DateTime.UtcNow
            });
        }
        // Add a NEW photo we'll try (and fail) to add to the cart.
        var newPhoto = new Photo
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            FileName = "new.jpg",
            UploadDate = DateTime.UtcNow
        };
        db.Photos.Add(newPhoto);
        db.SaveChanges();

        var controller = NewController(db, Owner);
        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = newPhoto.Id.ToString(),
            Quality = "Medium"
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(409, conflict.StatusCode);
        var body = Assert.IsType<CartCapResponse>(conflict.Value);
        Assert.Equal("cap_reached", body.Reason);
        Assert.Equal(100, body.Limit);
    }

    [Fact]
    public async Task AddToCart_AtCap_StillIdempotentForExistingItem()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        // Fill exactly 100 including 'photo'.
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(),
            UserId = Owner,
            PhotoId = photo.Id,
            Quality = QualityType.Medium,
            SourceAlbumId = album.Id,
            AddedAt = DateTime.UtcNow
        });
        for (var i = 0; i < CartController.MaxCartItems - 1; i++)
        {
            var p = new Photo
            {
                Id = Guid.NewGuid(),
                AlbumId = album.Id,
                FileName = $"f{i}.jpg",
                UploadDate = DateTime.UtcNow
            };
            db.Photos.Add(p);
            db.UserCartItems.Add(new UserCartItem
            {
                Id = Guid.NewGuid(),
                UserId = Owner,
                PhotoId = p.Id,
                Quality = QualityType.Medium,
                SourceAlbumId = album.Id,
                AddedAt = DateTime.UtcNow
            });
        }
        db.SaveChanges();

        var controller = NewController(db, Owner);
        var result = await controller.AddToCart(new AddCartItemRequest
        {
            PhotoId = photo.Id.ToString(),
            Quality = "Medium"
        });
        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ───────── remove / clear / get ─────────

    [Fact]
    public async Task RemoveFromCart_RemovesRow_AndReturnsNoContent()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(),
            UserId = Owner,
            PhotoId = photo.Id,
            Quality = QualityType.High,
            SourceAlbumId = album.Id,
            AddedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var controller = NewController(db, Owner);
        var result = await controller.RemoveFromCart(photo.Id, "High");

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(db.UserCartItems);
    }

    [Fact]
    public async Task ClearCart_RemovesEverythingForUser()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(), UserId = Owner, PhotoId = photo.Id,
            Quality = QualityType.Medium, SourceAlbumId = album.Id, AddedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var controller = NewController(db, Owner);
        var result = await controller.ClearCart();

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(db.UserCartItems);
    }

    [Fact]
    public async Task GetCart_ReturnsOnlyCurrentUsersItems()
    {
        using var db = NewContext();
        var (_, album, photo) = Seed(db, Owner);
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(), UserId = Owner, PhotoId = photo.Id,
            Quality = QualityType.High, SourceAlbumId = album.Id, AddedAt = DateTime.UtcNow
        });
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(), UserId = "someone-else", PhotoId = photo.Id,
            Quality = QualityType.Low, AddedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var controller = NewController(db, Owner);
        var result = await controller.GetCart();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<CartListResponse>(ok.Value);

        Assert.Single(body.Items);
        Assert.Equal(photo.Id.ToString(), body.Items[0].PhotoId);
        Assert.Equal("High", body.Items[0].Quality);
        Assert.Equal(album.Id.ToString(), body.Items[0].SourceAlbumId);
        Assert.Equal(album.Title, body.Items[0].SourceAlbumTitle);
    }

    // ───────── download path ─────────

    [Fact]
    public async Task Download_StreamsAuthorisedItems_AndDropsUnauthorised_WithSkippedHeader()
    {
        using var db = NewContext();
        // Album A: owned by user. Album B: stranger's, no saved code → unauthorised.
        var (_, ownedAlbum, ownedPhoto) = Seed(db, Owner, "-A");
        var (_, otherAlbum, otherPhoto) = Seed(db, "another-owner", "-B");
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(), UserId = Owner, PhotoId = ownedPhoto.Id,
            Quality = QualityType.High, SourceAlbumId = ownedAlbum.Id, AddedAt = DateTime.UtcNow
        });
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(), UserId = Owner, PhotoId = otherPhoto.Id,
            Quality = QualityType.Medium, SourceAlbumId = otherAlbum.Id, AddedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var cartZip = new Mock<ICartZipService>();
        List<CartZipItem>? streamed = null;
        cartZip.Setup(s => s.StreamCartZipAsync(
                It.IsAny<IReadOnlyList<CartZipItem>>(),
                It.IsAny<Stream>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>()))
            .Callback<IReadOnlyList<CartZipItem>, Stream, Guid?, string?>(
                (items, _, _, _) => streamed = items.ToList())
            .ReturnsAsync(1);

        var controller = NewController(db, Owner, cartZipMock: cartZip);
        var result = await controller.Download();

        Assert.IsType<EmptyResult>(result);
        Assert.NotNull(streamed);
        Assert.Single(streamed!);
        Assert.Equal(ownedPhoto.Id, streamed![0].PhotoId);

        var headerVal = controller.Response.Headers["X-Skipped-Photo-Ids"].ToString();
        Assert.Contains(otherPhoto.Id.ToString(), headerVal);
        Assert.DoesNotContain(ownedPhoto.Id.ToString(), headerVal);
    }

    [Fact]
    public async Task Download_Returns403_WhenEveryItemUnauthorised()
    {
        using var db = NewContext();
        var (_, otherAlbum, otherPhoto) = Seed(db, "another-owner", "-X");
        var stranger = new User { Id = Stranger, UserName = "s@e.com", Email = "s@e.com" };
        db.Users.Add(stranger);
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(), UserId = Stranger, PhotoId = otherPhoto.Id,
            Quality = QualityType.Medium, SourceAlbumId = otherAlbum.Id, AddedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var cartZip = new Mock<ICartZipService>();
        var controller = NewController(db, Stranger, cartZipMock: cartZip);
        var result = await controller.Download();

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);

        var headerVal = controller.Response.Headers["X-Skipped-Photo-Ids"].ToString();
        Assert.Contains(otherPhoto.Id.ToString(), headerVal);

        cartZip.Verify(s => s.StreamCartZipAsync(
            It.IsAny<IReadOnlyList<CartZipItem>>(), It.IsAny<Stream>(),
            It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Download_BadRequest_WhenCartEmpty()
    {
        using var db = NewContext();
        Seed(db, Owner);
        var controller = NewController(db, Owner);
        var result = await controller.Download();
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Download_CrossAlbum_StreamsBothWhenBothAuthorised()
    {
        using var db = NewContext();
        var (_, albumA, photoA) = Seed(db, Owner, "-A");
        var (_, albumB, photoB) = Seed(db, "other-owner", "-B");
        // User has saved a non-expired code for album B.
        var code = new AccessCode
        {
            Id = Guid.NewGuid(), AlbumId = albumB.Id, Code = "OK1234",
            CreatedDate = DateTime.UtcNow, CreatedBy = "other-owner",
            ExpirationDate = DateTime.UtcNow.AddDays(7)
        };
        db.AccessCodes.Add(code);
        db.SavedAccessCodes.Add(new SavedAccessCode
        {
            Id = Guid.NewGuid(), UserId = Owner, AccessCodeId = code.Id, SavedAt = DateTime.UtcNow
        });
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(), UserId = Owner, PhotoId = photoA.Id,
            Quality = QualityType.High, SourceAlbumId = albumA.Id, AddedAt = DateTime.UtcNow
        });
        db.UserCartItems.Add(new UserCartItem
        {
            Id = Guid.NewGuid(), UserId = Owner, PhotoId = photoB.Id,
            Quality = QualityType.Medium, SourceAlbumId = albumB.Id, AddedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var cartZip = new Mock<ICartZipService>();
        List<CartZipItem>? streamed = null;
        cartZip.Setup(s => s.StreamCartZipAsync(
                It.IsAny<IReadOnlyList<CartZipItem>>(), It.IsAny<Stream>(),
                It.IsAny<Guid?>(), It.IsAny<string?>()))
            .Callback<IReadOnlyList<CartZipItem>, Stream, Guid?, string?>(
                (items, _, _, _) => streamed = items.ToList())
            .ReturnsAsync(2);

        var controller = NewController(db, Owner, cartZipMock: cartZip);
        var result = await controller.Download();

        Assert.IsType<EmptyResult>(result);
        Assert.NotNull(streamed);
        Assert.Equal(2, streamed!.Count);
        var albumIds = streamed.Select(i => i.AlbumId).ToHashSet();
        Assert.Contains(albumA.Id, albumIds);
        Assert.Contains(albumB.Id, albumIds);
    }
}
