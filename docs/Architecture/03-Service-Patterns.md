# 03 — Service and DI Patterns

How to add a new service to the codebase without warping the architecture. This doc names the lifetime rules, registration conventions, naming, and the cases where you should reach for a factory instead of a direct registration.

## The four lifetimes you will use

| Lifetime    | Use when                                                                                       | Examples in this codebase                                            |
| ----------- | ---------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| `Scoped`    | The default. State per HTTP request or per worker tick. Anything that touches `DbContext`.     | `StorageConsistencyService`, `ChaosStorageService`, every repository |
| `Singleton` | Truly process-wide state, no DB. In-memory caches, schedule registries, counter dictionaries. | `WorkerScheduleRegistry`, `WorkerHeartbeatWriter`, `AdminJobDispatcher` |
| `Transient` | Cheap, stateless helpers. Rare in this codebase. Most stateless helpers are static.           | None today.                                                          |
| `HostedService` | Long-running background loop.                                                              | `PhotoProcessingWorker`, `AdminJobScheduler`, `StorageConsistencyWorker` |

The default is Scoped. If you don't know, choose Scoped. The cost is one extra constructor call per request, the safety gain is huge.

### The singleton trap

A singleton holds an `IServiceProvider`, not a `DbContext`. Workers that need a `DbContext` open a per-tick scope:

```csharp
using var scope = _serviceProvider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
```

That pattern shows up in every worker and in `WorkerHeartbeatWriter`. Never inject `DbContext` directly into a singleton, EF Core will refuse to be tracked across async boundaries and the next request will see stale or torn state.

## Naming

The codebase uses three patterns. Pick the one that matches what you are writing.

| Suffix       | Meaning                                                                                | Examples                                              |
| ------------ | -------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| `*Service`   | Domain action. Stateless per call. Constructor injects repositories + helpers.         | `StorageConsistencyService`, `OrphanedBlobReaperService` |
| `*Worker`    | `BackgroundService` subclass. Long-running loop. Owns no domain logic, calls services. | `PhotoProcessingWorker`, `StorageConsistencyWorker`   |
| `*Repository`| Wraps `DbContext` queries for one aggregate root. Returns domain types, not DTOs.      | `PhotoRepository`, `ProcessingQueueItemRepository`    |

Avoid `*Manager` and `*Helper`. They mean "a bag of methods" and lead to fat constructors.

## Constructor injection only

* No service locator (`IServiceProvider.GetService`) inside business logic.
* Singletons hold an `IServiceProvider` only because they need to create per-tick scopes.
* Constructors with more than 4 parameters are a smell. They are a Blocker per the agent rule. The fix is usually to introduce a factory (see below) or to split the service.

## Where to register

* All registrations live in `Program.cs`. One block per concern, with a short comment naming the lifetime choice.
* Provider-style services (the storage provider, the future queue provider) are registered through a factory, not directly. See `StorageProviderFactory.Create` in `Program.cs`.
* Workers are registered through `AddHostedService<T>()` and gated on `WorkersEnabled`.

### The `WorkersEnabled` gate

`Program.cs` reads this once and branches:

```csharp
var workersEnabled = builder.Configuration.GetValue("WorkersEnabled", true);
if (workersEnabled)
{
    builder.Services.AddHostedService<PhotoProcessingWorker>();
    builder.Services.AddHostedService<PhotoVersionUrlRefreshWorker>();
    builder.Services.AddHostedService<StorageConsistencyWorker>();
    builder.Services.AddHostedService<OrphanedBlobReaperWorker>();
}
else
{
    builder.Services.AddHostedService<AdminJobScheduler>();   // API only
}
```

Two rules follow from this:

* New workers go in the `if (workersEnabled)` block.
* New API-side schedulers / hosted services go in the `else` block.

Run `Backend: Run as Worker` locally to exercise the worker branch.

## Factory for provider selection

When a service has multiple implementations selected at runtime, register a factory, not the implementation.

`StorageProviderFactory.Create(IConfiguration, IServiceProvider)` returns one of:

* `MinioStorageProvider`
* `AzureBlobStorageProvider` (preferred for prod, user-delegation SAS)
* `AzureStorageProvider` (legacy connection-string Azure)

`Program.cs` calls the factory once, registers the result as a singleton. Consumers inject `IStorageProvider`. They never see the concrete type. The mechanism is described in [06-Storage-Abstraction.md](06-Storage-Abstraction.md).

Use the same shape when you introduce a new provider abstraction (a queue provider, an email provider). The pattern is in the `factory-pattern-recipe` skill.

## Builder for multi-step construction

Use a builder when constructing an object requires more than three pieces of state set in order. Today the codebase uses this pattern lightly. The most common construction is a record/DTO with a primary constructor or `with`-expressions. Reach for a full builder when:

* You have optional pieces that interact (e.g. a download URL request with quality, TTL, watermark flag, and access-code context).
* The current code has a parameterless `new`, then ten `obj.X = ...` lines, then a method call.

The `builder-pattern-recipe` skill has the canonical shape.

## Adding a new service: checklist

1. Decide the lifetime. Default to `Scoped`. Singleton only if process-wide state.
2. Decide the name. `*Service`, `*Worker`, or `*Repository`.
3. Write the type. Constructor injects collaborators. No service locator.
4. Register in `Program.cs`. One line. Comment if non-obvious.
5. If the service has multiple runtime implementations, write a factory and register the factory.
6. If the service is a worker, wrap it in `AddHostedService` inside the `WorkersEnabled` branch.
7. Add tests. The xUnit project has examples for every pattern. The convention is one test class per service.
8. If the service has tunable knobs, add them to `SettingsCatalogue` and read them through `ISettingsResolver` per call. See [04-Runtime-Settings.md](04-Runtime-Settings.md).
9. If the service is long-running, add heartbeat stamping. See [05-Worker-Heartbeats.md](05-Worker-Heartbeats.md).

## Anti-patterns that show up here

These are the ones we keep catching in review. Cite them explicitly when you see them.

| Smell                                                            | Why bad                                                            | Fix                                                          |
| ---------------------------------------------------------------- | ------------------------------------------------------------------ | ------------------------------------------------------------ |
| `new ConcreteService(...)` inside Application code                | Couples to the concrete. Untestable.                               | Inject the interface. Register in `Program.cs`.              |
| Constructor with > 4 parameters                                   | Too many collaborators. Service is doing more than one thing.      | Split into two services, or introduce a builder/factory.     |
| `_serviceProvider.GetService<T>()` inside a method                | Service locator. Hides dependencies. Untestable.                   | Inject `T` in the constructor.                               |
| Singleton holds `DbContext`                                       | Cross-async tracking corruption.                                   | Hold `IServiceProvider`, open per-call scope.                |
| Worker logic inline in a controller                               | Blocks the request thread.                                         | Enqueue an `AdminJob`. See [02-AdminJob-Queue.md](02-AdminJob-Queue.md). |
| Hard-coded interval / threshold                                    | Operations cannot tune at runtime.                                 | Add to `SettingsCatalogue`. Read through `ISettingsResolver`.|
| Static `Dictionary<>` for cross-request state                      | Lost on scale-out. Not coherent across replicas.                   | Store in DB. Or accept that it is per-replica and document it.|

## Where to read next

* Runtime tuning: [04-Runtime-Settings.md](04-Runtime-Settings.md).
* Worker lifecycle: [05-Worker-Heartbeats.md](05-Worker-Heartbeats.md).
* The storage swap as a worked example of factory + provider: [06-Storage-Abstraction.md](06-Storage-Abstraction.md).
