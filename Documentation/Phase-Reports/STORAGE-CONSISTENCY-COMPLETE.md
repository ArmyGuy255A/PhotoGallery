# Storage Reconciliation + Frontend Testing Strategy — Phase Complete ✅

**Status**: Implementation complete, awaiting full E2E verification against running backend
**Date**: 2026-05-03
**Design Decisions**: D006, D007, D008
**Skills Consulted**: photogallery-architect-skill, backend-developer-skill, tdd-unit-testing-skill, frontend-developer-skill, playwright-testing-skill, qa-quality-control-skill, photogallery-documentation-skill

---

## Problem Statement

Three intertwined production issues were reported:

1. **Frontend testing strategy gap.** Karma/Jasmine specs with mocked `HttpClient` could not detect the broken-thumbnail bug because the bug only manifests with a real browser, real API, and real MinIO storage talking to each other.
2. **Album-detail thumbnails rendered as broken-image icons.** After upload, the in-component thumbnail in the photo-upload control showed correctly, but the photo cards below showed the browser's broken-image icon. Root cause was twofold — (a) cached pre-signed URLs in `PhotoVersionUrl` pointed at storage objects that no longer existed, and (b) the `<img>` had no `(error)` fallback so a 404 from MinIO produced the broken-image icon rather than the SVG placeholder.
3. **Storage/database drift.** Many photos had `original.jpg` in MinIO but were missing one or more of `thumbnail.jpg / low.jpg / medium.jpg / high.jpg`. The existing `PhotoConsistencyChecker` validated queue *records* but never inspected actual storage objects, so the drift was invisible to the system.

---

## What Was Done

### D006 — Frontend Testing Strategy: Playwright-First

- Added a Page Object Model under `FE.PhotoGallery/e2e/pages/`, `fixtures/`, and `helpers/`.
- Specs now drive the actual user journey through a real backend + MinIO instead of relying on mocked HTTP.
- Karma/Jasmine retained for pure component logic but receives no new investment.

### D008 — Cached Pre-Signed URL Storage Verification (BACKEND, TDD complete)

- `PhotoVersionUrlService.GetPhotoVersionUrlAsync` now verifies the underlying object exists via `IStorageProvider.ExistsAsync` before returning a cached pre-signed URL.
- If the object is gone, the cached row is marked `IsActive = false` and the URL is regenerated (overwriting the now-inactive row in place via `GetByPhotoAndQualityIncludingInactiveAsync`).
- Gated by `BlobStorage:VerifyCachedUrls` (default true) so production can disable the extra HEAD request once D007's worker keeps drift bounded.
- Centralized storage-key construction in `BuildStorageKey(albumId, photoId, quality)` to remove the previously-duplicated key-format strings.
- 4 new tests (RED → GREEN → BLUE), all passing.

### D007 — Storage/Database Consistency Reconciliation (BACKEND, TDD complete)

- New `StorageConsistencyService` reconciles all four classification cases per quality:
  - `(Storage missing, Item none)` → insert Pending
  - `(Storage missing, Item Complete)` → flip to Pending, reopen queue, invalidate cached URL
  - `(Storage present, Item none)` → back-fill Complete
  - `(Storage present, Item Complete)` → no-op
- Edge cases handled: original-missing photos short-circuit with a warning (no auto-delete), Processing items are never touched (concurrency safety with `PhotoProcessingWorker`), Error-at-MaxRetries items are left alone, retryable Errors with present storage are marked Complete.
- `SemaphoreSlim(1, 1)` guard prevents overlapping cycles when worker tick coincides with admin endpoint call.
- New `StorageConsistencyWorker` BackgroundService runs the service hourly, mirroring the `PhotoVersionUrlRefreshWorker` pattern.
- New `POST /api/photos/admin/reconcile-storage` endpoint with `[Authorize(Roles="Admin")]` lets admins synchronously trigger a cycle and inspect the `ConsistencyReport` JSON.
- 17 new tests covering all 13 cells of the classification × item-state matrix plus idempotency + concurrency.

### Frontend defensive fix for the broken-thumbnail bug

- Added `data-testid` attributes to `album-detail.component.ts` and `photo-upload.component.ts` for stable E2E selectors.
- Added `(error)="onThumbnailError(photo)"` handlers to both components. When the `<img>` fails to load, `thumbnailUrl` is cleared and the existing SVG placeholder renders instead of the broken-image icon.

### Playwright spec infrastructure

- `e2e/pages/base.page.ts` — `BasePage` with the `byTestId` helper.
- `e2e/pages/login.page.ts`, `album-detail.page.ts`, `photo-upload.page.ts` — page objects mirroring component testids.
- `e2e/fixtures/auth.fixture.ts` — `createAlbumViaApi`, `adminAuthHeaders` for fast test setup.
- `e2e/fixtures/data.fixture.ts` — `getSamplePhotos(n)` reads from `SamplePhotos/`.
- `e2e/helpers/wait-for-processing.ts` — polls `/api/photos/{id}/status` until `percentComplete === 100`.
- `e2e/helpers/assert-image-loads.ts` — checks `naturalWidth > 0 && complete === true` (the only reliable way to distinguish loaded image from broken-image icon).
- `e2e/photo-upload-and-display.spec.ts` — the regression spec for the bug.
- `e2e/admin-reconcile.spec.ts` — smoke test for the admin endpoint.

### VS Code task automation

- Added `Tests: Frontend E2E (Playwright)`, `Tests: Frontend E2E UI Mode (Playwright)`, `Tests: Frontend E2E Headed (Playwright)` next to the existing test tasks.

---

## Files Affected

**New (production code):**
- `PhotoGallery/Services/Processing/StorageConsistencyService.cs`
- `PhotoGallery/Services/Processing/StorageConsistencyWorker.cs`

**New (tests):**
- `PhotoGallery.Tests/StorageConsistencyServiceTests.cs` (17 tests)
- `FE.PhotoGallery/e2e/pages/{base,login,album-detail,photo-upload}.page.ts`
- `FE.PhotoGallery/e2e/fixtures/{auth,data}.fixture.ts`
- `FE.PhotoGallery/e2e/helpers/{wait-for-processing,assert-image-loads}.ts`
- `FE.PhotoGallery/e2e/photo-upload-and-display.spec.ts`
- `FE.PhotoGallery/e2e/admin-reconcile.spec.ts`

**Modified (production):**
- `PhotoGallery/Services/PhotoVersionUrlService.cs` — D008 verification + key-builder helper.
- `PhotoGallery/Interfaces/IPhotoVersionUrlRepository.cs` + `Data/Repositories/PhotoVersionUrlRepository.cs` — added `GetByPhotoAndQualityIncludingInactiveAsync`.
- `PhotoGallery/Controllers/PhotosController.cs` — `POST /api/photos/admin/reconcile-storage`.
- `PhotoGallery/Program.cs` — registered `StorageConsistencyService` + `StorageConsistencyWorker`.
- `FE.PhotoGallery/src/app/components/albums/album-detail.component.ts` — testids + `(error)` handler.
- `FE.PhotoGallery/src/app/components/albums/photo-upload.component.ts` — testids + `(error)` handler.
- `.vscode/tasks.json` — Playwright tasks.
- `PhotoGallery.Tests/PhotoVersionUrlServiceTests.cs` — extended for the D008 verification path.

**Modified (documentation):**
- `Documentation/Architecture/DESIGN_DECISIONS.md` — D006, D007, D008 added.
- `Documentation/INDEX.md` — last-updated timestamp + cross-links for the new design decisions.
- `Documentation/Phase-Reports/STORAGE-CONSISTENCY-COMPLETE.md` (this file).

---

## Test Results

| Suite | Before | After | New |
|-------|--------|-------|-----|
| Backend xUnit | 66 | 87 | +21 (4 D008 + 17 D007) |
| Frontend Karma | unchanged | unchanged | — |
| Playwright specs | 1 (full-flow) | 3 | +2 (regression + reconcile) |
| Angular build | ✅ | ✅ | — |

Backend tests: `dotnet test PhotoGallery.Tests --nologo --verbosity quiet` → **87 passed, 0 failed**.

---

## Manual Verification Checklist (next session)

The Playwright specs need a live backend + MinIO to actually run. Recommended verification order when the developer next starts the stack:

1. `Docker: Start Services (MinIO, PostgreSQL)` task.
2. `Quick Start: Backend Only` task (sets `DISABLE_AUTH=true`).
3. `Tests: Frontend E2E (Playwright)` task — should pass both new specs.
4. Manually upload a photo to an existing album and confirm the thumbnail renders in the card.
5. Hit `POST /api/photos/admin/reconcile-storage` from a REST client and confirm the `ConsistencyReport` JSON shape matches `StorageConsistencyService.ConsistencyReport`.

---

## Skill Validation Summary

- **photogallery-architect-skill** — Approved D006, D007, D008 designs before implementation. Rubber-duck pass on D007 caught 6 blocking issues in the original test design (wrong URL invalidation contract, missing edge cases) which were fixed before any code was written.
- **tdd-unit-testing-skill** — All backend changes followed strict RED → GREEN → BLUE with separate commits per phase.
- **playwright-testing-skill** — Page Object Model used throughout; specs target user-visible behavior; `naturalWidth > 0` is the assertion for "image actually loaded".
- **qa-quality-control-skill** — Helpers prevent flakiness (`waitForPhotoProcessing` polls instead of `waitForTimeout`; image-load assertion uses `expect.poll`).
- **photogallery-documentation-skill** — D006, D007, D008 added with the standard Context/Decision/Rationale/Implications/Implementation/Alternatives template.

---

## Out of Scope (by design)

- Migrating existing Karma specs to Playwright.
- Auto-deleting orphan storage objects (no DB record).
- Auto-deleting Photo rows whose `original.jpg` is missing.
- Touching `PhotoConsistencyChecker` — different concern (queue-record validation), per D007.
- Adding new CoreUI components — only added `data-testid` and `(error)` handlers to existing components.

---

**Next Phase Candidate**: Shopping cart feature (the user's original next-step). With the consistency worker running and cached URLs being verified, the data layer is now reliable enough to build customer-facing purchase flows on top of.
