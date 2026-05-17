# 02 — AdminJob Queue

The mechanism that keeps long-running admin work off the API request thread. The API enqueues a row, a worker drains it. This document covers the entity, the dispatcher, the scheduler, and the deduplication contract.

## Why

Before the queue, admin clicks like "Reap orphan blobs" ran synchronously on the API. On a 0.5 vCPU container with 1500+ photos that was 30+ seconds of CPU starvation while the API also tried to serve client requests. The workers, meanwhile, polled the storage backend on their own timers, doubling up. The queue collapses both surfaces into one channel and lets workers scale independently of the API.

## Entity

`PhotoGallery/Models/AdminJob.cs`. One row per scheduled or admin-triggered task.

| Column                  | Type        | Notes                                                                  |
| ----------------------- | ----------- | ---------------------------------------------------------------------- |
| `Id`                    | Guid        | PK.                                                                    |
| `JobType`               | string      | One of `AdminJobTypes.*`. See the matrix below.                        |
| `AlbumId`               | Guid?       | Required for `ReconcileAlbumStorage`, null otherwise.                  |
| `Status`                | string      | `pending` → `running` → `complete` or `error`.                          |
| `RequestedAt`           | UTC         | Set by `EnqueueAdminJobAsync`.                                         |
| `RequestedBy`           | string?     | User email, or `system:scheduler` for routine maintenance.             |
| `StartedAt`             | UTC?        | Set by the dispatcher when it claims the row.                          |
| `CompletedAt`           | UTC?        | Set when the dispatcher finishes (success or error).                   |
| `CompletedByInstanceId` | string?     | Hostname of the worker replica that drained the row.                   |
| `ResultJson`            | string?     | Per-job report (e.g. `ConsistencyReport`, `OrphanReapReport`).         |
| `ErrorMessage`          | string?     | Truncated to 2048 chars.                                               |

## Job types

| `JobType`                  | Drained by                  | Service called                  | Notes                                                                          |
| -------------------------- | --------------------------- | ------------------------------- | ------------------------------------------------------------------------------ |
| `reconcile-storage`        | `StorageConsistencyWorker`  | `StorageConsistencyService`     | Walks every photo, reconciles DB ↔ storage divergence.                         |
| `reconcile-album-storage`  | `StorageConsistencyWorker`  | `StorageConsistencyService`     | Same as above but scoped to one album. Requires `AlbumId`.                     |
| `reap-orphans`             | `OrphanedBlobReaperWorker`  | `OrphanedBlobReaperService`     | Deletes blobs in storage whose Photo row is gone. Protected by a grace window. |
| `purge-failed-photos`      | `StorageConsistencyWorker`  | `FailedPhotoPurgeService`       | Hard-deletes Photo rows with `ProcessingStatus = Failed` and all dependents.   |
| `chaos-storage`            | `StorageConsistencyWorker`  | `ChaosStorageService`           | Dev/Trial only. Deletes random blobs to validate reconcile + reap.             |

## Lifecycle

```
+-----------+   API enqueue           +-----------+
|  Pending  | <---  POST /admin/jobs  +-----------+
|           | <---  AdminJobScheduler (API replica) every N hours
+-----------+
      |
      | dispatcher claim
      ▼
+-----------+
|  Running  |   StartedAt set, CompletedByInstanceId set
+-----------+
      |
      ▼
+-----------+               +-----------+
|  Complete |   or          |   Error   |
+-----------+               +-----------+
ResultJson set              ErrorMessage set
CompletedAt set             CompletedAt set
```

The FE polls `GET /api/photos/admin/jobs/{id}` every 2 seconds with a 5-minute cap. The page also lists recent jobs through `GET /api/photos/admin/jobs?page=&pageSize=&sortBy=&sortDir=&status=&jobType=`.

## Enqueue contract

`PhotosController.EnqueueAdminJobAsync(jobType, albumId)`:

1. Looks for an existing row with the same `(JobType, AlbumId)` that is `Pending` or `Running`.
2. If found, returns `202 Accepted` with the existing `jobId` and `deduped: true`. Nothing is inserted.
3. Otherwise inserts a new row, returns `202 Accepted` with the new `jobId`.

This is what makes the queue safe under double-clicks, multi-replica API hosts, and scheduler retries. The scheduler can fire its hourly tick on every replica and only one row per cycle survives.

For chaos jobs the controller additionally checks `Chaos:Enabled` and returns `403 Forbidden` upfront when the flag is off, instead of silently queuing a no-op.

## Dispatcher

`PhotoGallery/Services/AdminJobDispatcher.cs`. Each worker calls `DrainAsync(string[] jobTypes, ct)` at the top of its tick.

```csharp
var processed = 0;
for (var i = 0; i < 5; i++)
{
    var job = await TryClaimAsync(jobTypes, ct);   // atomic claim
    if (job is null) break;
    await RunOneAsync(job, ct);                     // routes by JobType
    processed++;
}
return processed;
```

* Bounded loop (max 5 per tick) so one runaway admin click cannot burn the whole tick.
* The claim is atomic: a single `UPDATE-FROM-CTE` on SQL Server flips one row's `Status` from `pending` to `running` and stamps `StartedAt` + `CompletedByInstanceId` (the InMemory test provider has a best-effort fallback).
* Routing is a single switch on `JobType` (see the matrix above).
* Errors are caught per-job, written to `ErrorMessage`, never propagated. The next job runs.

## Scheduler

`PhotoGallery/Services/AdminJobScheduler.cs`. Hosted only on the API replica (`WorkersEnabled=false` path in `Program.cs`).

| What it enqueues       | Cadence (default)                            |
| ---------------------- | -------------------------------------------- |
| `reconcile-storage`    | `Workers:Scheduler:ReconcileIntervalHours` (1 h) |
| `reap-orphans`         | `Workers:Scheduler:ReapIntervalHours` (6 h)  |

The scheduler reads both intervals at runtime through `ISettingsResolver` on every poll so changes take effect on the next tick. It relies on enqueue idempotency: if a worker is slow and the previous reconcile is still `Pending` or `Running`, the new attempt is deduped.

Running the scheduler only on API replicas is a deliberate choice. It keeps the routine workload visible in one place (the audit trail on `RequestedBy = "system:scheduler"` rows always comes from the API). With idempotent enqueues it would still be safe to run on every replica, but the log noise would multiply.

## Where this could go

The current implementation is "the database is the queue". This is fine at our scale. The interface to migrate to a real broker later is small.

Two future paths:

1. Swap the underlying claim shape to use [Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/) or [Azure Storage Queues](https://learn.microsoft.com/en-us/azure/storage/queues/). The `AdminJobDispatcher` becomes a thin adapter. The job-type → service routing stays. The `AdminJob` table becomes the audit log instead of the queue.
2. If at some point a worker needs to handle thousands of jobs per minute, introduce `IQueue<T>` per the `queue-provider-abstraction` skill. Today this is overkill.

Either move would be a single PR. The queue model is currently the right shape for this codebase.

## Admin UX

The Service Health admin tab has three things tied to the queue:

* The Admin job queue table. Server-paginated, server-sortable. Default sort: `RequestedAt desc`.
* Per-row delete (`DELETE /api/photos/admin/jobs/{id}`). Confirms before cancelling pending/running rows.
* The Enqueue form. Manual fire of any allowed job type. Confirms for destructive types (`purge-failed-photos`, `chaos-storage`).

## Where to read next

* The processing pipeline this queue protects: [01-Processing-Pipeline.md](01-Processing-Pipeline.md).
* Hot-reloading the scheduler cadences: [04-Runtime-Settings.md](04-Runtime-Settings.md).
* Sequence diagram: [../Diagrams/05-Sequence-Admin-Job-Flow.md](../Diagrams/05-Sequence-Admin-Job-Flow.md).
