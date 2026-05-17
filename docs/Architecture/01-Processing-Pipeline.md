# 01 — Photo Processing Pipeline

The end-to-end lifecycle of a photo, from the user's click to the watermarked download. This is the most important architectural surface in the system. Everything else (workers, admin jobs, reconciliation) exists to keep this pipeline correct.

## The seven blobs

A fully-processed photo has exactly seven blobs in storage:

| Blob                         | Source                       | Used by                                    |
| ---------------------------- | ---------------------------- | ------------------------------------------ |
| `original.jpg`               | direct upload from SPA       | paid checkout, archival download           |
| `thumbnail.jpg`              | derived, 200x200             | grid view                                  |
| `low.jpg`                    | derived, 800x800             | mobile viewing                             |
| `medium.jpg`                 | derived, 1920x1920           | web/email                                  |
| `high.jpg`                   | derived, 3840x3840           | print, large download                      |
| `thumbnail-watermarked.jpg`  | derived from thumbnail       | public code-gallery grid                   |
| `medium-watermarked.jpg`     | derived from medium          | public code-gallery modal preview          |

A photo is `Complete` only when all seven exist. This is the canonical definition, enforced by `StorageConsistencyService.ReconcilePhotoStatusAsync` (`PhotoGallery/Services/Processing/StorageConsistencyService.cs`).

Storage path layout: `photogallery/{albumId}/{photoId}/{quality}.jpg`.

## Phases

The pipeline has three phases per photo. Each phase has clear ownership and a clear hand-off.

### Phase 1: Direct upload to blob

Owner: API + SPA.

1. SPA calls `POST /api/photos/albums/{id}/upload-tickets`. Body is the file list.
2. API inserts a `Photo` row with `ProcessingStatus = Uploading`. Returns a write SAS URL per file plus the `photoId`.
3. SPA PUTs each file directly to storage. Bytes never traverse the API host.
4. SPA calls `POST /api/photos/{photoId}/upload-complete` once the PUT 200s.
5. API flips `Photo.ProcessingStatus` from `Uploading` to `Pending`. Inserts five `ProcessingQueueItem` rows: Thumbnail, Low, Medium, High, Watermark.

What protects this phase:

* A 1-hour grace window. If a Photo row has been in `Uploading` longer than that with no blob present, `StorageConsistencyService` flips it to `Failed`.
* If the SPA dies after the PUT but before `upload-complete`, the next reconcile pass sees the blob present and a Photo row in `Uploading`, then promotes the row to `Pending`.

### Phase 2: Worker derives the six variants

Owner: `PhotoProcessingWorker` on a worker replica.

1. Worker leases up to `WorkerParallelism × LeaseBatchMultiplier` queue items in one DB statement (`ProcessingQueueItemRepository.LeaseNextBatchAsync`).
2. For each leased item, the worker downloads `original.jpg`, resizes / watermarks, uploads the result blob.
3. On success, the item flips to `Status = Complete`.
4. On error, the item flips to `Status = Error` with a retry timer using bounded exponential backoff.
5. The Watermark queue item is a special case. It produces two blobs in one go: `thumbnail-watermarked.jpg` + `medium-watermarked.jpg`. It is enqueued exactly once per photo and depends on the four base qualities being present.

What protects this phase:

* A DB-level lease (`LeaseExpiresAt`) keeps two workers from picking the same item.
* The lease query also reclaims orphans: rows where `Status = Processing AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt < now)`. This recovers from a worker that crashed mid-tick or got `OperationCanceledException`.
* `PhotoProcessing:WorkerParallelism` is hot-reloadable. Drop it to 2 on a 0.5 vCPU container to keep the API responsive during bulk uploads.

### Phase 3: Pre-signed URLs

Owner: `PhotoVersionUrlService` on the API.

1. When a controller needs to return URLs to the SPA, it calls `GetPhotoVersionUrlAsync` per quality.
2. The service hits an in-process sliding cache first. Cache TTL is bounded at the underlying SAS expiry minus a 3-minute safety margin.
3. On miss, it mints a new pre-signed URL (Azure: user-delegation SAS, MinIO: S3 pre-signed) and persists it to the `PhotoVersionUrls` table.
4. `PhotoVersionUrlRefreshWorker` runs once a day and rotates URLs that are within `BlobStorage:PreSignedUrlRefreshWindowDays` of expiry.

What protects this phase:

* The cache is bounded by SAS expiry, so we cannot hand out an expired URL.
* `BlobStorage:VerifyCachedUrls` (default off) HEAD-checks each cached URL before reuse. Safer but pays a round-trip per cache hit. The reconciler is the cheaper alternative for catching drift.

## End-to-end happy path

```
SPA           API           Storage      Worker            DB
 │ POST upload-tickets       │            │                 │
 │ ──────────▶  │ INSERT Photo (Uploading), mint SAS        │
 │ ◀─── 200 (sas, photoId) ──│            │                 │
 │ PUT file      │            │            │                 │
 │ ──────────────────────────▶  blob lands │                 │
 │ POST upload-complete       │            │                 │
 │ ──────────▶  │ Photo → Pending, enqueue 5 items          │
 │ ◀─── 200 ────│            │            │                 │
 │              │            │            │ lease batch     │
 │              │            │            │ ──────────────▶ │
 │              │            │ download   │                 │
 │              │            │ original ◀─│                 │
 │              │            │ upload     │                 │
 │              │            │ derived ◀──│                 │
 │              │            │            │ item → Complete │
 │              │            │            │ ──────────────▶ │
 │              │            │            │ (repeat × 5)    │
 │ GET album w/photo URLs    │            │                 │
 │ ──────────▶  │ GetPhotoVersionUrlAsync per quality        │
 │              │ → cache or mint SAS, return URLs           │
 │ ◀─── 200 (urls) ──────────│            │                 │
```

A sequence diagram of this same flow is in [../Diagrams/03-Sequence-Photo-Upload.md](../Diagrams/03-Sequence-Photo-Upload.md).

## Failure modes and how the pipeline recovers

| Failure                                          | Recovery mechanism                                                                                            |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------- |
| SPA dies after PUT, before `upload-complete`     | `StorageConsistencyService` sees blob present + Uploading row, promotes to Pending.                           |
| Upload SAS expires before user finishes          | Photo row stays in Uploading. After 1 h with no blob, reconciler flips to Failed.                             |
| Worker crashes mid-tick                          | Lease expires. Next lease query reclaims the row. Item retries.                                                |
| Worker hits `OperationCanceledException`         | `ReleaseLeaseAsync` nulls the lease. Lease query catches `Status=Processing AND LeaseExpiresAt IS NULL`.       |
| Original blob gets deleted (chaos, manual)       | Photo is older than 1 h with no original. Reconciler flips Photo to Failed. Admin enqueues `purge-failed-photos`. |
| Derived blob gets deleted                        | Reconciler sees Complete queue item + missing blob, resets item to Pending. Worker re-derives.                 |
| Watermarked variant deleted                      | Same as above. Watermark item is `present` only if BOTH watermarked blobs exist. Either missing flips it to Pending. |
| Cached pre-signed URL expires before refresh     | `PhotoVersionUrlRefreshWorker` should catch it. Backup: cache TTL is bounded under SAS expiry.                 |
| Storage backend has blobs whose Photo row is gone | `OrphanedBlobReaperService` lists prefixes, finds blobs older than the grace window with no DB row, deletes.   |
| Admin queue duplicate                            | `EnqueueAdminJobAsync` is idempotent on (`JobType`, `AlbumId`). Returns the existing jobId.                   |

This recovery story is the reason the chaos engineering job exists (`AdminJobTypes.ChaosStorage`). Trial intentionally allows blob deletion so we can validate the reconcile + reap + purge loop runs cleanly.

## What the service health page shows about the pipeline

The Photos card on the Service Health page derives its counts live from `ProcessingQueueItem` state, not from the slow-moving `Photo.ProcessingStatus` column.

| Bucket      | Definition                                                            |
| ----------- | --------------------------------------------------------------------- |
| Uploading   | `Photo.ProcessingStatus = Uploading`.                                  |
| Failed      | `Photo.ProcessingStatus = Failed`.                                     |
| Processing  | Photo has at least one queue item with `Status = Processing`.          |
| Pending     | Photo has at least one Pending item AND no Processing items, OR no queue items yet. |
| Complete    | Every queue item is Complete.                                          |

This means the dashboard reflects worker state in seconds, not hours. The slow column is still authoritative for the two terminal states because they depend on storage truth rather than queue truth.

## Hot-reloadable parameters

| Setting key                                       | Default | Effect                                                                                  |
| ------------------------------------------------- | ------- | --------------------------------------------------------------------------------------- |
| `PhotoProcessing:IntervalSeconds`                 | 5       | `PhotoProcessingWorker` tick interval.                                                  |
| `PhotoProcessing:WorkerParallelism`               | 5       | Max concurrent resize consumers per tick. Drop to 2 on a 0.5 vCPU container.            |
| `PhotoProcessing:LeaseBatchMultiplier`            | 4       | Items leased per tick = WorkerParallelism × this.                                       |
| `BlobStorage:PreSignedUrlTTLDays`                 | 7       | Lifetime of pre-signed download URLs.                                                   |
| `BlobStorage:PreSignedUrlRefreshWindowDays`       | 5       | When refresh worker rotates URLs.                                                       |
| `BlobStorage:UrlCacheSlidingMinutes`              | 30      | Sliding in-process cache TTL for public URLs.                                           |
| `BlobStorage:PublicUrlTtlMinutes`                 | 60      | TTL for short-lived public-visitor URLs.                                                |
| `BlobStorage:VerifyCachedUrls`                    | false   | HEAD-check each cached URL on reuse. Safer, slower.                                     |
| `Workers:StorageConsistency:TickIntervalSeconds`  | 10      | How often the reconciler worker polls the AdminJob queue.                               |
| `Workers:OrphanedBlobReaper:TickIntervalSeconds`  | 10      | How often the reaper worker polls.                                                      |
| `Workers:Scheduler:ReconcileIntervalHours`        | 1       | How often the API auto-enqueues a routine reconcile.                                    |
| `Workers:Scheduler:ReapIntervalHours`             | 6       | How often the API auto-enqueues a routine reap.                                         |

See [04-Runtime-Settings.md](04-Runtime-Settings.md) for the catalogue mechanics.

## Where to read next

* The queue surface: [02-AdminJob-Queue.md](02-AdminJob-Queue.md).
* Storage swap: [06-Storage-Abstraction.md](06-Storage-Abstraction.md).
* Photo download flow: [../Diagrams/04-Sequence-Photo-Download.md](../Diagrams/04-Sequence-Photo-Download.md).
