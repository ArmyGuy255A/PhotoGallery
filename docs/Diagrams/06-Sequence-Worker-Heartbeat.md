# 06 — Worker Heartbeat Sequence

How a worker reports liveness, how the API replica reads it, how the dashboard renders the result.

## Write path (per worker tick)

```mermaid
sequenceDiagram
    autonumber
    participant Wkr as Worker (e.g. PhotoProcessingWorker)
    participant HB as WorkerHeartbeatWriter<br/>(singleton)
    participant DB as SQL Server

    Note over Wkr: registration<br/>(once at startup)
    Wkr->>HB: StampAsync(initial)
    HB->>DB: SELECT existing (WorkerName, InstanceId)
    HB->>DB: INSERT WorkerHeartbeat row
    HB->>HB: sample CpuPercent (null), WorkingSetBytes, ManagedHeapBytes
    HB->>DB: SaveChanges

    loop every tick (interval = ResolveTickIntervalAsync)
        Wkr->>Wkr: do work (lease items, process)
        Wkr->>HB: IncrementProcessed(n)<br/>(in-memory ConcurrentDictionary)

        alt drained > 0 OR > 30 s since last stamp
            Wkr->>HB: StampAsync(workerName, displayName,<br/>interval, lastRanAt, itemsInFlight, lastError)
            HB->>DB: SELECT existing
            HB->>HB: sample CPU% (delta since last sample),<br/>working set, managed heap
            HB->>DB: UPDATE row<br/>(LastHeartbeatAt, LastRanAt, ItemsInFlight,<br/>ItemsProcessedTotal, CpuPercent, WS, GC, LastError)
            HB->>DB: SaveChanges
        else idle and recent stamp
            Wkr-->>Wkr: skip stamp this tick
        end

        opt every minute (across the process)
            HB->>DB: DELETE WorkerHeartbeats WHERE LastHeartbeatAt < now - 30 min
        end
    end
```

## Read path (Service Health page)

```mermaid
sequenceDiagram
    autonumber
    participant Admin as Admin SPA
    participant API as API replica<br/>(AdminController)
    participant DB as SQL Server
    participant Reg as WorkerScheduleRegistry<br/>(in-process)

    Admin->>API: GET /api/admin/service-health
    API->>DB: SELECT * FROM WorkerHeartbeats
    API->>Reg: Snapshot() (this replica's in-memory workers)
    API->>API: merge:<br/>- local entries override their own heartbeat<br/>- remote entries appear as additional rows
    API->>API: group by InstanceId<br/>(replica-centric view)
    API->>API: compute IsAlive per replica =<br/>max(LastHeartbeatAt) >= now - 2 min
    API->>DB: derive Photos.Pending/Processing/Complete from ProcessingQueueItems<br/>(live, not Photo.ProcessingStatus)
    API->>API: list storage stats via IStorageProvider<br/>(30s in-process cache)
    API-->>Admin: ServiceHealthDto<br/>{ replicas, photos, queue, storage, workers, adminJobs }
```

## Liveness state machine

```mermaid
stateDiagram-v2
    [*] --> Alive : first StampAsync
    Alive --> Alive : tick, LastHeartbeatAt = now
    Alive --> Offline : > OfflineAfter (2 min)<br/>since last stamp
    Offline --> Alive : new StampAsync (replica recovered)
    Offline --> Gone : > PruneAfter (30 min)<br/>since last stamp
    Gone --> [*] : row deleted on next prune
```

## Notes

* `CpuPercent` is null on the very first heartbeat because two samples of `TotalProcessorTime` are required for a delta.
* The prune lock (`_lastPruneAt`) is process-level, not DB-level. Two replicas pruning at the same time race harmlessly: both will see the same set of stale rows, the SQL `DELETE` is idempotent.
* `WorkerScheduleRegistry` is per-process. It is the canonical "what is registered" view inside one replica. The DB heartbeat table is the canonical "what is registered across all replicas" view.
* The Service Health page merge step prefers the in-process registry data for the local replica (real-time, no DB read latency) and falls back to the DB heartbeat for all remote replicas.

## When to update

* Change to the prune cadence or retention.
* Change to the alive/offline thresholds (`WorkerHeartbeatThresholds`).
* New capacity metric added to the heartbeat row.
* Change to the heartbeat throttling rule.
