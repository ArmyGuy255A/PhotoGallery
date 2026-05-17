# 03 — Photo Upload Sequence

End-to-end sequence for the happy path of one photo upload. Reference for [../Architecture/01-Processing-Pipeline.md](../Architecture/01-Processing-Pipeline.md).

## Diagram

```mermaid
sequenceDiagram
    autonumber
    participant SPA as Angular SPA
    participant API as API replica
    participant DB as SQL Server
    participant Stg as Object Storage<br/>(MinIO / Azure Blob)
    participant Wkr as Worker replica

    Note over SPA,Wkr: Phase 1 — direct upload to blob
    SPA->>API: POST /api/photos/albums/{id}/upload-tickets<br/>(files: [name, size, ...])
    API->>DB: INSERT Photo (ProcessingStatus=Uploading)
    API->>Stg: mint write SAS URL per file
    API-->>SPA: 200 OK<br/>[{ photoId, sasUrl }]
    SPA->>Stg: PUT file → sasUrl<br/>(bytes never traverse API)
    Stg-->>SPA: 200 OK
    SPA->>API: POST /api/photos/{photoId}/upload-complete
    API->>DB: UPDATE Photo SET ProcessingStatus=Pending
    API->>DB: INSERT 5 ProcessingQueueItems<br/>(Thumbnail, Low, Medium, High, Watermark)
    API-->>SPA: 200 OK

    Note over SPA,Wkr: Phase 2 — worker derives variants
    Wkr->>DB: LeaseNextBatchAsync<br/>(claim N items atomically)
    DB-->>Wkr: rows + lease until T+5min
    loop for each item
        Wkr->>Stg: GET original.jpg
        Stg-->>Wkr: bytes
        Wkr->>Wkr: resize / watermark
        Wkr->>Stg: PUT {quality}.jpg
        Stg-->>Wkr: 200 OK
        Wkr->>DB: UPDATE item SET Status=Complete
    end
    Wkr->>DB: WorkerHeartbeatWriter.StampAsync<br/>(items processed, LastRanAt)

    Note over SPA,Wkr: Phase 3 — pre-signed URLs on demand
    SPA->>API: GET /api/photos/albums/{id}
    API->>DB: SELECT photos + queue progress
    loop per photo per quality
        API->>API: PhotoVersionUrlService.GetUrlAsync<br/>(cache hit?)
        alt cache miss
            API->>Stg: presign SAS
            Stg-->>API: signed URL
            API->>DB: INSERT/UPDATE PhotoVersionUrl
        end
    end
    API-->>SPA: 200 OK<br/>{ photos: [{ id, urls: {...} }] }
```

## Key points

* The API row insert and the SAS minting happen before the file leaves the SPA. The `Photo.ProcessingStatus = Uploading` row is the breadcrumb that `StorageConsistencyService` uses if the SPA dies after the PUT but before `upload-complete`.
* The Watermark queue item is enqueued in step 8 along with the four base qualities. It is the same shape as the others, but the worker handler produces two blobs (`thumbnail-watermarked.jpg` + `medium-watermarked.jpg`) in one run.
* The lease step uses an atomic `UPDATE-FROM-CTE` on SQL Server. Two workers cannot pick the same row. See `ProcessingQueueItemRepository.LeaseNextBatchAsync`.
* The URL cache (step "cache hit?") is per-process. Each API replica maintains its own. Cache TTL is bounded under the SAS expiry by `BlobStorage:UrlCacheSlidingMinutes` (default 30) capped at `BlobStorage:PublicUrlTtlMinutes` (default 60) minus a 3-minute safety margin.

## Failure modes covered by the recovery doc

* SPA dies between PUT and upload-complete.
* Worker dies mid-tick.
* Lease expires before the item completes.
* Original blob is missing when the worker tries to read it (chaos or manual).

See [../Architecture/01-Processing-Pipeline.md#failure-modes-and-how-the-pipeline-recovers](../Architecture/01-Processing-Pipeline.md#failure-modes-and-how-the-pipeline-recovers) for the full table.

## When to update

* Any change to the upload protocol (upload-tickets, upload-complete).
* Any change to the queue-item lifecycle that affects the worker loop.
* Any change to the pre-signed URL minting strategy.
