# 05 — Admin Job Flow Sequence

How an admin click becomes worker work. Two flows: admin-triggered, and scheduler-triggered. Both end in the same `AdminJobDispatcher.DrainAsync` call.

## Admin-triggered (manual)

```mermaid
sequenceDiagram
    autonumber
    participant Admin as Admin SPA
    participant API as API replica
    participant DB as SQL Server (AdminJobs)
    participant Wkr as Worker replica
    participant Svc as Domain Service<br/>(Reconcile / Reap / Chaos / Purge)

    Admin->>API: POST /api/photos/admin/jobs<br/>{ jobType: "reconcile-storage" }
    API->>DB: SELECT existing WHERE JobType=? AND (Pending OR Running)
    alt found existing
        API-->>Admin: 202 Accepted { jobId, deduped: true }
    else no existing
        API->>DB: INSERT AdminJob (Status=Pending, RequestedBy=admin@...)
        API-->>Admin: 202 Accepted { jobId, deduped: false }
    end

    Note over Admin,API: FE polls every 2 s
    Admin->>API: GET /api/photos/admin/jobs/{jobId}
    API->>DB: SELECT row
    API-->>Admin: { status: pending }

    Wkr->>Wkr: tick (every 10 s)
    Wkr->>DB: AdminJobDispatcher.TryClaimAsync<br/>(UPDATE FROM CTE: SET Status=running, StartedAt, CompletedByInstanceId)
    DB-->>Wkr: claimed row

    Wkr->>Svc: RunOnceAsync(ct)
    Svc->>DB: read state
    Svc->>Wkr: report (ConsistencyReport / OrphanReapReport / ChaosReport)

    Wkr->>DB: UPDATE row SET Status=complete, CompletedAt, ResultJson=...
    Wkr->>DB: WorkerHeartbeatWriter.IncrementProcessed(jobCount)

    Admin->>API: GET /api/photos/admin/jobs/{jobId}
    API-->>Admin: { status: complete, result: {...}, completedByInstanceId: "..." }
```

## Scheduler-triggered (routine maintenance)

```mermaid
sequenceDiagram
    autonumber
    participant Sched as AdminJobScheduler<br/>(API replica only)
    participant Cfg as ISettingsResolver
    participant DB as SQL Server
    participant Wkr as Worker replica

    loop every 1 min
        Sched->>Cfg: GetIntAsync("Workers:Scheduler:ReconcileIntervalHours", 1)
        Cfg-->>Sched: 1
        Sched->>Cfg: GetIntAsync("Workers:Scheduler:ReapIntervalHours", 6)
        Cfg-->>Sched: 6

        alt time since last reconcile >= interval
            Sched->>DB: any Pending/Running reconcile-storage?
            alt yes
                Sched-->>Sched: skip (dedupe)
            else no
                Sched->>DB: INSERT AdminJob (JobType=reconcile-storage, RequestedBy=system:scheduler)
            end
        end

        alt time since last reap >= interval
            Sched->>DB: any Pending/Running reap-orphans?
            alt yes
                Sched-->>Sched: skip
            else no
                Sched->>DB: INSERT AdminJob (JobType=reap-orphans, RequestedBy=system:scheduler)
            end
        end
    end

    Note over Sched,Wkr: Same drain flow as above

    Wkr->>DB: claim + run + complete
```

## Job-type routing inside the dispatcher

```mermaid
flowchart TB
    Drain["AdminJobDispatcher.DrainAsync(jobTypes)"]
    Claim["TryClaimAsync<br/>(atomic CTE update)"]
    Run["RunOneAsync"]
    Route{{"switch on JobType"}}
    Recon["StorageConsistencyService.RunOnceAsync"]
    ReconAlb["StorageConsistencyService.RunForAlbumAsync(albumId)"]
    Reap["OrphanedBlobReaperService.RunOnceAsync"]
    Chaos["ChaosStorageService.RunOnceAsync<br/>(refuses if Chaos:Enabled=false)"]
    Purge["FailedPhotoPurgeService.RunOnceAsync"]
    Complete["CompleteAsync<br/>(Status=complete, ResultJson)"]
    Error["CompleteAsync<br/>(Status=error, ErrorMessage)"]

    Drain --> Claim
    Claim --> Run
    Run --> Route
    Route -- reconcile-storage --> Recon
    Route -- reconcile-album-storage --> ReconAlb
    Route -- reap-orphans --> Reap
    Route -- chaos-storage --> Chaos
    Route -- purge-failed-photos --> Purge
    Recon --> Complete
    ReconAlb --> Complete
    Reap --> Complete
    Chaos --> Complete
    Purge --> Complete
    Recon -. throws .-> Error
    ReconAlb -. throws .-> Error
    Reap -. throws .-> Error
    Chaos -. throws .-> Error
    Purge -. throws .-> Error
```

## Key points

* The atomic claim is the only piece that needs to be exactly-once. Everything else is at-least-once with idempotent enqueue.
* Workers bound their drain at 5 jobs per tick so one runaway admin click cannot burn the whole tick.
* `StorageConsistencyWorker` drains five job types (`reconcile-storage`, `reconcile-album-storage`, `chaos-storage`, `purge-failed-photos`). `OrphanedBlobReaperWorker` drains one (`reap-orphans`). Routing is by the `jobTypes[]` argument to `DrainAsync`.
* The scheduler does not need a distributed lock. Idempotent enqueue means even if it ran on every replica the queue would still only get one row per cycle.

## When to update

* New `AdminJobType` constant. Update the dispatcher routing diagram.
* Change to the claim strategy (e.g. moving to a real broker).
* Change to scheduler cadence policy (e.g. adding a new routine job).
