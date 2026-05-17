# 08 — Photo Download Data Flow Diagram

Level-1 data flow diagram for the photo download use cases. Covers both the authenticated path (album owner / admin downloads any quality) and the unauthenticated path (access-code visitor sees watermarked variants only).

This diagram pattern is owned by the security-reviewer agent. See `data-flow-diagram-security` skill.

## DFD

```mermaid
flowchart LR
  classDef ext fill:#f8e8c0,stroke:#b08020,stroke-width:1px,color:#000;
  classDef proc fill:#cce0ff,stroke:#2050a0,stroke-width:1px,color:#000;
  classDef store fill:#dff0d0,stroke:#308030,stroke-width:1px,color:#000;
  classDef boundary stroke-dasharray:4 3,stroke:#900,stroke-width:1px;

  Owner["Authenticated user<br/>(Admin or AlbumCreator)"]:::ext
  Visitor["Code-gallery visitor<br/>(anonymous, owns the code)"]:::ext

  subgraph TrustBoundary["Trust boundary: PhotoGallery system"]
    direction LR

    subgraph Edge["Edge (HTTPS)"]
      P1["P1<br/>GET /api/photos/albums/{id}<br/>(JWT required,<br/>full URLs)"]:::proc
      P2["P2<br/>GET /code/{code}<br/>(no auth,<br/>watermarked URLs)"]:::proc
      P3["P3<br/>POST /api/cart/download<br/>(JWT,<br/>server-side zip)"]:::proc
      P4["P4<br/>PhotoVersionUrlService<br/>(cache + mint SAS)"]:::proc
    end

    subgraph Background["Background"]
      P5["P5<br/>PhotoVersionUrlRefreshWorker<br/>(rotate URLs near expiry)"]:::proc
    end

    D1[("D1: SQL Server<br/>Photos, Albums, AccessCodes,<br/>UserAccessLog, Downloads")]:::store
    D2[("D2: Object Storage<br/>original / variants / watermarked")]:::store
    D3[("D3: PhotoVersionUrls<br/>(persisted cached URLs)")]:::store
    D4[("D4: In-process cache<br/>(short-lived public URLs)")]:::store
  end

  TrustBoundary:::boundary

  %% Authenticated owner
  Owner -- "JWT, album id" --> P1
  P1 -- "check ownership / role" --> D1
  P1 -- "GetPhotoVersionUrlAsync per quality" --> P4
  P4 -- "lookup cache" --> D3
  P4 -- "presign SAS on miss" --> D2
  P4 -- "store" --> D3
  P4 -- "signed URLs" --> P1
  P1 -- "JSON { urls: ... }" --> Owner
  Owner -- "GET via signed URL" --> D2

  %% Cart download
  Owner -- "JWT, item ids" --> P3
  P3 -- "ownership + access checks" --> D1
  P3 -- "GET originals (server-side)" --> D2
  P3 -- "INSERT Downloads (UserId, PhotoId, Quality=Original)" --> D1
  P3 -- "zip stream" --> Owner

  %% Visitor (anonymous)
  Visitor -- "code" --> P2
  P2 -- "validate AccessCode (active, not expired, not deleted)" --> D1
  P2 -- "INSERT UserAccessLog (UserId=null, ip, ua)" --> D1
  P2 -- "GetWatermarkedThumbnailUrl per photo" --> P4
  P4 -- "in-process cache (short TTL)" --> D4
  P4 -- "presign SAS on miss, TTL = PublicUrlTtlMinutes" --> D2
  P4 -- "watermarked URLs" --> P2
  P2 -- "JSON { album, photos: [{ thumbnailUrl }] }" --> Visitor
  Visitor -- "GET via signed URL" --> D2

  %% URL refresh
  P5 -- "scan urls near expiry" --> D3
  P5 -- "presign new SAS" --> D2
  P5 -- "UPDATE" --> D3
```

## Data elements

| Label | Element                                                       | Sensitivity                                                              |
| ----- | ------------------------------------------------------------- | ------------------------------------------------------------------------ |
| Owner | Authenticated user, role = Admin or AlbumCreator              | Identity in JWT. Owner check on album.                                   |
| Visitor | Anonymous, holds an `AccessCode` string                     | No identity. Auditability via IP + User-Agent in `UserAccessLog`.        |
| P1    | `PhotosController.GetAlbumPhotos`                             | Returns full-quality URLs to authorized users only.                      |
| P2    | `AccessCodeController.GetByCode`                              | Returns watermarked thumbnail URLs only.                                 |
| P3    | `CartController.Download` / `CartZipService`                  | Streams a zip of originals. Records each download.                       |
| P4    | `PhotoVersionUrlService.GetPhotoVersionUrlAsync`              | The seam between consumers and the storage provider.                     |
| P5    | `PhotoVersionUrlRefreshWorker`                                | Background; rotates persisted URLs within `PreSignedUrlRefreshWindowDays`.|
| D1    | SQL Server                                                    | Workload Identity + Key Vault in Azure.                                  |
| D2    | Object Storage                                                | SAS-only access for clients. Server-side `DefaultAzureCredential` for the API and workers in Azure. |
| D3    | `PhotoVersionUrls` table                                      | Persisted long-lived URLs. Rotated by P5.                                |
| D4    | In-process LRU cache                                          | Short-lived URLs for public visitors. Per-replica.                       |

## Trust boundaries crossed

1. Owner / Visitor → P1/P2/P3 (TLS, JWT or unauthenticated).
2. P4 → D2 (mint SAS, requires API credential).
3. Owner / Visitor → D2 (direct GET via SAS, no API involvement).
4. P3 → D2 (server-side GET for cart, requires API credential).

## Authorization model

| Endpoint                              | Auth                                        | Visibility                                                            |
| ------------------------------------- | ------------------------------------------- | --------------------------------------------------------------------- |
| `GET /api/photos/albums/{id}`         | JWT, `Album.OwnerId == user.Id OR Admin`    | All five base qualities + original + watermarked.                     |
| `GET /code/{code}`                    | None                                        | Only watermarked thumbnails. Modal also has watermarked medium.       |
| `POST /api/cart/download`             | JWT, must own each cart item's source album or have an active access code | Original quality only, streamed as zip.                               |
| `POST /api/photos/admin/*`            | JWT, `Admin` role                           | Admin endpoints (reconcile, reap, purge, chaos). See `ADMIN_JOBS`.    |

## Threats and mitigations

| Threat                                                  | Mitigation                                                                                |
| ------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| Code-gallery visitor sees the full-quality original     | P2 never returns URLs for non-watermarked variants. Authorization enforced at the URL-minting step. |
| Captured SAS URL replayed indefinitely                  | Bounded TTL on every URL. Visitor URLs in particular are short (default 60 min).          |
| Visitor accesses other albums' photos by guessing code  | `AccessCodes.Code` is high-entropy random. Expired + deleted codes return 404/410.        |
| Cart download bypasses access checks                    | P3 re-validates ownership / access for every item before adding to the zip.               |
| Visitor identity spoofed                                | Visitors are intentionally anonymous. The IP + UA in `UserAccessLog` is best-effort audit, not authoritative identity. |
| Watermark stripped from downloaded thumbnail            | Watermarking happens server-side in `ImageProcessingService`. The watermarked variant is a separate blob; the original is not exposed.|
| Pre-signed URL leak                                     | TTL bounded. URL cache TTL ≤ SAS TTL minus 3 min safety margin.                            |

## When to update

* New visibility tier (e.g. preview-quality URL for a third audience).
* New caching layer between P4 and D2.
* Change to the access-code authorization model (expiration, scope).
* New download endpoint or new cart shape.
