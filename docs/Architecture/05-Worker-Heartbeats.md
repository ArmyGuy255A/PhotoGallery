# 05 — Worker Heartbeats

How the API replica knows what every worker replica is doing, even though they run as separate Container Apps with no shared in-process state. This is the foundation of the Service Health admin page.

## The shape

```
+--------------------+         +--------------------+
|  API replica       |         |  Worker replica N  |
|                    |         |                    |
|  GET /service-health        |    every worker tick:
|  reads WorkerHeartbeats     |    WorkerHeartbeatWriter.StampAsync(...)
+---------+----------+         +----------+---------+
          │ SELECT                       │ INSERT or UPDATE
          ▼                              ▼
+----------------------------------------------------+
|  WorkerHeartbeats table                            |
|  unique on (WorkerName, InstanceId)                |
+----------------------------------------------------+
```

Every worker on every replica owns exactly one row in `WorkerHeartbeats`. The row is upserted on each tick. The API reads the table to answer "who is alive and what are they doing".

## Entity

`PhotoGallery/Models/WorkerHeartbeat.cs`. Unique index on `(WorkerName, InstanceId)`.

| Column                  | Notes                                                                              |
| ----------------------- | ---------------------------------------------------------------------------------- |
| `WorkerName`            | Stable name, e.g. `PhotoProcessing`, `OrphanedBlobReaper`.                         |
| `InstanceId`            | `CONTAINER_APP_REPLICA_NAME` in ACA. `Environment.MachineName` locally.            |
| `DisplayName`           | Human label for the dashboard.                                                     |
| `IntervalSeconds`       | The worker's current tick interval. The dashboard uses 2× this for "alive".        |
| `LastHeartbeatAt`       | UTC of the most recent stamp.                                                      |
| `LastRanAt`             | UTC of the most recent successful tick (`RecordTick`).                             |
| `ItemsProcessedTotal`   | Running total since the replica started. Bumped via `IncrementProcessed`.          |
| `ItemsInFlight`         | Size of the batch in flight right now.                                             |
| `LastError`             | Sticky breadcrumb. Cleared on the next successful stamp.                           |
| `CpuPercent`            | Process CPU across all cores at heartbeat time. Null on the first stamp.           |
| `WorkingSetBytes`       | Process working set (RAM the OS sees the process using).                           |
| `ManagedHeapBytes`      | .NET managed heap.                                                                  |

## Writer

`PhotoGallery/Services/WorkerHeartbeatWriter.cs`. Singleton.

```csharp
await hb.StampAsync(
    workerName:   WorkerName,
    displayName:  "Storage ↔ DB consistency",
    interval:     tickInterval,
    lastRanAt:    DateTime.UtcNow,
    cancellationToken,
    itemsInFlight: 0,
    lastError:    null);
```

Three behaviors worth knowing:

* **CPU sampling needs two reads.** `CpuPercent` is null on the very first stamp from a process, because we need two `TotalProcessorTime` snapshots to compute a delta. Subsequent stamps populate it.
* **Heartbeat throttling.** Workers only stamp when they did work, or when more than 30 s have passed since the last stamp. Cuts idle DB write load roughly 6×.
* **Initial heartbeat on registration.** Every worker stamps once before its first tick so the dashboard shows the replica immediately, not after one full tick interval.

`IncrementProcessed(workerName, delta)` is a per-process counter in a `ConcurrentDictionary`. The next stamp ships its value. This avoids a DB read-modify-write on every photo processed.

## Reader

`PhotoGallery/Controllers/AdminController.cs`, `GetServiceHealth`. Reads the table once per request and builds a replica-centric view.

```
WorkerHeartbeats (flat)
        │
        │ GROUP BY InstanceId
        ▼
Replica[]               ← one row per hostname
  ├── role: api-only | worker | api+worker
  ├── isAlive: bool
  ├── lastHeartbeatAt: timestamp
  └── jobs: Job[]        ← the workers on this replica
        ├── name, displayName, interval, lastRanAt, nextRunAt
        ├── itemsProcessedTotal, itemsInFlight, lastError
        └── cpuPercent, workingSetBytes, managedHeapBytes
```

## Thresholds

`PhotoGallery/Services/WorkerHeartbeatThresholds.cs`. Single source of truth for both the writer and the reader.

| Constant          | Default | Meaning                                                                 |
| ----------------- | ------- | ----------------------------------------------------------------------- |
| `OfflineAfter`    | 2 min   | A heartbeat older than this is considered offline.                      |
| `PruneAfter`      | 30 min  | Heartbeat rows older than this are deleted on the next prune cycle.    |

Three lifecycle states for a heartbeat row:

| State    | When                                                  | UI                                                                  |
| -------- | ----------------------------------------------------- | ------------------------------------------------------------------- |
| Alive    | last stamp within `OfflineAfter`                       | Green dot, normal text. Job list shown.                             |
| Offline  | older than `OfflineAfter`, younger than `PruneAfter`   | Dimmed row, hostname struck-through. Job list still shown.          |
| Gone     | older than `PruneAfter`                                | Deleted from the table on the next prune cycle. No longer rendered. |

Replicas are intentionally ephemeral. KEDA spawns workers under load, ACA kills them when traffic dies. The 30-minute prune window is long enough to investigate "what killed it" and short enough that scaling churn does not pile up ghost rows.

## Prune

Each call to `StampAsync` checks a throttled `_lastPruneAt` (once per minute across the whole process). When it is due, the writer deletes every `WorkerHeartbeat` row with `LastHeartbeatAt < now - PruneAfter`.

This is best-effort. If pruning fails, the next call retries. The dashboard's offline-detection logic does not depend on pruning, so a missed prune just leaves the row in the Offline state for an extra cycle.

## How the replica name is set

| Environment | Source                                            | Example                                                          |
| ----------- | ------------------------------------------------- | ---------------------------------------------------------------- |
| Local API   | `CONTAINER_APP_REPLICA_NAME` env var (`local-api`) | `local-api`                                                      |
| Local worker | Same env var, `local-worker-{suffix}`              | `local-worker-1`, `local-worker-2`                               |
| ACA         | ACA sets it for every replica                      | `ca-photogallery-worker-dev--0000003-7c5c996454-xf65m`           |
| Fallback    | `Environment.MachineName`                          | The dev box hostname.                                            |

The VS Code task `Backend: Run as Worker` prompts for a suffix so two worker terminals show as two distinct rows on the dashboard.

## Capacity metrics

`CpuPercent`, `WorkingSetBytes`, `ManagedHeapBytes` are sampled from `Process.GetCurrentProcess()` on every stamp. They are bounded:

* `CpuPercent` divided by `Environment.ProcessorCount` so a single-core-pegged process reads 100, not 100 × N.
* Clamped to [0, 100].
* Null on the very first heartbeat from a process (no delta yet).

The dashboard shows these per-worker so admins can spot CPU saturation or memory pressure at a glance. Tuning `PhotoProcessing:WorkerParallelism` from 5 to 2 is the typical response on a 0.5 vCPU container.

## What this avoids

* A separate distributed tracing tool for the basic "is the worker running" question. App Insights and Container Apps logs are still there for deep diagnostics. Heartbeats are the cheap fast-path signal.
* A control plane. Workers do not call the API to register. They write a row. The API reads the table. No coupling, no port.
* In-process registries shared between replicas. The `WorkerScheduleRegistry` is per-process, the heartbeat table is the shared state.

## Where to read next

* The Service Health page that consumes this: [../Diagrams/01-High-Level-Architecture.md](../Diagrams/01-High-Level-Architecture.md) for the data flow.
* The sequence: [../Diagrams/06-Sequence-Worker-Heartbeat.md](../Diagrams/06-Sequence-Worker-Heartbeat.md).
* Tunable cadences: [04-Runtime-Settings.md](04-Runtime-Settings.md).
