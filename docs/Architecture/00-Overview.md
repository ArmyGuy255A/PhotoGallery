# 00 — System Overview

PhotoGallery is a photo-sharing application with a hardened processing pipeline. This document names the runtime processes, the top-level components, and the principles every other doc in this set assumes.

## Runtime processes

In production there are two Azure Container Apps. Both run the same image. The difference is one environment variable.

| Process     | Image    | `WorkersEnabled` | Role                                                                                            |
| ----------- | -------- | ---------------- | ----------------------------------------------------------------------------------------------- |
| API replica | same     | `false`          | Serves HTTP, mints upload SAS URLs, enqueues admin jobs, runs the `AdminJobScheduler`.          |
| Worker replica | same  | `true`           | Runs the four `BackgroundService` workers. No ingress. Scales horizontally under load via KEDA. |

The API has `min=max=1` replicas because the in-process SignalR hub (`PhotoProgressHub`) holds connected WebSocket clients in memory. The worker app scales freely.

Locally the same split runs as two VS Code tasks. See `.vscode/tasks.json` (`Backend: Run (Port 5105)` for the API, `Backend: Run as Worker` for a worker).

## Top-level components

```
HTTP clients (Angular SPA + access-code visitors)
        │
        ▼
+-----------------------+         +-----------------------+
|  API replica          |         |  Worker replica(s)    |
|  ASP.NET Core 9       |         |  ASP.NET Core 9       |
|  Controllers          |         |  BackgroundServices   |
|  AdminJobScheduler    │◀──────▶│  AdminJobDispatcher   |
|  SignalR hub          |   DB   |  PhotoProcessing      |
|                       |   poll |  StorageConsistency   |
|                       |        |  OrphanedBlobReaper   |
|                       |        |  PhotoVersionUrlRefresh|
+-----------┬-----------+         +-----------┬-----------+
            │                                 │
            ▼                                 ▼
+-----------------------------------------------------------+
|  SQL Server (Azure SQL in prod, Docker MSSQL locally)     |
|  PhotoGalleryDev DB                                       |
+-----------------------------------------------------------+
            ▲                                 ▲
            │                                 │
+-----------------------------------------------------------+
|  Object storage (MinIO locally, Azure Blob in prod)       |
|  bucket/container: photogallery/                          |
+-----------------------------------------------------------+
```

The components are shown more precisely in [../Diagrams/01-High-Level-Architecture.md](../Diagrams/01-High-Level-Architecture.md).

## Solution layout

The codebase is currently a single ASP.NET host project with two small library projects beside it:

| Project              | Type            | Holds                                                              |
| -------------------- | --------------- | ------------------------------------------------------------------ |
| `PhotoGallery`       | `Microsoft.NET.Sdk.Web` | Controllers, entities, services, repositories, EF Core context, workers, migrations. |
| `Authentication`     | library         | JWT issuance, Google OAuth wiring, role helpers.                   |
| `Configuration`      | library         | Strongly-typed `ConfigurationSettings` reader.                     |
| `PhotoGallery.Tests` | xUnit test      | All test classes for both library projects and the host.           |

Frontend lives in `FE.PhotoGallery/` (Angular 19 + CoreUI Pro 5).

### Known architectural debt

This is not yet Clean Architecture. Entities, repositories, and domain services all live in the host project. The target layout is `Domain` / `Application` / `Infrastructure` / `Presentation` with the host as a thin composition root. Tracked under the `csharp-solution-structure` rule.

When you add a new domain type (entity, repository, domain service) prefer extracting a library project rather than adding to `PhotoGallery/`. New library projects are the path to the target layout.

## Principles

These are the rules every other doc in this set assumes. They are codified in `MEMORY.md` with full rationale.

### 1. The API never does long work synchronously

Anything that can take more than a few seconds is queued. The API enqueues an `AdminJob` row and returns `202 Accepted` with a job id. A worker picks it up. Details in [02-AdminJob-Queue.md](02-AdminJob-Queue.md).

### 2. The database is the queue

There is no Service Bus, no Storage Queues, no RabbitMQ. The `AdminJob` table is the queue. The `ProcessingQueueItem` table is the per-quality work queue. Both are polled. This costs nothing extra in DTU terms at our scale and avoids the operational footprint of a separate broker. The trade-off and the path to a real broker are noted in [02-AdminJob-Queue.md](02-AdminJob-Queue.md).

### 3. Storage is abstracted behind `IStorageProvider`

MinIO locally, Azure Blob in prod. The interface is the seam. New cross-cutting infrastructure (queues, blob, db) follows the same provider-abstraction shape. Details in [06-Storage-Abstraction.md](06-Storage-Abstraction.md).

### 4. Settings are hot-reloadable when they should be

Every operationally-mutable setting (intervals, thresholds, feature flags) lives in `SettingsCatalogue` and is read at call time through `ISettingsResolver`. Admins change them on the Runtime Settings tab. Workers pick the change up on their next tick. Details in [04-Runtime-Settings.md](04-Runtime-Settings.md).

### 5. Workers identify themselves through heartbeats

Every worker writes to `WorkerHeartbeats` so the API replica can show a replica-centric view of who is running, what they are doing, and whether they are alive. Details in [05-Worker-Heartbeats.md](05-Worker-Heartbeats.md).

### 6. Reconciliation, not coordination

There is no distributed-transaction layer between DB and storage. Each side is the source of truth for its own state. `StorageConsistencyService` periodically reconciles divergence by treating the storage backend as authoritative for blob presence and the queue items as authoritative for derivation work. Same approach for orphaned blobs and Failed photo rows.

### 7. Three-tier lifecycle

Every design has to run in three places: laptop (Docker compose: MSSQL, MinIO), Trial (Azure SQL + Azure Blob), Production (same Azure shape, hardened). Designs that work in only one tier are incomplete.

## Where to read next

* The pipeline: [01-Processing-Pipeline.md](01-Processing-Pipeline.md).
* The diagrams: [../Diagrams/01-High-Level-Architecture.md](../Diagrams/01-High-Level-Architecture.md).
* The historical context: `MEMORY.md` (repo root).
