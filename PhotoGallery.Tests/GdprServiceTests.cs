using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;

namespace PhotoGallery.Tests;

public class GdprServiceTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (User user, Album album, Photo photo, AccessCode code) SeedUserWithData(
        ApplicationDbContext ctx, string suffix = "a")
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"user-{suffix}@example.com",
            Email = $"user-{suffix}@example.com",
            FirstName = "First",
            LastName = "Last",
            CreatedDate = DateTime.UtcNow.AddDays(-30),
            IsActive = true
        };
        var album = new Album
        {
            Id = Guid.NewGuid(),
            Title = $"Album {suffix}",
            OwnerId = user.Id,
            CreatedBy = user.Id,
            CreatedDate = DateTime.UtcNow.AddDays(-10)
        };
        var photo = new Photo
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            FileName = $"photo-{suffix}.jpg",
            StorageKey = $"photogallery/{album.Id}/{Guid.NewGuid()}/original.jpg",
            UploadDate = DateTime.UtcNow.AddDays(-5),
            UploadedBy = user.Id
        };
        var code = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            Code = $"CODE{suffix.ToUpper()}{Guid.NewGuid():N}".Substring(0, 12),
            CreatedBy = user.Id,
            CreatedDate = DateTime.UtcNow.AddDays(-3)
        };
        ctx.Users.Add(user);
        ctx.Albums.Add(album);
        ctx.Photos.Add(photo);
        ctx.AccessCodes.Add(code);
        ctx.SaveChanges();
        return (user, album, photo, code);
    }

    private static GdprService NewService(ApplicationDbContext ctx, IAuditLogRepository? audit = null)
    {
        audit ??= new AuditLogRepository(ctx);
        return new GdprService(ctx, audit, NullLogger<GdprService>.Instance);
    }

    [Fact]
    public async Task ExportUserData_IncludesAllOwnedAlbums()
    {
        using var ctx = NewContext();
        var (user, album, photo, code) = SeedUserWithData(ctx);

        // Add a second album for the same user
        var album2 = new Album
        {
            Id = Guid.NewGuid(),
            Title = "Second Album",
            OwnerId = user.Id,
            CreatedBy = user.Id,
            CreatedDate = DateTime.UtcNow
        };
        ctx.Albums.Add(album2);
        await ctx.SaveChangesAsync();

        var service = NewService(ctx);
        var export = await service.ExportUserDataAsync(user.Id);

        Assert.Equal("1.0", export.SchemaVersion);
        Assert.Equal(user.Id, export.Profile.Id);
        Assert.Equal(user.Email, export.Profile.Email);
        Assert.Equal(2, export.OwnedAlbums.Count);
        Assert.Contains(export.OwnedAlbums, a => a.Id == album.Id);
        Assert.Contains(export.OwnedAlbums, a => a.Id == album2.Id);
        Assert.Single(export.UploadedPhotos);
        Assert.Single(export.SavedAccessCodes);
    }

    [Fact]
    public async Task ExportUserData_DoesNotIncludeOtherUsersData()
    {
        using var ctx = NewContext();
        var (alice, _, _, _) = SeedUserWithData(ctx, "alice");
        var (bob, bobAlbum, bobPhoto, bobCode) = SeedUserWithData(ctx, "bob");

        var service = NewService(ctx);
        var export = await service.ExportUserDataAsync(alice.Id);

        Assert.DoesNotContain(export.OwnedAlbums, a => a.Id == bobAlbum.Id);
        Assert.DoesNotContain(export.UploadedPhotos, p => p.Id == bobPhoto.Id);
        Assert.DoesNotContain(export.SavedAccessCodes, c => c.Id == bobCode.Id);
        Assert.Equal(alice.Id, export.Profile.Id);
        Assert.NotEqual(bob.Email, export.Profile.Email);
    }

    [Fact]
    public async Task DeleteUser_RemovesUserAndCascadeAlbums()
    {
        using var ctx = NewContext();
        var (user, album, _, _) = SeedUserWithData(ctx);

        var service = NewService(ctx);
        await service.DeleteUserAsync(user.Id, "admin@example.com");

        Assert.Null(await ctx.Users.FindAsync(user.Id));
        Assert.Null(await ctx.Albums.FindAsync(album.Id));
    }

    [Fact]
    public async Task DeleteUser_RemovesPhotos_AndAccessCodes()
    {
        using var ctx = NewContext();
        var (user, album, photo, code) = SeedUserWithData(ctx);

        // Add a download tied to the user's photo so we exercise cleanup.
        var download = new Download
        {
            Id = Guid.NewGuid(),
            PhotoId = photo.Id,
            AccessCodeId = code.Id,
            Quality = QualityType.Medium,
            DownloadedAt = DateTime.UtcNow,
            IpHash = new string('a', 64)
        };
        ctx.Downloads.Add(download);
        await ctx.SaveChangesAsync();

        var service = NewService(ctx);
        await service.DeleteUserAsync(user.Id, "admin@example.com");

        Assert.Empty(ctx.Photos.Where(p => p.Id == photo.Id));
        Assert.Empty(ctx.AccessCodes.Where(c => c.Id == code.Id));
        Assert.Empty(ctx.Downloads.Where(d => d.Id == download.Id));
    }

    [Fact]
    public async Task DeleteUser_WritesAuditLogEntry_WithActorEmail()
    {
        using var ctx = NewContext();
        var (user, _, _, _) = SeedUserWithData(ctx);

        var service = NewService(ctx);
        await service.DeleteUserAsync(user.Id, "admin@example.com");

        var entries = await ctx.AuditLogEntries.OrderBy(a => a.Timestamp).ToListAsync();
        Assert.Contains(entries, e => e.Action == "user.deleted" && e.ActorEmail == "admin@example.com" && e.TargetId == user.Id);
        Assert.Contains(entries, e => e.Action == "user.deletion.storage-pending" && e.TargetId == user.Id);
    }

    [Fact]
    public async Task DeleteUser_Throws_WhenUserNotFound()
    {
        using var ctx = NewContext();
        var service = NewService(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteUserAsync("does-not-exist", "admin@example.com"));
    }

    [Fact]
    public async Task ExportUserData_Throws_WhenUserNotFound()
    {
        using var ctx = NewContext();
        var service = NewService(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExportUserDataAsync("does-not-exist"));
    }

    [Fact]
    public async Task DeleteUser_AuditWritten_EvenWhenAuditRepoIsMocked()
    {
        // Sanity check that the service depends on the IAuditLogRepository abstraction.
        using var ctx = NewContext();
        var (user, _, _, _) = SeedUserWithData(ctx);

        var auditMock = new Mock<IAuditLogRepository>();
        auditMock.Setup(a => a.AddEntryAsync(It.IsAny<AuditLogEntry>()))
            .Returns(Task.CompletedTask);

        var service = new GdprService(ctx, auditMock.Object, NullLogger<GdprService>.Instance);
        await service.DeleteUserAsync(user.Id, "actor@example.com");

        auditMock.Verify(
            a => a.AddEntryAsync(It.Is<AuditLogEntry>(e => e.Action == "user.deleted" && e.ActorEmail == "actor@example.com")),
            Times.Once);
    }
}
