using Microsoft.Extensions.Logging;
using Moq;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// RED-phase tests for D007 (Storage/Database Consistency Reconciliation).
///
/// These tests cover the four-case classification table from D007 plus the
/// edge cases enumerated in the design (original-missing short-circuit,
/// queue creation/repair, retryable Error handling, in-flight Processing
/// items, idempotency, concurrency serialization, cancellation).
///
/// All assertions target observable behavior (mutations on repositories,
/// summary counters, log emissions) rather than implementation details
/// like SaveChangesAsync call counts or "ListAsync called once."
/// </summary>
public class StorageConsistencyServiceTests
{
    private readonly Mock<IPhotoRepository> _mockPhotoRepository = new();
    private readonly Mock<IProcessingQueueRepository> _mockQueueRepository = new();
    private readonly Mock<IProcessingQueueItemRepository> _mockItemRepository = new();
    private readonly Mock<IPhotoVersionUrlRepository> _mockUrlRepository = new();
    private readonly Mock<IStorageProvider> _mockStorageProvider = new();
    private readonly Mock<ILogger<StorageConsistencyService>> _mockLogger = new();

    private StorageConsistencyService BuildService()
    {
        return new StorageConsistencyService(
            _mockPhotoRepository.Object,
            _mockQueueRepository.Object,
            _mockItemRepository.Object,
            _mockUrlRepository.Object,
            _mockStorageProvider.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Helper: configure the storage mock so every key in <paramref name="presentKeys"/>
    /// reports as present and the prefix list returns those keys.
    /// </summary>
    private void SetupStorage(string prefix, IEnumerable<string> presentKeys)
    {
        var keysList = presentKeys.ToList();
        _mockStorageProvider
            .Setup(s => s.ListAsync(prefix))
            .ReturnsAsync(keysList);
        foreach (var key in keysList)
        {
            _mockStorageProvider.Setup(s => s.ExistsAsync(key)).ReturnsAsync(true);
        }
    }

    private static Photo MakePhoto(Guid id, Guid albumId)
    {
        return new Photo { Id = id, AlbumId = albumId, FileName = "test.jpg" };
    }

    private static string KeyFor(Photo photo, string qualityLowercase)
    {
        return $"photogallery/{photo.AlbumId}/{photo.Id}/{qualityLowercase}.jpg";
    }

    private static string PrefixFor(Photo photo)
    {
        return $"photogallery/{photo.AlbumId}/{photo.Id}/";
    }

    private static IEnumerable<string> AllProcessedKeys(Photo photo)
    {
        return new[]
        {
            KeyFor(photo, "thumbnail"),
            KeyFor(photo, "low"),
            KeyFor(photo, "medium"),
            KeyFor(photo, "high"),
            // Watermark queue item produces both watermarked variants — include
            // them in the "fully processed" key set so tests that assume a
            // photo is complete actually mark it complete.
            KeyFor(photo, "thumbnail-watermarked"),
            KeyFor(photo, "medium-watermarked"),
        };
    }

    // ---- Case 0: empty / no-op ----

    [Fact]
    public async Task RunOnceAsync_Should_Return_Empty_Summary_When_No_Photos()
    {
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<Photo>());

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, report.PhotosScanned);
        Assert.Equal(0, report.ItemsCreatedPending);
        Assert.Equal(0, report.ItemsBackFilledComplete);
        Assert.Equal(0, report.ItemsRequeued);
        Assert.Equal(0, report.QueuesCreated);
        Assert.Equal(0, report.UrlsInvalidated);
        Assert.Equal(0, report.OriginalsMissing);
        _mockItemRepository.Verify(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
        _mockItemRepository.Verify(r => r.UpdateAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_Should_NoOp_When_All_Storage_Present_And_All_Items_Complete()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Complete };

        var presentKeys = new List<string> { KeyFor(photo, "original") };
        presentKeys.AddRange(AllProcessedKeys(photo));
        SetupStorage(PrefixFor(photo), presentKeys);

        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[]
        {
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Low,       Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Medium,    Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.High,      Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Watermark, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
        });

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.PhotosScanned);
        Assert.Equal(0, report.ItemsCreatedPending);
        Assert.Equal(0, report.ItemsBackFilledComplete);
        Assert.Equal(0, report.ItemsRequeued);
        Assert.Equal(0, report.QueuesCreated);
        Assert.Equal(0, report.UrlsInvalidated);
        _mockItemRepository.Verify(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
        _mockItemRepository.Verify(r => r.UpdateAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
        _mockQueueRepository.Verify(r => r.AddAsync(It.IsAny<ProcessingQueue>()), Times.Never);
        _mockQueueRepository.Verify(r => r.UpdateAsync(It.IsAny<ProcessingQueue>()), Times.Never);
    }

    // ---- Case 1: storage missing AND no item ----

    [Fact]
    public async Task RunOnceAsync_Should_Create_Pending_Item_When_Storage_Missing_And_No_Item()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Complete };

        // Original present, but no processed qualities present at all.
        SetupStorage(PrefixFor(photo), new[] { KeyFor(photo, "original") });

        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync(Array.Empty<ProcessingQueueItem>());

        var captured = new List<ProcessingQueueItem>();
        _mockItemRepository
            .Setup(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()))
            .Callback<ProcessingQueueItem>(captured.Add)
            .Returns(Task.CompletedTask);

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(5, report.ItemsCreatedPending);
        Assert.Equal(5, captured.Count);
        Assert.All(captured, i => Assert.Equal(ProcessingStatus.Pending, i.Status));
        Assert.All(captured, i => Assert.Equal(photo.Id, i.PhotoId));
        Assert.All(captured, i => Assert.Equal(queue.Id, i.ProcessingQueueId));
        var qualities = captured.Select(i => i.Quality).OrderBy(q => q).ToArray();
        Assert.Equal(new[] { QualityType.Thumbnail, QualityType.Low, QualityType.Medium, QualityType.High, QualityType.Watermark }.OrderBy(q => q).ToArray(), qualities);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Create_Queue_If_None_Exists_When_Creating_Pending_Item()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());

        SetupStorage(PrefixFor(photo), new[] { KeyFor(photo, "original") });
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync((ProcessingQueue?)null);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync(Array.Empty<ProcessingQueueItem>());

        var capturedQueues = new List<ProcessingQueue>();
        _mockQueueRepository
            .Setup(r => r.AddAsync(It.IsAny<ProcessingQueue>()))
            .Callback<ProcessingQueue>(capturedQueues.Add)
            .Returns(Task.CompletedTask);

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.QueuesCreated);
        Assert.Single(capturedQueues);
        Assert.Equal(photo.Id, capturedQueues[0].PhotoId);
        Assert.Equal(ProcessingStatus.Pending, capturedQueues[0].Status);
    }

    // ---- Case 2: storage missing AND item Complete ----

    [Fact]
    public async Task RunOnceAsync_Should_Flip_Complete_Item_To_Pending_When_Storage_Missing()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Complete };

        // Only the thumbnail quality is missing; others (and original) are present.
        var presentKeys = new List<string> { KeyFor(photo, "original"), KeyFor(photo, "low"), KeyFor(photo, "medium"), KeyFor(photo, "high") };
        SetupStorage(PrefixFor(photo), presentKeys);

        var thumbItem = new ProcessingQueueItem
        {
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Complete,
            ProcessingQueueId = queue.Id,
            CompletedAt = DateTime.UtcNow.AddDays(-3),
            RetryCount = 2,
            LastError = "transient io error",
            NextRetryTime = DateTime.UtcNow.AddSeconds(-5),
            Attempts = 3,
        };
        var others = new[] { QualityType.Low, QualityType.Medium, QualityType.High }
            .Select(q => new ProcessingQueueItem { PhotoId = photo.Id, Quality = q, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id });

        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync(new[] { thumbItem }.Concat(others));

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.ItemsRequeued);
        Assert.Equal(ProcessingStatus.Pending, thumbItem.Status);
        Assert.Equal(0, thumbItem.RetryCount);
        Assert.Null(thumbItem.LastError);
        Assert.Null(thumbItem.NextRetryTime);
        Assert.Null(thumbItem.CompletedAt);
        Assert.Equal(0, thumbItem.Attempts);
        _mockItemRepository.Verify(r => r.UpdateAsync(thumbItem), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Invalidate_Active_Cached_PhotoVersionUrl_When_Flipping_To_Pending()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Complete };

        var presentKeys = new List<string> { KeyFor(photo, "original"), KeyFor(photo, "low"), KeyFor(photo, "medium"), KeyFor(photo, "high") };
        SetupStorage(PrefixFor(photo), presentKeys);

        var thumbItem = new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[] { thumbItem });

        var cachedUrl = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/stale-url",
            ExpiresAt = DateTime.UtcNow.AddDays(5),
            GeneratedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true,
        };
        _mockUrlRepository
            .Setup(u => u.GetByPhotoAndQualityAsync(photo.Id, QualityType.Thumbnail))
            .ReturnsAsync(cachedUrl);

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.UrlsInvalidated);
        Assert.False(cachedUrl.IsActive);
        _mockUrlRepository.Verify(u => u.UpdateAsync(cachedUrl), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Reopen_Complete_Queue_When_Flipping_Item_To_Pending()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue
        {
            Id = Guid.NewGuid(),
            PhotoId = photo.Id,
            Status = ProcessingStatus.Complete,
            CompletedAt = DateTime.UtcNow.AddDays(-1),
            ErrorMessage = "stale error",
        };

        var presentKeys = new List<string> { KeyFor(photo, "original"), KeyFor(photo, "low"), KeyFor(photo, "medium"), KeyFor(photo, "high") };
        SetupStorage(PrefixFor(photo), presentKeys);

        var thumbItem = new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[] { thumbItem });

        await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(ProcessingStatus.Pending, queue.Status);
        Assert.Null(queue.CompletedAt);
        Assert.Null(queue.ErrorMessage);
        _mockQueueRepository.Verify(r => r.UpdateAsync(queue), Times.AtLeastOnce);
    }

    // ---- Case 3: storage present AND no item (back-fill) ----

    [Fact]
    public async Task RunOnceAsync_Should_Create_Complete_Item_When_Storage_Present_And_No_Item()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Pending };

        var presentKeys = new List<string> { KeyFor(photo, "original") };
        presentKeys.AddRange(AllProcessedKeys(photo));
        SetupStorage(PrefixFor(photo), presentKeys);

        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync(Array.Empty<ProcessingQueueItem>());

        var captured = new List<ProcessingQueueItem>();
        _mockItemRepository
            .Setup(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()))
            .Callback<ProcessingQueueItem>(captured.Add)
            .Returns(Task.CompletedTask);

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(5, report.ItemsBackFilledComplete);
        Assert.Equal(5, captured.Count);
        Assert.All(captured, i => Assert.Equal(ProcessingStatus.Complete, i.Status));
        Assert.All(captured, i => Assert.NotNull(i.CompletedAt));
        Assert.All(captured, i => Assert.Equal(queue.Id, i.ProcessingQueueId));
    }

    [Fact]
    public async Task RunOnceAsync_Should_Create_Queue_If_None_Exists_When_Back_Filling_Complete_Item()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());

        var presentKeys = new List<string> { KeyFor(photo, "original") };
        presentKeys.AddRange(AllProcessedKeys(photo));
        SetupStorage(PrefixFor(photo), presentKeys);

        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync((ProcessingQueue?)null);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync(Array.Empty<ProcessingQueueItem>());

        var capturedQueues = new List<ProcessingQueue>();
        _mockQueueRepository
            .Setup(r => r.AddAsync(It.IsAny<ProcessingQueue>()))
            .Callback<ProcessingQueue>(capturedQueues.Add)
            .Returns(Task.CompletedTask);

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.QueuesCreated);
        Assert.Single(capturedQueues);
        Assert.Equal(photo.Id, capturedQueues[0].PhotoId);
    }

    // ---- Edge: original missing ----

    [Fact]
    public async Task RunOnceAsync_Should_Skip_Photo_Entirely_When_Original_Missing()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());

        // Only thumbnail is present; original AND others are missing. Every reconciliation
        // path that touches a quality must short-circuit when original is gone.
        SetupStorage(PrefixFor(photo), new[] { KeyFor(photo, "thumbnail") });

        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync((ProcessingQueue?)null);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync(Array.Empty<ProcessingQueueItem>());

        var report = await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.OriginalsMissing);
        Assert.Equal(0, report.ItemsCreatedPending);
        Assert.Equal(0, report.ItemsBackFilledComplete);
        Assert.Equal(0, report.ItemsRequeued);
        Assert.Equal(0, report.QueuesCreated);
        Assert.Equal(0, report.UrlsInvalidated);
        _mockItemRepository.Verify(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
        _mockItemRepository.Verify(r => r.UpdateAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
        _mockQueueRepository.Verify(r => r.AddAsync(It.IsAny<ProcessingQueue>()), Times.Never);
        _mockQueueRepository.Verify(r => r.UpdateAsync(It.IsAny<ProcessingQueue>()), Times.Never);
        _mockUrlRepository.Verify(u => u.UpdateAsync(It.IsAny<PhotoVersionUrl>()), Times.Never);
    }

    // ---- Edge: in-flight Processing items are never touched ----

    [Fact]
    public async Task RunOnceAsync_Should_Skip_Processing_Items_With_Active_Lease()
    {
        // An active lease means a worker is currently doing the work — the
        // reconciler must NOT touch the row. (The orphan path — null or
        // expired lease — is exercised by a separate test below.)
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Processing };

        // Storage missing for thumbnail; the queue item for thumbnail is currently Processing.
        var presentKeys = new List<string> { KeyFor(photo, "original"), KeyFor(photo, "low"), KeyFor(photo, "medium"), KeyFor(photo, "high") };
        SetupStorage(PrefixFor(photo), presentKeys);

        var thumbItem = new ProcessingQueueItem
        {
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Processing,
            ProcessingQueueId = queue.Id,
            // Active lease — 5 minutes in the future.
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[] { thumbItem });

        await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(ProcessingStatus.Processing, thumbItem.Status);
        _mockItemRepository.Verify(r => r.UpdateAsync(thumbItem), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Reset_Orphaned_Processing_Item_With_Null_Lease()
    {
        // ReleaseLeaseAsync nulls the lease but doesn't reset the status.
        // If the caller then crashed, the row is stuck at Processing with
        // no lease — the reconciler must treat it as an orphan and reset
        // to Pending. (41 stuck items in trial traced to this path.)
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Processing };
        var presentKeys = new List<string> { KeyFor(photo, "original"), KeyFor(photo, "low"), KeyFor(photo, "medium"), KeyFor(photo, "high") };
        SetupStorage(PrefixFor(photo), presentKeys);

        var thumbItem = new ProcessingQueueItem
        {
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Processing,
            ProcessingQueueId = queue.Id,
            LeaseExpiresAt = null  // <-- orphan signal
        };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[] { thumbItem });

        await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(ProcessingStatus.Pending, thumbItem.Status);
        _mockItemRepository.Verify(r => r.UpdateAsync(thumbItem), Times.Once);
    }

    // ---- Phase 4 §1: dead-letter behaviour removed — every Error item must retry forever ----

    [Fact]
    public async Task RunOnceAsync_Should_Reset_Error_Items_With_High_RetryCount_When_Storage_Missing()
    {
        // Pre-Phase-4 this branch was the "max retries — leave alone" dead-letter case.
        // Now there is no max; an Error item with a high RetryCount is still a retry candidate
        // and the consistency sweep should reset it to Pending when its storage is missing.
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Error };

        // Thumbnail missing from storage; other 3 base qualities present.
        var presentKeys = new List<string> { KeyFor(photo, "original"), KeyFor(photo, "low"), KeyFor(photo, "medium"), KeyFor(photo, "high") };
        SetupStorage(PrefixFor(photo), presentKeys);

        var pummeled = new ProcessingQueueItem
        {
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Error,
            ProcessingQueueId = queue.Id,
            RetryCount = 17,
            LastError = "still failing",
        };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[] { pummeled });

        await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(ProcessingStatus.Pending, pummeled.Status);
        Assert.Equal(0, pummeled.RetryCount);
        _mockItemRepository.Verify(r => r.UpdateAsync(pummeled), Times.Once);
    }

    // ---- Edge: retryable Error items ----

    [Fact]
    public async Task RunOnceAsync_Should_Reset_Retryable_Error_Item_When_Storage_Missing()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Error };

        var presentKeys = new List<string> { KeyFor(photo, "original"), KeyFor(photo, "low"), KeyFor(photo, "medium"), KeyFor(photo, "high") };
        SetupStorage(PrefixFor(photo), presentKeys);

        var retryable = new ProcessingQueueItem
        {
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Error,
            ProcessingQueueId = queue.Id,
            RetryCount = 1,
            LastError = "io error",
            NextRetryTime = DateTime.UtcNow.AddSeconds(2),
            Attempts = 2,
        };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[] { retryable });

        await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(ProcessingStatus.Pending, retryable.Status);
        Assert.Equal(0, retryable.RetryCount);
        Assert.Null(retryable.LastError);
        Assert.Null(retryable.NextRetryTime);
        Assert.Equal(0, retryable.Attempts);
        _mockItemRepository.Verify(r => r.UpdateAsync(retryable), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Mark_Retryable_Error_Item_Complete_When_Storage_Present()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Error };

        var presentKeys = new List<string> { KeyFor(photo, "original") };
        presentKeys.AddRange(AllProcessedKeys(photo));
        SetupStorage(PrefixFor(photo), presentKeys);

        var retryable = new ProcessingQueueItem
        {
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Error,
            ProcessingQueueId = queue.Id,
            RetryCount = 1,
            LastError = "transient",
            Attempts = 2,
        };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[] { retryable });

        await BuildService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(ProcessingStatus.Complete, retryable.Status);
        Assert.NotNull(retryable.CompletedAt);
        Assert.Null(retryable.LastError);
        _mockItemRepository.Verify(r => r.UpdateAsync(retryable), Times.Once);
    }

    // ---- Idempotency ----

    [Fact]
    public async Task RunOnceAsync_Should_Be_Idempotent_When_Run_Twice_With_All_Items_Repaired()
    {
        // After the "first" run repaired everything, the second run sees a fully-consistent
        // world and must perform zero mutations. We simulate the post-first-run state directly.
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Complete };

        var presentKeys = new List<string> { KeyFor(photo, "original") };
        presentKeys.AddRange(AllProcessedKeys(photo));
        SetupStorage(PrefixFor(photo), presentKeys);

        var items = new[]
        {
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Low,       Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Medium,    Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.High,      Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Watermark, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
        };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(items);

        var service = BuildService();
        var first = await service.RunOnceAsync(CancellationToken.None);
        var second = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, first.ItemsCreatedPending + first.ItemsBackFilledComplete + first.ItemsRequeued + first.QueuesCreated + first.UrlsInvalidated);
        Assert.Equal(0, second.ItemsCreatedPending + second.ItemsBackFilledComplete + second.ItemsRequeued + second.QueuesCreated + second.UrlsInvalidated);
        _mockItemRepository.Verify(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
        _mockItemRepository.Verify(r => r.UpdateAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
    }

    // ---- Concurrency: SemaphoreSlim serializes RunOnceAsync ----

    [Fact]
    public async Task RunOnceAsync_Should_Serialize_Concurrent_Calls_Via_Semaphore()
    {
        // Verify the SemaphoreSlim(1,1) gate works: two simultaneous RunOnceAsync invocations
        // must NOT have overlapping access to the storage provider. We instrument the mock
        // with an "in-flight" counter that throws on reentry.
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<Photo>());

        var inFlight = 0;
        var maxInFlight = 0;
        _mockPhotoRepository
            .Setup(r => r.GetAllAsync())
            .Returns(async () =>
            {
                var current = Interlocked.Increment(ref inFlight);
                // Track the high-water mark.
                while (true)
                {
                    var observed = maxInFlight;
                    if (current <= observed) break;
                    if (Interlocked.CompareExchange(ref maxInFlight, current, observed) == observed) break;
                }
                await Task.Delay(50);
                Interlocked.Decrement(ref inFlight);
                return Array.Empty<Photo>();
            });

        var service = BuildService();
        var t1 = service.RunOnceAsync(CancellationToken.None);
        var t2 = service.RunOnceAsync(CancellationToken.None);
        await Task.WhenAll(t1, t2);

        Assert.Equal(1, maxInFlight);
    }

    // ---- Cancellation ----

    [Fact]
    public async Task RunOnceAsync_Should_Throw_OperationCanceledException_When_Token_PreCancelled()
    {
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<Photo>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BuildService().RunOnceAsync(cts.Token));

        _mockItemRepository.Verify(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
        _mockItemRepository.Verify(r => r.UpdateAsync(It.IsAny<ProcessingQueueItem>()), Times.Never);
        _mockQueueRepository.Verify(r => r.AddAsync(It.IsAny<ProcessingQueue>()), Times.Never);
    }

    // ---- Persistence: SaveChangesAsync must be called for every mutation path ----
    //
    // RED-phase regression tests for the bug where the reconciler appeared to work
    // (returned a non-zero report) but actually persisted nothing because none of
    // the AddAsync/UpdateAsync calls were followed by SaveChangesAsync. The DbContext
    // tracked the changes, the using-scope around the worker disposed the context,
    // and all reconciliation work was silently abandoned. Live thumbnails were not
    // restored because subsequent worker ticks saw zero new Pending items.
    //
    // These tests assert SaveChangesAsync was invoked on each repository that
    // received writes. This is implementation-leaning, but it is the only signal a
    // mock can provide; the alternative is a real-DB integration test, which is
    // already covered by D004.

    [Fact]
    public async Task RunOnceAsync_Should_Persist_When_Creating_Pending_Items()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Complete };

        SetupStorage(PrefixFor(photo), new[] { KeyFor(photo, "original") });
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync(Array.Empty<ProcessingQueueItem>());

        await BuildService().RunOnceAsync(CancellationToken.None);

        _mockItemRepository.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Persist_When_Requeuing_Complete_Items_With_Missing_Storage()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Complete };

        SetupStorage(PrefixFor(photo), new[] { KeyFor(photo, "original") });
        var completeItem = new ProcessingQueueItem
        {
            PhotoId = photo.Id,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Complete,
            ProcessingQueueId = queue.Id,
            CompletedAt = DateTime.UtcNow.AddDays(-1),
        };
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[] { completeItem });

        await BuildService().RunOnceAsync(CancellationToken.None);

        _mockItemRepository.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Persist_Queue_When_Creating_Missing_Queue()
    {
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());

        SetupStorage(PrefixFor(photo), new[] { KeyFor(photo, "original") });
        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync((ProcessingQueue?)null);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id))
            .ReturnsAsync(Array.Empty<ProcessingQueueItem>());

        await BuildService().RunOnceAsync(CancellationToken.None);

        _mockQueueRepository.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Not_Save_When_No_Mutations_Occurred()
    {
        // No-op cycles must not issue gratuitous SaveChangesAsync calls — there's nothing
        // to flush. This guards against a "save on every cycle" anti-fix where someone
        // would just unconditionally call SaveChangesAsync at the end of RunOnceAsync.
        var photo = MakePhoto(Guid.NewGuid(), Guid.NewGuid());
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photo.Id, Status = ProcessingStatus.Complete };

        var presentKeys = new List<string> { KeyFor(photo, "original") };
        presentKeys.AddRange(AllProcessedKeys(photo));
        SetupStorage(PrefixFor(photo), presentKeys);

        _mockPhotoRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { photo });
        _mockQueueRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(queue);
        _mockItemRepository.Setup(r => r.GetByPhotoIdAsync(photo.Id)).ReturnsAsync(new[]
        {
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Low,       Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Medium,    Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.High,      Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
            new ProcessingQueueItem { PhotoId = photo.Id, Quality = QualityType.Watermark, Status = ProcessingStatus.Complete, ProcessingQueueId = queue.Id },
        });

        await BuildService().RunOnceAsync(CancellationToken.None);

        _mockItemRepository.Verify(r => r.SaveChangesAsync(), Times.Never);
        _mockQueueRepository.Verify(r => r.SaveChangesAsync(), Times.Never);
        _mockUrlRepository.Verify(r => r.SaveChangesAsync(), Times.Never);
    }
}
