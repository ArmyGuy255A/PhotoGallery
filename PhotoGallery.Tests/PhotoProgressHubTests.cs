using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Hubs;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Phase 3 unit coverage for <see cref="PhotoProgressHub"/>:
///   - OnConnectedAsync joins the per-user group derived from the
///     NameIdentifier claim. This is what makes
///     <c>Clients.Group($"user:{uploadedBy}")</c> broadcasts reach the
///     uploader's tabs.
///   - RequestStatus returns a snapshot to the calling connection only.
/// </summary>
public class PhotoProgressHubTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static PhotoProgressHub BuildHub(
        ApplicationDbContext ctx,
        string userId,
        out Mock<IGroupManager> groups,
        out Mock<IHubCallerClients> clients,
        out Mock<ISingleClientProxy> callerProxy,
        string connectionId = "conn-1")
    {
        var photoRepo = new Repository<Photo>(ctx);
        var queueItemRepo = new ProcessingQueueItemRepository(ctx);

        var hub = new PhotoProgressHub(
            queueItemRepo,
            photoRepo,
            NullLogger<PhotoProgressHub>.Instance);

        groups = new Mock<IGroupManager>();
        clients = new Mock<IHubCallerClients>();
        callerProxy = new Mock<ISingleClientProxy>();
        clients.SetupGet(c => c.Caller).Returns(callerProxy.Object);

        var context = new Mock<HubCallerContext>();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }, "TestAuth");
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(identity));
        context.SetupGet(c => c.ConnectionId).Returns(connectionId);

        hub.Context = context.Object;
        hub.Groups = groups.Object;
        hub.Clients = clients.Object;
        return hub;
    }

    [Fact]
    public async Task OnConnectedAsync_AddsConnectionToUserGroup()
    {
        using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        const string userId = "user-42";
        var hub = BuildHub(ctx, userId, out var groups, out _, out _, connectionId: "abc");

        await hub.OnConnectedAsync();

        groups.Verify(g => g.AddToGroupAsync(
                "abc",
                PhotoProgressHub.UserGroup(userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestStatus_SendsSnapshotToCallerOnly_ForOwnedPhoto()
    {
        using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        const string userId = "user-42";
        var photoId = Guid.NewGuid();
        var queueId = Guid.NewGuid();
        ctx.Photos.Add(new Photo
        {
            Id = photoId,
            AlbumId = Guid.NewGuid(),
            FileName = "x.jpg",
            StorageKey = "k",
            UploadedBy = userId,
            UploadDate = DateTime.UtcNow,
            ProcessingStatus = PhotoProcessingStatus.Processing
        });
        ctx.ProcessingQueues.Add(new ProcessingQueue { Id = queueId, PhotoId = photoId });
        ctx.ProcessingQueueItems.AddRange(
            new ProcessingQueueItem { PhotoId = photoId, ProcessingQueueId = queueId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { PhotoId = photoId, ProcessingQueueId = queueId, Quality = QualityType.Low, Status = ProcessingStatus.Processing });
        await ctx.SaveChangesAsync();

        var hub = BuildHub(ctx, userId, out _, out _, out var callerProxy);

        await hub.RequestStatus(photoId.ToString());

        callerProxy.Verify(c => c.SendCoreAsync(
                "StatusSnapshot",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestStatus_RefusesPhotoOwnedByAnotherUser()
    {
        using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        var photoId = Guid.NewGuid();
        ctx.Photos.Add(new Photo
        {
            Id = photoId,
            AlbumId = Guid.NewGuid(),
            FileName = "x.jpg",
            StorageKey = "k",
            UploadedBy = "someone-else",
            UploadDate = DateTime.UtcNow,
            ProcessingStatus = PhotoProcessingStatus.Pending
        });
        await ctx.SaveChangesAsync();

        var hub = BuildHub(ctx, "intruder", out _, out _, out var callerProxy);

        await hub.RequestStatus(photoId.ToString());

        callerProxy.Verify(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
