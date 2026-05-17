# 04 — Photo Download Sequence

End-to-end sequence for both download paths: authenticated user from their own album, and unauthenticated visitor through an access code.

## Authenticated user (own album)

```mermaid
sequenceDiagram
    autonumber
    participant SPA as Angular SPA<br/>(authenticated)
    participant API as API replica
    participant DB as SQL Server
    participant Cache as URL cache (in-process)
    participant Stg as Object Storage

    SPA->>API: GET /api/photos/albums/{id}<br/>Authorization: Bearer <JWT>
    API->>API: parse JWT, check role
    API->>DB: SELECT photos in album where owner matches OR role=Admin
    DB-->>API: photos[]

    loop per photo per quality
        API->>Cache: GetPhotoVersionUrlAsync(photoId, quality)
        alt cache hit
            Cache-->>API: signed URL
        else cache miss
            API->>Stg: presign SAS (TTL = PreSignedUrlTTLDays)
            Stg-->>API: signed URL
            API->>DB: UPSERT PhotoVersionUrl
            API->>Cache: store with sliding TTL
        end
    end

    API-->>SPA: 200 OK<br/>{ photos: [{ id, urls: { thumbnail, low, medium, high, original } }] }

    SPA->>Stg: GET signed URL (direct)
    Stg-->>SPA: image bytes
```

## Access-code visitor (anonymous)

```mermaid
sequenceDiagram
    autonumber
    participant Vis as Visitor browser
    participant API as API replica
    participant DB as SQL Server
    participant Cache as URL cache
    participant Stg as Object Storage

    Vis->>API: GET /code/{code}<br/>(no auth)
    API->>DB: SELECT AccessCode WHERE Code=? AND IsActive AND NOT IsDeleted AND ExpiresAt > now
    alt code invalid or expired
        API-->>Vis: 404 / 410
    else valid
        API->>DB: SELECT album + photos
        DB-->>API: photos[]
        API->>DB: INSERT UserAccessLog (UserId=null, AccessCodeId, ip, userAgent)

        loop per photo
            API->>Cache: get watermarked thumbnail URL (short TTL)
            alt miss
                API->>Stg: presign SAS for thumbnail-watermarked.jpg<br/>(TTL = PublicUrlTtlMinutes, default 60)
                Stg-->>API: signed URL
                API->>Cache: store
            end
        end

        API-->>Vis: 200 OK<br/>{ album, photos: [{ id, thumbnailUrl (watermarked) }] }
    end

    Vis->>Stg: GET signed URL (direct)
    Stg-->>Vis: watermarked thumbnail bytes

    Note over Vis,Stg: Modal click → same flow,<br/>watermarked medium URL
```

## Download to disk (cart checkout)

```mermaid
sequenceDiagram
    autonumber
    participant SPA as SPA (auth)
    participant API as API replica
    participant DB as SQL Server
    participant Zip as CartZipService
    participant Stg as Object Storage

    SPA->>API: POST /api/cart/download (cart item ids)
    API->>DB: SELECT cart items + photos + access permissions
    DB-->>API: items[]

    API->>API: stream zip response
    loop per item
        API->>Stg: GET original.jpg (server-side, signed)
        Stg-->>API: bytes
        API->>Zip: add entry
        API->>DB: INSERT Download (UserId, PhotoId, Quality=Original, AccessCodeId=null)
    end
    API-->>SPA: 200 OK (zip stream)
```

## Key points

* The SPA never proxies image bytes through the API for thumbnail / preview rendering. It receives signed URLs and pulls bytes directly from storage.
* Cart download is the one exception. The zip is streamed server-side because we need to assemble multiple originals into one response and the storage backend does not zip natively.
* Watermarked variants only exist for thumbnail and medium. The public code-gallery never has access to the unwatermarked versions or to high/original.
* Every visitor request appends a row to `UserAccessLog`. The admin Visitors tab groups by IP + User-Agent to surface unique visitors.

## URL TTL summary

| Audience              | URL pattern                       | TTL                                                | Refresh strategy                                              |
| --------------------- | --------------------------------- | -------------------------------------------------- | ------------------------------------------------------------- |
| Authenticated user    | `original`, `low`, `medium`, `high` | `BlobStorage:PreSignedUrlTTLDays` (default 7 days) | `PhotoVersionUrlRefreshWorker` rotates inside the refresh window. |
| Access-code visitor   | `thumbnail-watermarked`, `medium-watermarked` | `BlobStorage:PublicUrlTtlMinutes` (default 60 min) | Per-request mint, in-process cache, no DB persistence.        |
| Cart originals        | server-side GET only              | none (server reads bytes)                          | n/a                                                           |

## When to update

* Any change to the URL cache TTL or refresh strategy.
* Any change to the access-code authorization model.
* Any new download endpoint that bypasses the existing paths.
