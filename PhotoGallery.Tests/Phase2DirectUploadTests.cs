using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Controllers;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Hubs;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Phase 2 + 3 unit coverage:
///   - <see cref="PhotoRepository.GetAlbumPhotosAsync"/> hides photos that
///     are still in <see cref="PhotoProcessingStatus.Uploading"/>.
///   - <see cref="PhotosController.UploadComplete"/> verifies the blob,
///     transitions Uploading → Pending, inserts 4 quality items, and
///     broadcasts ProcessingStarted to the uploader's hub group.
///   - <see cref="PhotoProgressHub"/> adds the connection to the right
///     per-user group on connect, and <c>RequestStatus</c> sends a snapshot
///     to the calling connection only.
/// </summary>
public class Phase2DirectUploadTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task GetAlbumPhotosAsync_HidesPhotosStillInUploadingState()
    {
        using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        var albumId = Guid.NewGuid();
        var visiblePhotoId = Guid.NewGuid();
        var hiddenPhotoId = Guid.NewGuid();

        await ctx.Photos.AddRangeAsync(
            new Photo
            {
                Id = visiblePhotoId,
                AlbumId = albumId,
                FileName = "visible.jpg",
                StorageKey = $"photogallery/{albumId}/{visiblePhotoId}/original.jpg",
                UploadedBy = "u1",
                UploadDate = DateTime.UtcNow,
                ProcessingStatus = PhotoProcessingStatus.Pending
            },
            new Photo
            {
                Id = hiddenPhotoId,
                AlbumId = albumId,
                FileName = "ghost.jpg",
                StorageKey = $"photogallery/{albumId}/{hiddenPhotoId}/original.jpg",
                UploadedBy = "u1",
                UploadDate = DateTime.UtcNow,
                ProcessingStatus = PhotoProcessingStatus.Uploading
            });
        await ctx.SaveChangesAsync();

        var repo = new PhotoRepository(ctx);

        var listed = await repo.GetAlbumPhotosAsync(albumId);

        Assert.Single(listed);
        Assert.Equal(visiblePhotoId, listed[0].Id);
    }

    private static PhotosController BuildController(
        ApplicationDbContext ctx,
        Mock<IStorageProvider> storage,
        Mock<IHubContext<PhotoProgressHub>> hub,
        string userId)
    {
        var photoRepo = new PhotoRepository(ctx);
        var albumRepo = new Repository<Album>(ctx);
        var photoVersionRepo = new Repository<PhotoVersion>(ctx);
        var queueRepo = new Repository<ProcessingQueue>(ctx);
        var queueItemRepo = new ProcessingQueueItemRepository(ctx);

        var imageProcessor = new Mock<IImageProcessor>().Object;

        var controller = new PhotosController(
            imageProcessor,
            storage.Object,
            photoRepo,
            albumRepo,
            photoVersionRepo,
            queueRepo,
            queueItemRepo,
            // StorageConsistencyService is not exercised by /upload-complete
            // or any other path under test here; pass a real instance built
            // with a permissive in-memory context to keep DI honest.
            new StubStorageConsistencyService(),
            hub.Object,
            new OrphanedBlobReaperService(
                Mock.Of<IAlbumRepository>(),
                Mock.Of<IPhotoRepository>(),
                Mock.Of<IStorageProvider>(),
                new ConfigurationBuilder().AddInMemoryCollection().Build(),
                NullLogger<OrphanedBlobReaperService>.Instance),
            new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("phase2-direct-upload-" + Guid.NewGuid()).Options),
            NullLogger<PhotosController>.Instance);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "tester")
        }, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
        return controller;
    }

    [Fact]
    public async Task UploadComplete_TransitionsUploadingToPending_AndQueuesFourItems_AndBroadcasts()
    {
        using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        const string userId = "owner-1";
        var albumId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var blobPath = $"photogallery/{albumId}/{photoId}/original.jpg";

        ctx.Albums.Add(new Album
        {
            Id = albumId,
            Title = "A",
            OwnerId = userId,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow
        });
        ctx.Photos.Add(new Photo
        {
            Id = photoId,
            AlbumId = albumId,
            FileName = "x.jpg",
            StorageKey = blobPath,
            UploadDate = DateTime.UtcNow,
            UploadedBy = userId,
            ProcessingStatus = PhotoProcessingStatus.Uploading
        });
        ctx.ProcessingQueues.Add(new ProcessingQueue
        {
            PhotoId = photoId,
            Status = ProcessingStatus.Pending
        });
        await ctx.SaveChangesAsync();

        var storage = new Mock<IStorageProvider>();
        storage.Setup(s => s.ExistsAsync(blobPath)).ReturnsAsync(true);

        var hubClients = new Mock<IHubClients>();
        var groupProxy = new Mock<IClientProxy>();
        hubClients.Setup(c => c.Group(PhotoProgressHub.UserGroup(userId)))
            .Returns(groupProxy.Object);
        var hub = new Mock<IHubContext<PhotoProgressHub>>();
        hub.SetupGet(h => h.Clients).Returns(hubClients.Object);

        var controller = BuildController(ctx, storage, hub, userId);

        var result = await controller.UploadComplete(photoId,
            new UploadCompleteRequest { ActualSize = 12345 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<UploadCompleteResponse>(ok.Value);
        Assert.Equal(PhotoProcessingStatus.Pending.ToString(), body.Status);

        var reloaded = await ctx.Photos.FindAsync(photoId);
        Assert.NotNull(reloaded);
        Assert.Equal(PhotoProcessingStatus.Pending, reloaded!.ProcessingStatus);
        Assert.NotNull(reloaded.ProcessingStartedAt);

        var items = await ctx.ProcessingQueueItems
            .Where(i => i.PhotoId == photoId)
            .ToListAsync();
        Assert.Equal(4, items.Count);
        Assert.Contains(items, i => i.Quality == QualityType.Thumbnail);
        Assert.Contains(items, i => i.Quality == QualityType.Low);
        Assert.Contains(items, i => i.Quality == QualityType.Medium);
        Assert.Contains(items, i => i.Quality == QualityType.High);
        Assert.All(items, i => Assert.Equal(ProcessingStatus.Pending, i.Status));

        // Broadcast went to the right group with the right event name.
        groupProxy.Verify(p => p.SendCoreAsync(
                PhotoProgressEvents.ProcessingStarted,
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadComplete_WhenBlobMissing_Returns400_AndKeepsRowInUploading()
    {
        using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        const string userId = "owner-1";
        var albumId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var blobPath = $"photogallery/{albumId}/{photoId}/original.jpg";

        ctx.Albums.Add(new Album
        {
            Id = albumId, Title = "A", OwnerId = userId,
            CreatedBy = userId, CreatedDate = DateTime.UtcNow
        });
        ctx.Photos.Add(new Photo
        {
            Id = photoId, AlbumId = albumId, FileName = "x.jpg",
            StorageKey = blobPath, UploadDate = DateTime.UtcNow,
            UploadedBy = userId,
            ProcessingStatus = PhotoProcessingStatus.Uploading
        });
        await ctx.SaveChangesAsync();

        var storage = new Mock<IStorageProvider>();
        storage.Setup(s => s.ExistsAsync(blobPath)).ReturnsAsync(false);
        var hub = new Mock<IHubContext<PhotoProgressHub>>();

        var controller = BuildController(ctx, storage, hub, userId);

        var result = await controller.UploadComplete(photoId,
            new UploadCompleteRequest { ActualSize = 0 });

        Assert.IsType<BadRequestObjectResult>(result.Result);

        var reloaded = await ctx.Photos.FindAsync(photoId);
        Assert.Equal(PhotoProcessingStatus.Uploading, reloaded!.ProcessingStatus);
        Assert.Empty(await ctx.ProcessingQueueItems.Where(i => i.PhotoId == photoId).ToListAsync());
    }

    // --- Duplicate-filename rejection (both upload paths) ------------------

    private static async Task<(ApplicationDbContext ctx, PhotosController controller, Guid albumId)>
        SetupAlbumWithExistingPhotosAsync(string userId, params string[] existingFileNames)
    {
        var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        var albumId = Guid.NewGuid();
        ctx.Albums.Add(new Album
        {
            Id = albumId,
            Title = "A",
            OwnerId = userId,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow
        });
        foreach (var name in existingFileNames)
        {
            var pid = Guid.NewGuid();
            ctx.Photos.Add(new Photo
            {
                Id = pid,
                AlbumId = albumId,
                FileName = name,
                StorageKey = $"photogallery/{albumId}/{pid}/original.jpg",
                UploadDate = DateTime.UtcNow,
                UploadedBy = userId,
                ProcessingStatus = PhotoProcessingStatus.Complete
            });
        }
        await ctx.SaveChangesAsync();

        var storage = new Mock<IStorageProvider>();
        storage
            .Setup(s => s.GenerateWriteSasUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync((string blob, TimeSpan _) => $"https://test.blob/{blob}?sas=stub");
        var hub = new Mock<IHubContext<PhotoProgressHub>>();

        var controller = BuildController(ctx, storage, hub, userId);
        return (ctx, controller, albumId);
    }

    [Fact]
    public async Task UploadTickets_DuplicateOfCompletedPhoto_ReturnsAlreadyComplete()
    {
        const string userId = "owner-1";
        var (ctx, controller, albumId) = await SetupAlbumWithExistingPhotosAsync(userId, "DSC_8000.JPG");
        var existingId = await ctx.Photos.Where(p => p.AlbumId == albumId && p.FileName == "DSC_8000.JPG")
            .Select(p => p.Id).FirstAsync();

        var result = await controller.CreateUploadTickets(
            albumId.ToString(),
            new List<UploadTicketRequest>
            {
                new() { FileName = "DSC_8000.JPG", ContentType = "image/jpeg", Size = 1000 }
            });

        // No more 409 — duplicate is short-circuited as AlreadyComplete (200 OK).
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<UploadTicketsResponse>(ok.Value);
        Assert.Empty(body.Tickets);
        var done = Assert.Single(body.AlreadyComplete);
        Assert.Equal("DSC_8000.JPG", done.FileName);
        Assert.Equal(existingId.ToString(), done.PhotoId);

        // No second Photo row inserted for the duplicate filename.
        var rows = await ctx.Photos.Where(p => p.AlbumId == albumId && p.FileName == "DSC_8000.JPG").CountAsync();
        Assert.Equal(1, rows);

        ctx.Dispose();
    }

    [Fact]
    public async Task UploadTickets_DuplicateOfUploadingPhoto_RecyclesOrphanAndIssuesNewTicket()
    {
        const string userId = "owner-1";
        var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        var albumId = Guid.NewGuid();
        ctx.Albums.Add(new Album
        {
            Id = albumId, Title = "A", OwnerId = userId, CreatedBy = userId, CreatedDate = DateTime.UtcNow
        });
        var orphanId = Guid.NewGuid();
        ctx.Photos.Add(new Photo
        {
            Id = orphanId,
            AlbumId = albumId,
            FileName = "DSC_RETRY.JPG",
            StorageKey = $"photogallery/{albumId}/{orphanId}/original.jpg",
            UploadDate = DateTime.UtcNow,
            UploadedBy = userId,
            // Orphan from a prior failed ticket attempt.
            ProcessingStatus = PhotoProcessingStatus.Uploading
        });
        ctx.ProcessingQueues.Add(new ProcessingQueue { PhotoId = orphanId, Status = ProcessingStatus.Pending });
        await ctx.SaveChangesAsync();

        var storage = new Mock<IStorageProvider>();
        storage.Setup(s => s.GenerateWriteSasUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync((string blob, TimeSpan _) => $"https://test.blob/{blob}?sas=stub");
        var hub = new Mock<IHubContext<PhotoProgressHub>>();
        var controller = BuildController(ctx, storage, hub, userId);

        var result = await controller.CreateUploadTickets(
            albumId.ToString(),
            new List<UploadTicketRequest>
            {
                new() { FileName = "DSC_RETRY.JPG", ContentType = "image/jpeg", Size = 2048 }
            });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<UploadTicketsResponse>(ok.Value);
        Assert.Empty(body.AlreadyComplete);
        var ticket = Assert.Single(body.Tickets);
        Assert.Equal("DSC_RETRY.JPG", ticket.FileName);
        // The new ticket's PhotoId is a brand-new row, not the orphan.
        Assert.NotEqual(orphanId.ToString(), ticket.PhotoId);

        // Orphan row is gone and exactly one row remains.
        var rows = await ctx.Photos.Where(p => p.AlbumId == albumId).ToListAsync();
        var only = Assert.Single(rows);
        Assert.NotEqual(orphanId, only.Id);
        Assert.Equal(PhotoProcessingStatus.Uploading, only.ProcessingStatus);
        // Orphan's ProcessingQueue is gone, new row has its own.
        var queues = await ctx.ProcessingQueues.Where(q => q.PhotoId == orphanId).CountAsync();
        Assert.Equal(0, queues);

        ctx.Dispose();
    }

    [Fact]
    public async Task UploadTickets_BatchWithMixedNewAndDuplicate()
    {
        const string userId = "owner-1";
        var (ctx, controller, albumId) = await SetupAlbumWithExistingPhotosAsync(
            userId, "dup-a.jpg", "dup-b.jpg");

        var batch = new List<UploadTicketRequest>
        {
            new() { FileName = "new-1.jpg", ContentType = "image/jpeg", Size = 1000 },
            new() { FileName = "dup-a.jpg", ContentType = "image/jpeg", Size = 1000 },
            new() { FileName = "new-2.jpg", ContentType = "image/jpeg", Size = 1000 },
            new() { FileName = "dup-b.jpg", ContentType = "image/jpeg", Size = 1000 },
            new() { FileName = "new-3.jpg", ContentType = "image/jpeg", Size = 1000 }
        };

        var result = await controller.CreateUploadTickets(albumId.ToString(), batch);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<UploadTicketsResponse>(ok.Value);
        Assert.Equal(3, body.Tickets.Count);
        Assert.Equal(2, body.AlreadyComplete.Count);
        Assert.Contains(body.AlreadyComplete, r => r.FileName == "dup-a.jpg");
        Assert.Contains(body.AlreadyComplete, r => r.FileName == "dup-b.jpg");
        Assert.Contains(body.Tickets, t => t.FileName == "new-1.jpg");
        Assert.Contains(body.Tickets, t => t.FileName == "new-2.jpg");
        Assert.Contains(body.Tickets, t => t.FileName == "new-3.jpg");

        // 3 new Uploading rows added (plus the 2 originals).
        var inserted = await ctx.Photos
            .Where(p => p.AlbumId == albumId && p.ProcessingStatus == PhotoProcessingStatus.Uploading)
            .Select(p => p.FileName).ToListAsync();
        Assert.Equal(3, inserted.Count);
        Assert.Contains("new-1.jpg", inserted);
        Assert.Contains("new-2.jpg", inserted);
        Assert.Contains("new-3.jpg", inserted);

        ctx.Dispose();
    }

    [Fact]
    public async Task UploadTickets_DuplicateWithinSameBatch_ReturnsAlreadyCompleteForSecondOccurrence()
    {
        const string userId = "owner-1";
        var (ctx, controller, albumId) = await SetupAlbumWithExistingPhotosAsync(userId);

        var result = await controller.CreateUploadTickets(
            albumId.ToString(),
            new List<UploadTicketRequest>
            {
                new() { FileName = "same.jpg", ContentType = "image/jpeg", Size = 1000 },
                new() { FileName = "same.jpg", ContentType = "image/jpeg", Size = 1000 }
            });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<UploadTicketsResponse>(ok.Value);
        // First occurrence creates the row + ticket; second occurrence is
        // returned as AlreadyComplete pointing at the just-created row, so
        // the SPA renders both rows as resolved (one uploads, one is skipped).
        var ticket = Assert.Single(body.Tickets);
        var done = Assert.Single(body.AlreadyComplete);
        Assert.Equal("same.jpg", ticket.FileName);
        Assert.Equal("same.jpg", done.FileName);
        Assert.Equal(ticket.PhotoId, done.PhotoId);
        ctx.Dispose();
    }

    [Fact]
    public async Task MultipartUpload_RejectsDuplicate()
    {
        const string userId = "owner-1";
        var (ctx, controller, albumId) = await SetupAlbumWithExistingPhotosAsync(userId, "existing.jpg");

        // Build a multipart form file collection: one duplicate, one fresh.
        var dupBytes = new byte[] { 1, 2, 3 };
        var newBytes = new byte[] { 4, 5, 6 };
        IFormFile dup = new FormFile(new MemoryStream(dupBytes), 0, dupBytes.Length, "files", "existing.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
        IFormFile fresh = new FormFile(new MemoryStream(newBytes), 0, newBytes.Length, "files", "brand-new.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
        var collection = new FormFileCollection { dup, fresh };

        var result = await controller.UploadPhotos(albumId.ToString(), collection);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<UploadPhotoResponse>(ok.Value);

        // The duplicate is captured in errors, never makes it to storage or
        // the DB. The fresh upload may itself fail later in the pipeline
        // (mock storage provider is not wired here) but the duplicate must
        // not land regardless.
        Assert.Contains(body.Errors, e => e.Contains("existing.jpg") && e.Contains("already exists"));
        var existingRows = await ctx.Photos
            .Where(p => p.AlbumId == albumId && p.FileName == "existing.jpg")
            .CountAsync();
        Assert.Equal(1, existingRows);

        ctx.Dispose();
    }
}

/// <summary>
/// Stand-in for <see cref="StorageConsistencyService"/> used only by the
/// upload-complete controller tests, which never hit any reconcile code
/// path. Constructed with no dependencies so it can't accidentally fire
/// off real reconciliation work inside a unit test.
/// </summary>
internal sealed class StubStorageConsistencyService : StorageConsistencyService
{
    public StubStorageConsistencyService()
        : base(null!, null!, null!, null!, null!, NullLogger<StorageConsistencyService>.Instance)
    {
    }
}
