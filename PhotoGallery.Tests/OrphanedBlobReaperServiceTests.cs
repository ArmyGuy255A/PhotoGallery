using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Controllers;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using System.Reflection;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Tests for <see cref="OrphanedBlobReaperService"/> (Phase 5).
///
/// Covers:
/// <list type="bullet">
///   <item>Reaper deletes every blob under an album prefix whose GUID is not in the Albums table.</item>
///   <item>Reaper deletes every blob under a photo prefix whose GUID is not in the Photos table (album still exists).</item>
///   <item>Reaper does NOT delete blobs within the configured grace window — protects in-flight uploads.</item>
///   <item>Reaper skips non-GUID top-level prefixes defensively.</item>
///   <item>Reaper does nothing when DB and storage are consistent.</item>
///   <item>Reaper photo-prefix mismatch (Photo exists but AlbumId differs from the storage parent) is treated as orphan.</item>
///   <item>Admin endpoint requires the Admin role (reflective check) and returns 200 with the summary report when invoked.</item>
/// </list>
///
/// Mocks <see cref="IStorageProvider"/> directly: matches the harness used by
/// <see cref="StorageConsistencyServiceTests"/>, no Azurite required.
/// </summary>
public class OrphanedBlobReaperServiceTests
{
    private const string Root = "photogallery/";

    private readonly Mock<IAlbumRepository> _mockAlbums = new();
    private readonly Mock<IPhotoRepository> _mockPhotos = new();
    private readonly Mock<IStorageProvider> _mockStorage = new();
    private readonly Mock<ILogger<OrphanedBlobReaperService>> _mockLogger = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    private OrphanedBlobReaperService BuildService(int graceMinutes = 60)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:OrphanReapGraceMinutes"] = graceMinutes.ToString(),
            })
            .Build();
        return new OrphanedBlobReaperService(
            _mockAlbums.Object,
            _mockPhotos.Object,
            _mockStorage.Object,
            config,
            _mockLogger.Object,
            _time);
    }

    private static string AlbumPrefix(Guid albumId) => $"{Root}{albumId}/";
    private static string PhotoPrefix(Guid albumId, Guid photoId) => $"{Root}{albumId}/{photoId}/";

    private void SetupRoot(params string[] albumPrefixes)
    {
        _mockStorage.Setup(s => s.ListSubPrefixesAsync(Root)).ReturnsAsync(albumPrefixes);
    }

    private void SetupAlbumChildren(string albumPrefix, params string[] photoPrefixes)
    {
        _mockStorage.Setup(s => s.ListSubPrefixesAsync(albumPrefix)).ReturnsAsync(photoPrefixes);
    }

    private void SetupBlobs(string prefix, params BlobInfo[] blobs)
    {
        _mockStorage.Setup(s => s.ListWithMetadataAsync(prefix)).ReturnsAsync(blobs);
    }

    private void SetupAlbumExists(Guid albumId, bool exists)
    {
        _mockAlbums.Setup(r => r.GetByIdAsync(albumId))
            .ReturnsAsync(exists ? new Album { Id = albumId } : null);
    }

    private void SetupPhotoExists(Guid photoId, Guid albumId, bool exists)
    {
        _mockPhotos.Setup(r => r.GetByIdAsync(photoId))
            .ReturnsAsync(exists ? new Photo { Id = photoId, AlbumId = albumId, FileName = "x.jpg" } : null);
    }

    private List<string> CaptureDeletedKeys()
    {
        var deleted = new List<string>();
        _mockStorage.Setup(s => s.DeleteManyAsync(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(keys => deleted.AddRange(keys))
            .ReturnsAsync((IEnumerable<string> keys) => keys.Count());
        return deleted;
    }

    [Fact]
    public async Task RunOnce_OrphanedAlbum_DeletesAllBlobsAndReports()
    {
        var orphanAlbumId = Guid.NewGuid();
        var ap = AlbumPrefix(orphanAlbumId);
        SetupRoot(ap);
        SetupAlbumExists(orphanAlbumId, false);

        var oldTime = _time.GetUtcNow().AddHours(-2);
        SetupBlobs(ap,
            new BlobInfo($"{ap}p1/original.jpg", 1000, oldTime),
            new BlobInfo($"{ap}p1/thumbnail.jpg", 100, oldTime),
            new BlobInfo($"{ap}p2/original.jpg", 2000, oldTime));

        var deleted = CaptureDeletedKeys();

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.Scanned.Albums);
        Assert.Single(report.OrphanedAlbums, orphanAlbumId);
        Assert.Empty(report.OrphanedPhotos);
        Assert.Equal(3, report.BlobsDeleted);
        Assert.Equal(3100L, report.BytesReclaimed);
        Assert.Equal(0, report.SkippedByGracePeriod);
        Assert.Equal(3, deleted.Count);
        // Reaper must NOT descend into per-photo prefixes of an orphaned album.
        _mockStorage.Verify(s => s.ListSubPrefixesAsync(ap), Times.Never);
    }

    [Fact]
    public async Task RunOnce_OrphanedPhotoWithExistingAlbum_DeletesPhotoPrefixOnly()
    {
        var albumId = Guid.NewGuid();
        var liveId = Guid.NewGuid();
        var deadId = Guid.NewGuid();
        var ap = AlbumPrefix(albumId);
        var liveP = PhotoPrefix(albumId, liveId);
        var deadP = PhotoPrefix(albumId, deadId);

        SetupRoot(ap);
        SetupAlbumExists(albumId, true);
        SetupAlbumChildren(ap, liveP, deadP);
        SetupPhotoExists(liveId, albumId, true);
        SetupPhotoExists(deadId, albumId, false);

        var oldTime = _time.GetUtcNow().AddHours(-2);
        SetupBlobs(deadP,
            new BlobInfo($"{deadP}original.jpg", 500, oldTime),
            new BlobInfo($"{deadP}thumbnail.jpg", 50, oldTime));
        // Live photo blobs should not be enumerated for reap because the photo exists;
        // but we set up an empty list defensively in case the impl ever does.
        SetupBlobs(liveP);

        var deleted = CaptureDeletedKeys();

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.Scanned.Albums);
        Assert.Equal(2, report.Scanned.Photos);
        Assert.Empty(report.OrphanedAlbums);
        Assert.Single(report.OrphanedPhotos, deadId);
        Assert.Equal(2, report.BlobsDeleted);
        Assert.Equal(550L, report.BytesReclaimed);
        Assert.Equal(2, deleted.Count);
        Assert.All(deleted, k => Assert.StartsWith(deadP, k));
    }

    [Fact]
    public async Task RunOnce_BlobsWithinGraceWindow_AreNotDeleted()
    {
        // In-flight upload: blob is in storage but DB row hasn't landed yet.
        var ghostAlbumId = Guid.NewGuid();
        var ap = AlbumPrefix(ghostAlbumId);
        SetupRoot(ap);
        SetupAlbumExists(ghostAlbumId, false);

        var fresh = _time.GetUtcNow().AddMinutes(-5); // inside default 60-min grace
        SetupBlobs(ap,
            new BlobInfo($"{ap}p1/original.jpg", 1000, fresh),
            new BlobInfo($"{ap}p1/thumbnail.jpg", 100, fresh));

        var deleted = CaptureDeletedKeys();

        var report = await BuildService(graceMinutes: 60).RunOnceAsync(CancellationToken.None);

        Assert.Empty(report.OrphanedAlbums);
        Assert.Equal(0, report.BlobsDeleted);
        Assert.Equal(0L, report.BytesReclaimed);
        Assert.Equal(2, report.SkippedByGracePeriod);
        Assert.Empty(deleted);
        _mockStorage.Verify(s => s.DeleteManyAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task RunOnce_NonGuidTopLevelPrefix_IsSkippedDefensively()
    {
        SetupRoot($"{Root}not-a-guid/");

        var deleted = CaptureDeletedKeys();

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Empty(report.OrphanedAlbums);
        Assert.Empty(report.OrphanedPhotos);
        Assert.Equal(0, report.BlobsDeleted);
        Assert.Empty(deleted);
        _mockAlbums.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task RunOnce_PhotoExistsButBelongsToDifferentAlbum_TreatedAsOrphan()
    {
        // Defensive: if a Photo row exists but its AlbumId disagrees with the
        // storage parent prefix, the blobs under this parent are orphans.
        var albumId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var otherAlbumId = Guid.NewGuid();
        var ap = AlbumPrefix(albumId);
        var pp = PhotoPrefix(albumId, photoId);

        SetupRoot(ap);
        SetupAlbumExists(albumId, true);
        SetupAlbumChildren(ap, pp);
        _mockPhotos.Setup(r => r.GetByIdAsync(photoId))
            .ReturnsAsync(new Photo { Id = photoId, AlbumId = otherAlbumId, FileName = "x.jpg" });

        var oldTime = _time.GetUtcNow().AddHours(-2);
        SetupBlobs(pp, new BlobInfo($"{pp}original.jpg", 100, oldTime));

        var deleted = CaptureDeletedKeys();

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Single(report.OrphanedPhotos, photoId);
        Assert.Equal(1, report.BlobsDeleted);
        Assert.Single(deleted);
    }

    [Fact]
    public async Task RunOnce_NoOrphans_NoDeletes()
    {
        var albumId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var ap = AlbumPrefix(albumId);
        var pp = PhotoPrefix(albumId, photoId);
        SetupRoot(ap);
        SetupAlbumExists(albumId, true);
        SetupAlbumChildren(ap, pp);
        SetupPhotoExists(photoId, albumId, true);

        var deleted = CaptureDeletedKeys();

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, report.BlobsDeleted);
        Assert.Empty(report.OrphanedAlbums);
        Assert.Empty(report.OrphanedPhotos);
        Assert.Empty(deleted);
        _mockStorage.Verify(s => s.DeleteManyAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public void TryParseGuidFromPrefix_AcceptsWellFormed_RejectsGarbage()
    {
        var g = Guid.NewGuid();
        Assert.True(OrphanedBlobReaperService.TryParseGuidFromPrefix($"photogallery/{g}/", "photogallery/", out var parsed));
        Assert.Equal(g, parsed);

        Assert.False(OrphanedBlobReaperService.TryParseGuidFromPrefix("photogallery/not-a-guid/", "photogallery/", out _));
        Assert.False(OrphanedBlobReaperService.TryParseGuidFromPrefix("", "photogallery/", out _));
        Assert.False(OrphanedBlobReaperService.TryParseGuidFromPrefix("photogallery/", "other/", out _));
    }

    [Fact]
    public void ReapOrphans_EndpointAttribute_RequiresAdminRole()
    {
        var method = typeof(PhotosController).GetMethod(nameof(PhotosController.ReapOrphans));
        Assert.NotNull(method);
        var authz = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authz);
        Assert.Equal("Admin", authz!.Roles);
    }

    [Fact]
    public async Task ReapOrphans_Endpoint_ReturnsOkWithReport()
    {
        // Drive the controller method directly with mocks. Wiring through the
        // auth pipeline is covered by the attribute-presence test above; here
        // we assert the happy-path response shape and that the service is invoked.
        var orphanAlbumId = Guid.NewGuid();
        var ap = AlbumPrefix(orphanAlbumId);
        SetupRoot(ap);
        SetupAlbumExists(orphanAlbumId, false);
        var oldTime = _time.GetUtcNow().AddHours(-2);
        SetupBlobs(ap, new BlobInfo($"{ap}p1/original.jpg", 123, oldTime));
        CaptureDeletedKeys();

        var reaper = BuildService();
        var controller = new PhotosController(
            imageProcessor: Mock.Of<IImageProcessor>(),
            storageProvider: _mockStorage.Object,
            photoRepository: Mock.Of<IRepository<Photo>>(),
            albumRepository: Mock.Of<IRepository<Album>>(),
            photoVersionRepository: Mock.Of<IRepository<PhotoVersion>>(),
            queueItemRepository: Mock.Of<IProcessingQueueItemRepository>(),
            storageConsistencyService: BuildPlaceholderConsistencyService(),
            orphanedBlobReaperService: reaper,
            logger: NullLogger<PhotosController>.Instance);
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin@test"),
                                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin") },
                        "Test"))
            }
        };

        var result = await controller.ReapOrphans(CancellationToken.None);

        var ok = result as OkObjectResult;
        if (ok is null)
        {
            var obj = Assert.IsType<ObjectResult>(result);
            throw new Xunit.Sdk.XunitException($"Expected 200 OK, got {obj.StatusCode}: {System.Text.Json.JsonSerializer.Serialize(obj.Value)}");
        }
        var report = Assert.IsType<OrphanReapReport>(ok.Value);
        Assert.Equal(1, report.BlobsDeleted);
        Assert.Single(report.OrphanedAlbums, orphanAlbumId);
        Assert.Equal(123L, report.BytesReclaimed);
    }

    /// <summary>
    /// PhotosController's constructor still requires a StorageConsistencyService
    /// instance. Build a minimal one with Mock.Of so the constructor is satisfied;
    /// the ReapOrphans path never touches it.
    /// </summary>
    private static StorageConsistencyService BuildPlaceholderConsistencyService()
    {
        return new StorageConsistencyService(
            Mock.Of<IPhotoRepository>(),
            Mock.Of<IProcessingQueueRepository>(),
            Mock.Of<IProcessingQueueItemRepository>(),
            Mock.Of<IPhotoVersionUrlRepository>(),
            Mock.Of<IStorageProvider>(),
            NullLogger<StorageConsistencyService>.Instance);
    }

    /// <summary>
    /// Minimal <see cref="TimeProvider"/> stub fixed to a single instant so the
    /// grace-window math in the service is deterministic.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
