# 07 — Photo Upload Data Flow Diagram

Level-1 data flow diagram for the photo upload use case. Identifies the entities, the processes, the data stores, and the trust boundaries.

This is the diagram pattern owned by the security-reviewer agent. Use it as a starting point for threat-modeling new endpoints. See the `data-flow-diagram-security` skill for the canonical format.

## DFD

```mermaid
flowchart LR
  classDef ext fill:#f8e8c0,stroke:#b08020,stroke-width:1px,color:#000;
  classDef proc fill:#cce0ff,stroke:#2050a0,stroke-width:1px,color:#000;
  classDef store fill:#dff0d0,stroke:#308030,stroke-width:1px,color:#000;
  classDef boundary stroke-dasharray:4 3,stroke:#900,stroke-width:1px;

  User["User<br/>(authenticated photographer)"]:::ext

  subgraph TrustBoundary["Trust boundary: PhotoGallery system"]
    direction LR

    subgraph Edge["Edge (HTTPS, JWT)"]
      P1["P1<br/>POST /upload-tickets<br/>(validate role,<br/>mint write SAS)"]:::proc
      P2["P2<br/>POST /upload-complete<br/>(flip status,<br/>enqueue 5 items)"]:::proc
    end

    subgraph Background["Background (worker app)"]
      P3["P3<br/>PhotoProcessingWorker<br/>(lease, derive 6 variants,<br/>upload, mark complete)"]:::proc
      P4["P4<br/>StorageConsistencyService<br/>(reconcile DB↔storage)"]:::proc
    end

    D1[("D1: SQL Server<br/>Photos, ProcessingQueueItems,<br/>WorkerHeartbeats")]:::store
    D2[("D2: Object Storage<br/>photogallery/{album}/{photo}/*.jpg")]:::store
    D3[("D3: PhotoVersionUrls<br/>(cached pre-signed URLs)")]:::store
  end

  TrustBoundary:::boundary

  %% Inbound flows
  User -- "file metadata,<br/>auth cookie / JWT" --> P1
  User -- "PUT file bytes via SAS URL" --> D2
  User -- "upload-complete signal" --> P2

  %% API ↔ DB
  P1 -- "INSERT Photo (Uploading),<br/>read role/album ownership" --> D1
  P2 -- "UPDATE Photo (Pending),<br/>INSERT 5 ProcessingQueueItems" --> D1

  %% Worker flows
  P3 -- "lease ProcessingQueueItems<br/>(atomic claim)" --> D1
  P3 -- "GET original.jpg" --> D2
  P3 -- "PUT derived jpg" --> D2
  P3 -- "UPDATE item Status=Complete,<br/>UPDATE PhotoStatus" --> D1
  P3 -- "stamp heartbeat" --> D1

  %% URL minting (separate flow, simplified here)
  P1 -. "mint write SAS, no read on D3" .-> D2

  %% Reconciliation
  P4 -- "scan photos + items" --> D1
  P4 -- "list blobs" --> D2
  P4 -- "reset orphan items, flip Photo status" --> D1

  %% Optional cache writes (when API later returns read URLs, not part of upload)
  P3 -. "no direct write to D3" .-> D3
```

## Data elements

| Label | Element                                                          | Sensitivity                                                            |
| ----- | ---------------------------------------------------------------- | ---------------------------------------------------------------------- |
| `User` | Authenticated photographer (Admin or AlbumCreator role)         | Identity bound by JWT. Roles enforced server-side.                     |
| P1    | `PhotosController.RequestUploadTickets`                          | Issues write-only single-blob SAS. TTL bounded.                        |
| P2    | `PhotosController.UploadComplete`                                | Idempotent on `photoId`. Cannot promote photos owned by other users.   |
| P3    | `PhotoProcessingWorker` + `ImageProcessingService`               | No public ingress. Reads/writes via `IStorageProvider`.                |
| P4    | `StorageConsistencyService.RunOnceAsync` / `RunForAlbumAsync`    | Triggered by `AdminJobScheduler` and admin enqueue.                    |
| D1    | SQL Server (Azure SQL prod, Docker MSSQL local)                  | Workload Identity in Azure. Connection string in Key Vault.            |
| D2    | Object Storage (Azure Blob prod, MinIO local)                    | User-delegation SAS only in prod. Shared keys disabled.                |
| D3    | `PhotoVersionUrls` table (cached URLs)                           | Pre-signed URLs are bearer tokens. Bounded TTL.                        |

## Trust boundaries crossed

1. User → P1: TLS + JWT bearer. Role checked against `Authentication/AuthorizationPolicies`.
2. User → D2 (direct PUT via SAS): TLS + SAS bearer. SAS scope is write+create on a single blob, expires in `UploadTicketTtl`.
3. P3 → D2: Worker uses `DefaultAzureCredential` for Azure Blob (Workload Identity) or access key for MinIO.
4. P3 → D1: EF Core via connection string from Key Vault.

## Threats and mitigations

| Threat                                                       | Mitigation                                                                                       |
| ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| User uploads a non-image, gigantic file                       | SAS is single-blob, single-content-type scoped. Server-side size validation on `upload-complete`. Worker rejects on first decode.                                                                          |
| User uploads to another album they don't own                  | P1 checks `Album.OwnerId == user.Id OR user has Admin role` before minting SAS.                  |
| Replay of a captured SAS URL                                   | SAS expires in `UploadTicketTtl` (short). Subsequent attempts to re-use the same `photoId` get an `alreadyComplete` short-circuit response.                                                                  |
| Worker steals another worker's lease                          | Atomic CTE-based claim. The `LeaseExpiresAt` column is the lock.                                  |
| Storage drift (chaos, manual delete, partial restore)          | `StorageConsistencyService` reconciles. Missing original → Photo flipped to Failed after grace.   |
| Pre-signed URL leak                                           | Bounded TTL. Public visitor URLs use the short TTL path. Watermarked-only for code-gallery.       |
| Photo metadata injection                                       | `Metadata` column is opaque JSON. Never executed. Sanitized on read by the SPA when displayed.    |

## When to update

* New endpoint added to the upload flow.
* New data store written to during upload.
* New trust-boundary crossing introduced.
* Change to the SAS minting strategy (scope, TTL, auth model).
