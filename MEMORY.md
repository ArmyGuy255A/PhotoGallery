# PhotoGallery ΓÇö Memory

Append-only log of architectural and operational lessons learned during the
**48-hour scale-out + admin-settings + reliability cycle (May 2026)** that
shipped via PRs #141, #142, #143, #144 onto the `trial` and `main` branches.

The copilot dev team's metaskills should read this file before answering
questions or proposing changes touching: split-topology ACA, runtime
settings, worker reliability, role-based authorization, the SAS cache, or
the trialΓåÆmain release flow.

Newest sections live at the top. Each entry uses a consistent shape:

- **Convention** ΓÇö the rule we now follow.
- **Citation** ΓÇö file path + commit SHA on the `trial` branch.
- **Reason** ΓÇö why a future task should respect this.
- **Pitfall** ΓÇö concrete failure mode if ignored.

If you discover an entry has gone stale, append a dated note at the bottom
of that entry rather than editing in place ΓÇö provenance matters.

---

## Table of contents

1. [Topology & scale-out](#1-topology--scale-out)
2. [Workers & the processing queue](#2-workers--the-processing-queue)
3. [Settings & hot-reload](#3-settings--hot-reload)
4. [Permissions & roles](#4-permissions--roles)
5. [Performance & caching](#5-performance--caching)
6. [API & serialization](#6-api--serialization)
7. [Frontend patterns](#7-frontend-patterns)
8. [Branching & release process](#8-branching--release-process)
9. [Infrastructure & terraform](#9-infrastructure--terraform)
10. [CI / GitHub Actions](#10-ci--github-actions)
11. [UX gotchas](#11-ux-gotchas)

---

## 1. Topology & scale-out

### 1.1 API replica is pinned `min=max=1`

- **Convention**: The `ca-photogallery-api-dev` container app runs at exactly one replica. The worker container app (`ca-photogallery-worker-dev`) is the only piece that scales horizontally.
- **Citation**: `terraform/dev/main.tf` `module "compute"` block; commit `c576453`.
- **Reason**: SignalR's `PhotoProgressHub` keeps connected WebSocket clients in process memory. We deliberately do **not** use Azure SignalR Service. With two API replicas, half of every photo's `ProcessingProgress` and `ProcessingCompleted` events get dropped because the client is connected to the "other" replica.
- **Pitfall**: A second API replica appears to work ΓÇö uploads complete, the queue drains, the worker logs photos as processed ΓÇö but the SPA's per-photo progress bars stay frozen for whichever replica didn't process the photo. The 5-second `upload-progress-aside` poll fallback partially masks it, but per-photo % bars are real-time only when the API replica itself processed the photo.

### 1.2 `WorkersEnabled` flag splits API and worker roles from one image

- **Convention**: The same Docker image runs as either API-only (`WorkersEnabled=false`) or worker-only (`WorkersEnabled=true`). The four hosted services (`PhotoProcessingWorker`, `PhotoVersionUrlRefreshWorker`, `StorageConsistencyWorker`, `OrphanedBlobReaperWorker`) register **only** when the flag is true.
- **Citation**: `PhotoGallery/Program.cs` line ~257 (`if (workersEnabled) { builder.Services.AddHostedService<ΓÇª>(); }`); commit `361d255`.
- **Reason**: One image, two ACA apps, differentiated only by env vars + ingress. Trivial to deploy in lockstep, trivial to roll back, trivial to debug ("which role are you running?").
- **Pitfall**: If you re-introduce a background service and forget the gate, the API replica will start eating CPU on image processing again, the request thread pool will starve, and `/processing-summary` will start 503-ing. **Every** `AddHostedService<T>` registration must be inside the `if (workersEnabled)` block.

### 1.3 KEDA MSSQL scaler cannot authenticate against AAD-only SQL ΓÇö use CPU

- **Convention**: For worker scale-out on ACA, use a `custom_scale_rule` of type `cpu` (utilization=70). Do **not** use the KEDA `mssql` scaler.
- **Citation**: `terraform/modules/compute/main.tf` `cpu_scale_rule` dynamic block; commit `c576453`. The dead `queue_depth_scale_rule` variable is kept in the module with a deprecation comment for backwards compat.
- **Reason**: KEDA's mssql scaler runs **outside** the container (in the KEDA operator). It cannot use our UAMI-only SQL auth. We observed `KEDAScalerFailed: mssql: login error: Login failed for user ''` and the worker never scaled past `min=0`.
- **Pitfall**: A future PR that switches back to MSSQL queue-depth scaling will silently fail with the same error in the KEDA logs while the worker happily sits idle and the queue piles up. The CPU scaler is a clean proxy because workers are CPU-bound during resize.

### 1.4 In-memory state (registries, caches) is per-process

- **Convention**: For anything an admin needs to see across replicas ΓÇö worker status, processed counts, last-tick times ΓÇö use a DB-backed table that workers UPSERT into. Don't rely on singletons inside the API process.
- **Citation**: `PhotoGallery/Models/WorkerHeartbeat.cs` + `PhotoGallery/Services/WorkerHeartbeatWriter.cs`; commit `248298c`. Compare with `PhotoGallery/Services/WorkerScheduleRegistry.cs` which is the per-process registry.
- **Reason**: The API replica's `WorkerScheduleRegistry` only knows about its own workers. With `WorkersEnabled=false` on the API, the admin Service Health page was empty even though the worker container was healthy on its sibling app.
- **Pitfall**: "But it works on my machine!" ΓÇö sure, the dev runs everything in one process. Production has the split. Always assume the data must travel through the database to be visible cross-replica.

### 1.5 OOM cap on Consumption ACA at 0.5 vCPU / 1 GiB

- **Convention**: Worker containers need at least `1.0 vCPU / 2 GiB`. Keep `PhotoProcessing:WorkerParallelism=2` so bursts scale **replicas** (1ΓåÆ3), not threads within a replica.
- **Citation**: `terraform/dev/main.tf` `module.compute_worker` cpu=1.0 memory="2Gi"; commit `c576453`. Original OOMKilled logs (`exit 137`) traced to ImageSharp parallel resize on 1 GiB.
- **Reason**: ImageSharp decoders + 4 parallel resize ops on a 12MP photo exceeded 1 GiB. The next supported Consumption pair up (1.0/2Gi) gives ~2├ù headroom at no SKU change.
- **Pitfall**: Increasing `WorkerParallelism` instead of memory is a foot-gun ΓÇö you'll just OOM faster. Always solve burst capacity horizontally (more replicas) rather than vertically (more threads).

---

## 2. Workers & the processing queue

### 2.1 Lease query must recover orphaned `Processing` rows

- **Convention**: `LeaseNextBatchAsync` claims `Status=Pending` **OR** `Status=Processing AND LeaseExpiresAt < now` **OR** `Status=Error AND retry-due`. The UPDATE atomically flips orphaned `Processing` rows back to `Pending` in the same operation.
- **Citation**: `PhotoGallery/Data/Repositories/ProcessingQueueItemRepository.cs` SqlServer lease SQL + Sqlite fallback; commit `248298c`.
- **Reason**: When a worker dies mid-tick (we observed OOM-kills), rows stay at `Status=Processing` with an expired lease. The original query filtered them out ΓÇö 808 stuck items piled up in trial before this fix.
- **Pitfall**: Worker crashes become silent permanent backlogs. If you ever see "Processing: N / Pending: 0" in Service Health with workers idle, the lease query has regressed.

### 2.2 Reconciler must reset orphaned `Processing` rows, not skip them

- **Convention**: `StorageConsistencyService.ReconcileQualityAsync` checks `item.LeaseExpiresAt < now` for `Processing` rows and resets to `Pending`. It does **not** blindly skip every `Processing` item.
- **Citation**: `PhotoGallery/Services/Processing/StorageConsistencyService.cs`; commit `248298c`.
- **Reason**: The old "never touch Processing for concurrency safety" comment was wrong for orphans. An expired lease is the unambiguous "the owner is dead" signal.
- **Pitfall**: Without this, the periodic consistency sweep walks past the same 800 stuck items every hour, growing your log noise but doing nothing.

### 2.3 Worker heartbeat is a DB UPSERT keyed on (WorkerName, InstanceId)

- **Convention**: Every worker writes one `WorkerHeartbeat` row per `(WorkerName, InstanceId)` pair via `WorkerHeartbeatWriter.StampAsync` on every tick. Success stamps clear `LastError`; failure stamps populate it. `ItemsProcessedTotal` and `ItemsInFlight` are process-local counters shipped on each stamp.
- **Citation**: `PhotoGallery/Models/WorkerHeartbeat.cs` (unique index on `(WorkerName, InstanceId)`) + `PhotoGallery/Services/WorkerHeartbeatWriter.cs`; commit `248298c`.
- **Reason**: Per-replica visibility on the admin Service Health page. The API replica's `GetServiceHealth` merges its in-memory registry with the heartbeat rows from sibling replicas, deduped by the same key.
- **Pitfall**: Don't add a worker without also wiring `WriteHeartbeat` into its loop ΓÇö it'll be invisible to admins. The `IsAlive` flag goes false when a heartbeat hasn't been refreshed in 2├ù its interval (min 90s).

---

## 3. Settings & hot-reload

### 3.1 Every catalogue setting hot-reloads ΓÇö no `RestartRequired: true`

- **Convention**: Settings are read via `ISettingsResolver` at the **use site** (top of each worker tick, top of each HTTP handler), not in the constructor. `SettingsCatalogue` entries all have `RestartRequired: false`.
- **Citation**: `PhotoGallery/Services/SettingsCatalogue.cs`; `PhotoGallery/Services/Processing/PhotoProcessingWorker.cs` lines that re-read `currentInterval` per loop iteration; commit `2b9edc2`.
- **Reason**: The admin Settings tab promises "no restart needed" ΓÇö when even one setting is `RestartRequired: true`, that promise becomes muddy and operators stop trusting the tab.
- **Pitfall**: Constructor-captured settings are silent. Tests still pass. You only notice when the admin changes a value and nothing happens until the next deploy.

### 3.2 Sliding + absolute cache TTL pattern

- **Convention**: For URL caches (or any cache where the stored value has a hard external expiry), use both `SlidingExpiration` AND `AbsoluteExpirationRelativeToNow`. Sliding is admin-tunable; absolute = signed-token TTL minus a safety margin. Clamp sliding so it can never exceed absolute.
- **Citation**: `PhotoGallery/Services/PhotoVersionUrlService.cs` `GenerateShortLivedUrlAsync` `MemoryCacheEntryOptions` block; commit `4a43700`.
- **Reason**: Sliding alone can outlive the SAS validity ΓåÆ expired URLs served. Absolute alone forces re-signs every N minutes regardless of usage. Both together = hot entries stay hot forever, cold entries fall out, and we never hand out an expired URL.
- **Pitfall**: Without the clamp, an admin sets `UrlCacheSlidingMinutes=120` when the SAS TTL is only 60, and within an hour we're serving 401s on stale URLs.

---

## 4. Permissions & roles

### 4.1 `User` is the implicit baseline ΓÇö never expose as a toggle

- **Convention**: `AdminController.AllowedRoles = { "Admin", "AlbumCreator" }`. The "User" role exists in the DB but is never shown in the admin UI and is never sent in the PUT `roles` payload as an *assignable* role. The FE renders it as a non-interactive baseline chip.
- **Citation**: `PhotoGallery/Controllers/AdminController.cs` `AllowedRoles` const + `FE.PhotoGallery/src/app/components/admin/admin-settings.component.ts` baseline chip; commit `9f8afcb`.
- **Reason**: Every authenticated user has the User role implicitly. Removing it leaves them in an "elevated-only" state that downstream code (`IsInRole("User")` checks, default-user paths) doesn't expect.
- **Pitfall**: Earlier we widened `AllowedRoles` to include "User" and the FE started sending it ΓÇö the admin checkbox column became impossible to use. Resist the temptation to let admins toggle it.

### 4.2 Server force-preserves `User` on every `PUT /roles`

- **Convention**: `SetUserRoles` accepts "User" in the payload (FE always sends it), force-adds it to the requested set if missing, and explicitly excludes it from the `toRemove` computation. Belt **and** suspenders.
- **Citation**: `PhotoGallery/Controllers/AdminController.cs` `SetUserRoles`; commit `09a2334`.
- **Reason**: A buggy FE caller (or a curl from an admin) can't strip the baseline role. Defence in depth.
- **Pitfall**: If you "simplify" this and just validate against `AllowedRoles`, you'll get 400 errors when the FE sends "User", or strip it when the FE forgets to.

### 4.3 Per-album auth: `OwnerId != userId && !isAdmin ΓåÆ Forbid`

- **Convention**: Every album-scoped endpoint pairs `[Authorize(Roles = "Admin,AlbumCreator")]` at the decorator with the body check `if (album.OwnerId != userId && !User.IsInRole("Admin")) return Forbid();`. The role decorator opens the door; the body check keeps non-admins to their own albums.
- **Citation**: Every endpoint in `PhotoGallery/Controllers/AlbumsController.cs` and the album-scoped endpoints in `PhotoGallery/Controllers/PhotosController.cs`; commit `9e2e309`.
- **Reason**: AlbumCreator can create albums but must only manage their own. Admin overrides everywhere.
- **Pitfall**: Widening the role decorator without also having the body check turns AlbumCreator into a pseudo-admin who can edit anyone's albums. Conversely, having the body check but a too-narrow decorator gives a 403 before the body even runs.

### 4.4 JWT refreshes on every page reload ΓÇö no logout/login needed

- **Convention**: `AuthService.refreshRolesFromServer()` POSTs to `/api/auth/refresh` on app boot via a `provideAppInitializer` hook. The endpoint re-reads roles from `UserManager.GetRolesAsync` and mints a fresh token.
- **Citation**: `FE.PhotoGallery/src/app/services/auth.service.ts` `refreshRolesFromServer` + `FE.PhotoGallery/src/app/app.config.ts` initializer; commit `022365f`.
- **Reason**: When an admin grants a role, the affected user just refreshes the page. The old "log out, log back in" workflow was a recurring support drain.
- **Pitfall**: If a future change moves the JWT issuance to an external IdP entirely and removes the `/api/auth/refresh` endpoint, this auto-refresh becomes a no-op (silently). Add a deprecation warning before removing the endpoint.

### 4.5 Inline role chips, not a modal ΓÇö until role count > 5

- **Convention**: The Users tab renders each elevated role as a click-to-toggle chip in the existing row. No "Edit user roles" modal. New roles automatically appear because the FE fetches the catalogue from `GET /api/admin/roles`.
- **Citation**: `FE.PhotoGallery/src/app/components/admin/admin-settings.component.ts` role-chip template; commit `9f8afcb`.
- **Reason**: For 2-3 roles, inline is faster (one click vs. modal-open-toggle-save-close). The BE PUT contract is already `{ roles: string[] }`, so promoting to a modal later is an FE-only refactor.
- **Pitfall**: Don't pre-emptively build the modal. Don't add a "Roles" sidebar tab ΓÇö search/sort/pagination are already on the Users tab.

---

## 5. Performance & caching

### 5.1 No N+1 on URL signing ΓÇö batch via `GetByPhotoIdsAsync`

- **Convention**: `AlbumsController.GetAlbumPhotos` calls `_urlService.GetCachedUrlsAsync(photoIds, qualities)`, which uses `IPhotoVersionUrlRepository.GetByPhotoIdsAsync` to fetch every page's URL rows in **one** DB call. No per-photo `GetByPhotoAndQualityAsync` loop.
- **Citation**: `PhotoGallery/Controllers/AlbumsController.cs` `GetAlbumPhotos` + `PhotoGallery/Services/PhotoVersionUrlService.cs` `GetCachedUrlsAsync`; commit `248298c`.
- **Reason**: 20-photo page was 40 DB calls + 40 HEAD requests against Azure Blob; now it's 2 DB calls + 0 HEADs. Same correctness; ~50├ù faster.
- **Pitfall**: Whenever you find yourself doing `foreach (photo) { await _urlService.GetPhotoVersionUrlAsync(...) }`, stop and add a batch method on the repo.

### 5.2 No `GetAllAsync()` in scoped/polled endpoints

- **Convention**: Endpoints scoped to a single album/photo MUST filter at the DB level. New repo methods like `GetAlbumPhotosIncludingUploadingAsync(albumId)` and `GetByAlbumIdAsync(albumId)` exist for this reason.
- **Citation**: `PhotoGallery/Controllers/PhotosController.cs` `GetAlbumProcessingSummary`; `PhotoGallery/Data/Repositories/PhotoRepository.cs` + `ProcessingQueueItemRepository.cs`; commit `8edf55b`.
- **Reason**: `/processing-summary` is polled every 5s per user. Doing `_photoRepository.GetAllAsync()` + in-memory filter pulled ~1500 photos and ~6000 queue items on every poll ΓÇö observed as 503s under load.
- **Pitfall**: A polling endpoint that does any full-table scan will tip over the API replica under burst load. Always grep new endpoints for `GetAllAsync` before merging.

### 5.3 `BlobStorage:VerifyCachedUrls=false` by default

- **Convention**: When returning a cached pre-signed URL, **don't** HEAD-check the underlying blob on every read. Trust `StorageConsistencyService` to catch drift on its sweep.
- **Citation**: `PhotoGallery/Services/SettingsCatalogue.cs` `VerifyCachedUrls` default `"false"`; commit `248298c`.
- **Reason**: Per-read HEAD against Azure Blob was burning ~50ms ├ù N photos per page render. The consistency worker handles real drift (deleted blobs, expired URLs) on its own cadence.
- **Pitfall**: If you flip this back to `true` to debug a drift issue, leave a calendar reminder to flip it off again ΓÇö production performance degrades immediately.

---

## 6. API & serialization

### 6.1 EF Core hands `DateTime` back as `Kind=Unspecified` ΓÇö wire the UTC converter

- **Convention**: `UtcDateTimeJsonConverter` and `NullableUtcDateTimeJsonConverter` are registered in BOTH the MVC `JsonSerializerOptions` and the SignalR `PayloadSerializerOptions`. They normalize every emitted `DateTime` to UTC with an explicit `Z` suffix.
- **Citation**: `PhotoGallery/Serialization/UtcDateTimeJsonConverter.cs` + `PhotoGallery/Program.cs` `AddControllersWithViews().AddJsonOptions` + `AddSignalR().AddJsonProtocol`; commit `9f8afcb`.
- **Reason**: Without the converter, `System.Text.Json` emits `Kind=Unspecified` `DateTime`s with no timezone offset. JavaScript's `new Date("2026-05-16T19:55:00")` then interprets that as **local** time, so every UI timestamp appears shifted by the visitor's UTC offset.
- **Pitfall**: Adding a new SignalR hub or a non-MVC HTTP pipeline (gRPC, minimal APIs) is a place to forget the converter. Audit any new serialization surface.

---

## 7. Frontend patterns

### 7.1 `PhotoPageLoader.refreshLoaded(idFn)` merges by id ΓÇö never resets the array

- **Convention**: Polling-triggered photo grid refreshes call `refreshLoaded(p => p.id)`, which re-fetches loaded pages, merges new data into existing array slots by id, and preserves object references for unchanged items. **Don't** call `loader.reset() + loader.enableAutoLoad()` for polling refresh.
- **Citation**: `FE.PhotoGallery/src/app/services/photo-page-loader.ts` `refreshLoaded`; `FE.PhotoGallery/src/app/components/albums/album-detail.component.ts` `onAlbumActivityChanged`; commit `8edf55b`.
- **Reason**: A reset-and-reload nukes the entire array and re-renders from page 1 ΓåÆ visible scroll glitch every poll cycle, especially on a 400-photo album. Combined with `trackBy: trackByPhotoId` on the `*ngFor`, the merge approach keeps DOM nodes stable for unchanged photos.
- **Pitfall**: A new "refresh on N" feature that uses `loader.reset()` will reintroduce the glitch. Always prefer `refreshLoaded`.

### 7.2 Mobile photo-modal nav buttons need explicit z-index + `touch-action`

- **Convention**: `.nav-btn` has `z-index: 2` (matching `.close-btn`), `touch-action: manipulation`, and a `@media (max-width: 640px)` breakpoint that shrinks to 44px and tucks closer to the edge.
- **Citation**: `FE.PhotoGallery/src/app/components/photo-modal/photo-modal.component.ts` styles; commit `3349043`.
- **Reason**: Absolute-positioned buttons inside a flex backdrop paint **under** the image on mobile because the modal-content creates its own stacking context. Without `z-index`, the prev/next buttons were un-tappable on phones. `touch-action: manipulation` disables iOS double-tap zoom on the button.
- **Pitfall**: Any new modal-overlay control needs the same treatment. Test with mobile DevTools emulation before merging.

---

## 8. Branching & release process

### 8.1 Squash-merge is the only merge type on this repo

- **Convention**: `mergeCommitAllowed=false, rebaseMergeAllowed=false, squashMergeAllowed=true, deleteBranchOnMerge=true`. Every PR collapses into one commit on the target branch.
- **Citation**: `gh repo edit ArmyGuy255A/PhotoGallery --enable-squash-merge --enable-merge-commit=false --enable-rebase-merge=false --delete-branch-on-merge`; this session.
- **Reason**: With merge commits, a feature branch that branched from PR #X's content (before #X squash-merged) accumulates commits with different SHAs than what landed on the target. The "same content, different SHAs" problem caused phantom conflicts on PRs #142 and #144.
- **Pitfall**: Don't turn merge-commit back on for "preserving history". The single squash commit on trial/main IS the history; the feature-branch detail lives on the PR itself.

### 8.2 `sync-main-to-trial.yml` keeps the two branches textually in lockstep

- **Convention**: Every push to `main` triggers a workflow that merges main back into trial and opens a `chore(sync)` PR. No-ops cleanly when trial already contains main. Labels `needs-fix` if there's a real conflict.
- **Citation**: `.github/workflows/sync-main-to-trial.yml`; commit `64b4b7d`.
- **Reason**: Without it, trial slowly drifts behind main. Any direct edit on main (e.g., the `terraform fmt` issue in PR #138) leaves trial with textually-different-but-functionally-identical code, causing phantom conflicts on the next trialΓåÆmain PR.
- **Pitfall**: If you disable this workflow, plan to manually open a mainΓåÆtrial PR after every release.

### 8.3 Pre-PR: fetch + merge `origin/trial` into the feature branch

- **Convention**: Before opening a PR, run `git fetch origin && git merge origin/trial` on the feature branch to surface conflicts up-front. Resolve them on the branch, push, then open the PR.
- **Citation**: Existing repo memory + this session's PR #143/#144 conflict-resolution flow.
- **Reason**: A surprise conflict at PR-open time blocks review. Resolving on-branch lets you re-run tests and validate the merge result before asking for review.
- **Pitfall**: Skipping this means a reviewer opens a PR labelled "CONFLICTING" and has to wait for a follow-up push. Doubles the review cycle.

### 8.4 Deploy both ACA apps in lockstep from the same image tag

- **Convention**: `release.yml` updates `ca-photogallery-api-dev` AND `ca-photogallery-worker-dev` with the same image tag in the same job. Gated on `vars.ACA_WORKER_NAME` so older deployments without the worker app still work (warning, continue).
- **Citation**: `.github/workflows/release.yml` "Update API Container App image" + "Update worker Container App image" steps; commit `5e2a02e`.
- **Reason**: Both apps share the same DB schema, the same migrations, the same domain model. An image-tag mismatch between them means one is reading rows the other doesn't know about yet ΓÇö silent data corruption potential.
- **Pitfall**: Don't manually `az containerapp update` one without the other. Use the workflow.

### 8.5 `--delete-branch-on-merge` is DANGEROUS for long-lived branches

- **Convention**: Repo setting `deleteBranchOnMerge = false`. The squash-merge default stays on, but the auto-delete is off.
- **Citation**: `gh repo edit ArmyGuy255A/PhotoGallery --delete-branch-on-merge=false`; this session (after PR #144 squash-merged and silently deleted `trial`).
- **Reason**: GitHub's auto-delete fires on **any** merged head branch, including long-lived staging branches like `trial`. When PR #144 (trial ΓåÆ main) squash-merged with `deleteBranchOnMerge=true`, the entire `trial` branch was deleted from origin. The next feature PR couldn't even find its target.
- **Pitfall**: If you re-enable auto-delete, add **branch protection** on `main` and `trial` first ΓÇö GitHub respects the "do not delete" protection rule even with auto-delete enabled. The proper hardening is:
  1. Branch protection on `main` and `trial` (block force-push + deletion).
  2. Then optionally re-enable `deleteBranchOnMerge` so short-lived feature branches still auto-clean.
- **Recovery**: After accidental deletion, recreate with `git push origin origin/main:refs/heads/trial` since `main` always has the latest trial content immediately after a trial ΓåÆ main merge.

### 8.6 Two parallel PRs against `trial`: rebase the second one after the first lands

- **Convention**: When two long-lived PRs target the same base branch (e.g. PR #145 `docs/memory-md` + PR #146 `fix/...` both ΓåÆ `trial`), the second PR to merge must rebase onto trial *after* the first lands. Don't try to merge the second PR as-is ΓÇö its diff will contain stale copies of files the first PR rewrote, and GitHub will show "47 files changed" when the real net contribution is one file.
- **Citation**: This session (May 17 2026): PR #145 opened first with MEMORY.md *plus* the May-2026 scale-out cycle in its base, PR #146 opened later with a different base after the cycle merged. When #146 merged first, #145 turned into 47 phantom-modified files (all already on trial) and one real new file. Rebase-onto-trial restored #145 to just `MEMORY.md`.
- **Reason**: Squash-merge means the first PR collapses to one commit on trial with a NEW SHA. The second PR's branch still has the pre-squash commits as ancestors, so git sees its files as "different content, same path" even when the textual content matches. GitHub reports phantom modifications and may report conflicts on lines both PRs touch.
- **Detection signal**: PR shows `mergeStateStatus: DIRTY` / `mergeable: CONFLICTING` after the other PR merges. Or `gh pr diff <N> --name-only` lists files the PR clearly shouldn't be touching.
- **Recovery flow** (preserve only the NEW files the PR adds):
  1. `git fetch origin && git checkout <feature-branch>`.
  2. `git diff origin/<base> --diff-filter=A --name-only` to identify files **added** by this PR (vs. modified ΓÇö modifications are likely stale).
  3. Save the content of those added files (`git show <branch-tip>:<path>` for each).
  4. `git reset --hard origin/<base>` to snap the branch to the current base.
  5. Restore each saved file with its original content.
  6. `git add <paths> && git commit -m "..." && git push --force-with-lease`.
- **Pitfall**: Don't `git merge origin/trial` into the feature branch ΓÇö that creates a merge commit but doesn't drop the stale modifications, so the PR still shows the phantom 47-file diff and CI may still fail. Reset + restore is the clean answer for a docs-only or single-feature PR.
- **Pitfall #2**: When restoring file content via PowerShell `Set-Content -Value $variable`, PowerShell flattens the line endings. Use `git show <ref>:<path> | Out-File <path> -Encoding utf8` instead (or `git checkout <ref> -- <path>`) to preserve formatting.
- **Prevention**: When you know two PRs will share a base, merge them **in dependency order** (the smaller/independent one first). For docs-only PRs against an active feature branch, prefer cherry-picking the doc commit onto the feature branch right before merging so there's only one PR.

---
---

## 9. Infrastructure & terraform

### 9.1 Compute module is parameterized for the API/worker split

- **Convention**: The single `terraform/modules/compute` module supports both roles via:
  - `ingress_enabled` ΓÇö dynamic ingress block; `false` for the worker.
  - `existing_environment_id` ΓÇö when non-empty, re-uses an existing CAE instead of creating one (so API and worker share a CAE).
  - `http_scale_concurrent_requests` ΓÇö KEDA HTTP scaler, only honoured when ingress is on.
  - `cpu_scale_rule` ΓÇö CPU-utilization scaler (worker only).
  - `container_name` ΓÇö log label, `api` or `worker`.
- **Citation**: `terraform/modules/compute/variables.tf` + `main.tf`; commit `73c4941`.
- **Reason**: One module, two instantiations. Lower drift risk than two copy-pasted modules.
- **Pitfall**: Adding a new "API-only" or "worker-only" feature without the matching variable splits the modules apart over time. Resist; always parameterize.

### 9.2 `Register-SqlPrincipals.ps1` is non-interactive

- **Convention**: SQL UAMI registration uses `az account get-access-token --resource https://database.windows.net/` piped to `Invoke-Sqlcmd -AccessToken`. **Not** `sqlcmd -G` (which prompts for a password in non-interactive sessions and deadlocks CI).
- **Citation**: `terraform/scripts/Register-SqlPrincipals.ps1`; commit `23df937`.
- **Reason**: The script must work the same under local `az login` and under `azure/login@v2` in CI. The `Invoke-Sqlcmd -AccessToken` path is the only one that's truly non-interactive across both.
- **Pitfall**: A "simpler" rewrite that uses `sqlcmd -G` works on a dev machine and silently hangs forever in CI.

### 9.3 `Apply.ps1` auto-detects the dev public IP

- **Convention**: `terraform/scripts/Apply.ps1` calls `Invoke-RestMethod https://api.ipify.org`, parses the IP, and passes `-var dev_public_ip=<x.y.z.w>` to `terraform apply`. `-SkipIpDetect` opts out for static-VPN / CI runners.
- **Citation**: `terraform/scripts/Apply.ps1`; commit `23df937`.
- **Reason**: Without this, every WiFi/ISP shuffle destroys the SQL firewall rule on apply and locks the operator out of the DB until they manually re-add it.
- **Pitfall**: Running plain `terraform apply` while on a different network than your last apply rebuilds the firewall rule with the *current* IP ΓÇö destructive change.

---

## 10. CI / GitHub Actions

### 10.1 `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` at workflow level

- **Convention**: Every `.github/workflows/*.yml` declares a top-level `env: { FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: 'true' }` block.
- **Citation**: All four workflow files (`build.yml`, `release.yml`, `cut-release.yml`, `validate-pr-base.yml`); commit `123feb0`.
- **Reason**: Silences the "Node 20 actions are deprecated" warning that surfaces on every run, and proactively opts every JS-based action (`actions/checkout@v4`, `azure/login@v2`, `docker/*`, `actions/setup-node@v4`) into Node 24 ahead of the June 2026 forced cutover.
- **Pitfall**: New workflows without the env block surface the warning again. Make it part of the new-workflow checklist.

---

## 11. UX gotchas

### 11.1 Cart cap is a UI sanity ceiling, not a memory constraint

- **Convention**: `CartZipService.MaxItemsPerCartConst = 99999`. Effectively unlimited until a ranged-selection UI ships. FE constant `MAX_CART_SIZE = 99999` mirrors it.
- **Citation**: `PhotoGallery/Services/CartZipService.cs` + `FE.PhotoGallery/src/app/services/cart.service.ts`; commit `248298c`.
- **Reason**: The streaming zip path is constant-memory regardless of count. The previous 100-item cap was vestigial and broke real workflows on 400-photo albums.
- **Pitfall**: Don't reintroduce a low cap "for safety". The actual safety concern is the zip download timeout, which is a separate dimension (and currently has no enforced cap).

---

## How this file should evolve

- **Append**, don't rewrite. Old entries stay so the audit trail is intact.
- **Date new entries** by session (yyyy-mm-dd).
- **Cite commit SHAs** at the time of writing. Future you will thank you.
- If a convention becomes obsolete, add a note at the bottom of that entry like:
  > **Update 2026-08-01**: superseded by ┬ºX.Y (link). Keep this entry for context.
