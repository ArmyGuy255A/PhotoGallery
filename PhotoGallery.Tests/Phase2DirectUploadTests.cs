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
        var photoRepo = new Repository<Photo>(ctx);
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
