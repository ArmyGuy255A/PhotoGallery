using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Models;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// EPIC May 2026 / Bug #9 — repository-level tests for the per-user cart.
/// Validates CRUD, idempotent add, count semantics for the cap check,
/// and that callers must invoke SaveChangesAsync explicitly.
/// </summary>
public class UserCartRepositoryTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UserCartItem Make(ApplicationDbContext db, string userId, Guid? photoId = null, QualityType q = QualityType.Medium, Guid? albumId = null)
    {
        var pid = photoId ?? Guid.NewGuid();
        // Seed the Photo so EF's required-FK Include doesn't filter the row out
        // (production schema enforces this; in-memory EF mirrors the inner-join shape).
        if (!db.Photos.Any(p => p.Id == pid))
        {
            db.Photos.Add(new Photo
            {
                Id = pid,
                AlbumId = albumId ?? Guid.NewGuid(),
                FileName = $"f-{pid:N}.jpg",
                UploadDate = DateTime.UtcNow
            });
        }
        if (!db.Users.Any(u => u.Id == userId))
        {
            db.Users.Add(new User { Id = userId, UserName = $"{userId}@e.com", Email = $"{userId}@e.com" });
        }
        db.SaveChanges();
        return new UserCartItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PhotoId = pid,
            Quality = q,
            SourceAlbumId = albumId,
            AddedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task AddAsync_PersistsItem_AfterSaveChanges()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);
        var userId = "u1";

        await repo.AddAsync(Make(db, userId));
        Assert.Empty(db.UserCartItems); // not yet saved
        await repo.SaveChangesAsync();

        Assert.Single(db.UserCartItems);
    }

    [Fact]
    public async Task AddAsync_DoesNotPersist_WhenSaveChangesNotCalled()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);

        await repo.AddAsync(Make(db, "u1"));
        // No SaveChangesAsync call.

        // Repository memory contract: nothing is persisted until the caller
        // explicitly calls SaveChangesAsync. Count from the repo (which queries
        // the DbSet) should reflect that.
        var count = await repo.CountForUserAsync("u1");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AddAsync_IsIdempotentOnUserPhotoQuality()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);
        var userId = "u1";
        var photoId = Guid.NewGuid();

        var first = Make(db, userId, photoId, QualityType.High);
        await repo.AddAsync(first);
        await repo.SaveChangesAsync();

        var dup = Make(db, userId, photoId, QualityType.High);
        var returned = await repo.AddAsync(dup);
        await repo.SaveChangesAsync();

        Assert.Equal(first.Id, returned.Id);
        Assert.Single(db.UserCartItems);
    }

    [Fact]
    public async Task AddAsync_DistinguishesByQuality()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);
        var userId = "u1";
        var photoId = Guid.NewGuid();

        await repo.AddAsync(Make(db, userId, photoId, QualityType.Medium));
        await repo.AddAsync(Make(db, userId, photoId, QualityType.High));
        await repo.SaveChangesAsync();

        Assert.Equal(2, db.UserCartItems.Count());
    }

    [Fact]
    public async Task GetForUserAsync_ReturnsOnlyCurrentUser()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);
        await repo.AddAsync(Make(db, "alice"));
        await repo.AddAsync(Make(db, "alice"));
        await repo.AddAsync(Make(db, "bob"));
        await repo.SaveChangesAsync();

        var alices = await repo.GetForUserAsync("alice");
        Assert.Equal(2, alices.Count);
        Assert.All(alices, i => Assert.Equal("alice", i.UserId));
    }

    [Fact]
    public async Task RemoveAsync_RemovesMatchingRow_AndReturnsTrue()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);
        var pid = Guid.NewGuid();
        await repo.AddAsync(Make(db, "u1", pid, QualityType.Medium));
        await repo.SaveChangesAsync();

        var ok = await repo.RemoveAsync("u1", pid, QualityType.Medium);
        await repo.SaveChangesAsync();

        Assert.True(ok);
        Assert.Empty(db.UserCartItems);
    }

    [Fact]
    public async Task RemoveAsync_ReturnsFalse_WhenRowMissing()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);

        var ok = await repo.RemoveAsync("u1", Guid.NewGuid(), QualityType.Low);
        Assert.False(ok);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllRowsForUser_LeavesOthers()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);
        await repo.AddAsync(Make(db, "u1"));
        await repo.AddAsync(Make(db, "u1"));
        await repo.AddAsync(Make(db, "u2"));
        await repo.SaveChangesAsync();

        var n = await repo.ClearAsync("u1");
        await repo.SaveChangesAsync();

        Assert.Equal(2, n);
        Assert.Single(db.UserCartItems);
        Assert.Equal("u2", db.UserCartItems.Single().UserId);
    }

    [Fact]
    public async Task CountForUserAsync_ReturnsExactCount()
    {
        using var db = NewContext();
        var repo = new UserCartRepository(db);
        for (var i = 0; i < 5; i++)
            await repo.AddAsync(Make(db, "u1"));
        await repo.AddAsync(Make(db, "u2"));
        await repo.SaveChangesAsync();

        Assert.Equal(5, await repo.CountForUserAsync("u1"));
        Assert.Equal(1, await repo.CountForUserAsync("u2"));
        Assert.Equal(0, await repo.CountForUserAsync("nobody"));
    }
}
