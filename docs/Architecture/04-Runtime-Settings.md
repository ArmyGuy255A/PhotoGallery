# 04 — Runtime Settings

How operationally-mutable settings work. Admin clicks a value on the Runtime Settings tab, a worker picks the change up on its next tick, no redeploy required. This document covers the catalogue, the resolver, the persistence layer, and the rules for adding a new tunable.

## The contract

Every operationally-mutable setting must satisfy three properties:

1. Declared in `SettingsCatalogue` with a key, category, data type, default, description, and a `RestartRequired` flag.
2. Read at call time through `ISettingsResolver`. Never cached in a constructor.
3. Falls back through three layers: the DB (`RuntimeSettings` table), then `IConfiguration` (appsettings + env vars + Key Vault), then a hard-coded default.

If your setting can be tuned in flight, it goes here. If it cannot be tuned in flight, set `RestartRequired = true` and document why.

## Where things live

```
PhotoGallery/Services/SettingsCatalogue.cs        # the declarations + ISettingsResolver interface
PhotoGallery/Services/SettingsResolver.cs         # DB-first → IConfiguration → fallback
PhotoGallery/Models/RuntimeSetting.cs             # entity (Key, Value, UpdatedAt, UpdatedBy)
PhotoGallery/Data/ApplicationDbContext.cs         # DbSet<RuntimeSetting>
PhotoGallery/Controllers/AdminController.cs       # GET/PUT /api/admin/settings
FE.PhotoGallery/.../admin-settings.component.ts   # the Settings tab UI
```

## Catalogue entry shape

```csharp
new SettingCatalogueEntry(
    Key:             "Workers:Scheduler:ReconcileIntervalHours",
    Category:        "Workers",
    DataType:        "int",                              // int | bool | double
    DefaultValue:    "1",
    Description:     "How often the API-side AdminJobScheduler enqueues a routine reconcile job (idempotently — duplicates are deduped). Hourly is fine for steady-state; lower if you want faster catch of corruption / chaos. Hot-reload — takes effect on the next scheduler tick.",
    RestartRequired: false);
```

The description is mandatory and shows up verbatim on the admin UI. Treat it as the user-facing manual entry, not as a code comment.

## Reading a setting

Always at call time, never in a constructor.

```csharp
// In a worker tick or a per-request scope:
using var scope = _serviceProvider.CreateScope();
var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();
var interval = TimeSpan.FromSeconds(
    Math.Max(1, await resolver.GetIntAsync(
        "Workers:StorageConsistency:TickIntervalSeconds",
        fallback: 10,
        ct)));
```

The `Math.Max(1, ...)` is defensive against an admin entering `0`.

`ISettingsResolver` exposes:

| Method                                              | Returns                                                          |
| --------------------------------------------------- | ---------------------------------------------------------------- |
| `GetAsync(key, ct)`                                 | Raw string. Null if neither DB nor IConfiguration has a value.   |
| `GetIntAsync(key, fallback, ct)`                    | int. Falls through string parse, then to the caller's fallback.  |
| `GetBoolAsync(key, fallback, ct)`                   | bool.                                                            |
| `GetDoubleAsync(key, fallback, ct)`                 | double. Invariant culture parse.                                 |

## Resolution order

```
ISettingsResolver.GetAsync("Foo:Bar")
        │
        ▼
+----------------------------------+
| RuntimeSettings table            |   admin override
+----------------------------------+
        │ miss
        ▼
+----------------------------------+
| IConfiguration["Foo:Bar"]        |   appsettings / env var / Key Vault
+----------------------------------+
        │ miss
        ▼
+----------------------------------+
| Caller-supplied fallback          |   the int/bool/double in the call
+----------------------------------+
```

This ordering matters. Admin changes win over appsettings, so a hot fix on the Runtime Settings tab takes effect without a redeploy. Appsettings still ship the defaults so a fresh install has sane behavior before the admin touches anything.

## Persisting an admin change

`PUT /api/admin/settings/{key}` with `{ value: "string-form" }`:

1. Controller looks up the catalogue entry. 404 if unknown key. 400 if `IsValid(value)` fails.
2. Controller upserts the `RuntimeSettings` row with the new value, `UpdatedAt = now`, `UpdatedBy = current user`.
3. The next call to `ISettingsResolver.GetAsync(key)` sees the new value. There is no cache to invalidate.
4. Workers that read their interval per tick will use the new value on the next tick.

`DELETE /api/admin/settings/{key}` removes the row, falling back to appsettings.

## Hot-reload semantics

Each setting in the catalogue has hot-reload semantics that you must respect when you consume it.

| Pattern                                          | Behavior                                                                                       |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------------- |
| Read inside the loop body (workers)               | Takes effect on the next iteration. Tick intervals, parallelism, kill switches.                |
| Read inside a per-request scope (controllers)     | Takes effect on the next request. Reaper grace, URL TTL.                                       |
| Read once at startup                              | Requires restart. Set `RestartRequired = true` in the catalogue entry.                         |
| Cached behind an in-process TTL                  | Takes effect on the next cache miss. Pre-signed URL cache TTL.                                 |

The Settings tab shows `RestartRequired` next to each value so admins know what to expect.

## Categories today

| Category    | Examples                                                                                       |
| ----------- | ---------------------------------------------------------------------------------------------- |
| Processing  | `PhotoProcessing:IntervalSeconds`, `WorkerParallelism`, `LeaseBatchMultiplier`, `ConsistencyCheck*` |
| Storage     | `Storage:OrphanReap*`, `BlobStorage:*`                                                          |
| Workers     | `Workers:StorageConsistency:TickIntervalSeconds`, `Workers:OrphanedBlobReaper:TickIntervalSeconds`, `Workers:Scheduler:Reconcile/ReapIntervalHours` |
| Chaos       | `Chaos:Enabled`, `Chaos:DeleteFraction`, `Chaos:MaxDeletionsPerRun`, `Chaos:IncludeOriginals`, `Chaos:IncludeDerivedVersions` |

Chaos has an additional rule: only honored in Trial. Production pins `Chaos:Enabled = false` in `appsettings.Production.json` and the controller refuses chaos enqueues with `403 Forbidden` upfront when the flag is off. See [01-Processing-Pipeline.md](01-Processing-Pipeline.md) and `MEMORY.md` for the rationale.

## Adding a new setting: checklist

1. Pick a key. `Category:Sub:Property` style. Keys are case-insensitive but write them PascalCase per segment.
2. Pick a data type. `int`, `bool`, `double`. Strings would be allowed but are very rare in this codebase.
3. Add the entry to `SettingsCatalogue.Items`. Include a real description (not "TODO").
4. Add a sane default in `appsettings.Development.json`. Add the appropriate value in `appsettings.Trial.json` and `appsettings.Production.json` if it differs by environment.
5. Read it through `ISettingsResolver.GetXAsync(key, hardcodedFallback, ct)`. The hard-coded fallback should equal the appsettings default so removing the DB row and the appsettings entry both result in safe behavior.
6. If the setting governs something the admin should never touch in prod (a debug flag, chaos), explicitly note that in the description. Consider gating with an environment check (see Chaos).
7. The Settings tab UI is generated from the catalogue. No FE change is needed for a new entry.

## What this avoids

* `IOptions<T>` reload semantics. The pattern works but the FE wants string round-trips and the catalogue needs to be the source of truth. A flat `RuntimeSetting(key, value)` table is simpler.
* Per-setting feature-flag systems. Adding a knob is one line in the catalogue.
* Cache invalidation. There is no cache to invalidate. Every read hits the DB. At our scale this is a sub-1ms query on a tiny table.

## Where to read next

* How workers actually read their tick interval: [03-Service-Patterns.md](03-Service-Patterns.md).
* The chaos setup as a worked example: see the Chaos category above and `ChaosStorageService`.
* The processing pipeline's tunable knobs: [01-Processing-Pipeline.md#hot-reloadable-parameters](01-Processing-Pipeline.md#hot-reloadable-parameters).
