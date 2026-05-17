## Context
Sub-story of the Admin Audit Console epic. Stand up the persistence layer for download events.

## Problem
There is no entity today that records who downloaded what; we need it before any capture or admin endpoints can be built.

## Proposed approach
- Evaluate reuse of `AuditEvent` (from EPIC-03 #4): if its `MetadataJson` is rich enough, model `DownloadAuditEvent` as a **typed projection / view** over `AuditEvent` rows where `Action = "download.cart"`. Otherwise, create a dedicated entity that links 1-to-1 with an `AuditEvent` row.
- Recommended initial fields: `Id`, `OccurredAtUtc`, `UserId` (nullable), `AccessCode` (nullable), `IpAddress`, `PhotoCount`, `TotalBytes`, `QualityVariant` (enum), `AlbumId` (nullable; null if cross-album), `PhotoIdsJson`.
- Index on `(OccurredAtUtc DESC)` and `(UserId, OccurredAtUtc)`.
- `IDownloadAuditRepository` with paged query support.

## Acceptance criteria
- [ ] **Given** the design decision is documented in `DESIGN_DECISIONS.md`, **when** review completes with #4 owner, **then** the path forward (extend vs separate) is captured.
- [ ] **Given** the migration runs, **when** the schema is inspected, **then** the tables/indexes exist.
- [ ] **Given** the repository, **when** queried with paging + filters (date, user, album, access code), **then** results are correct.
- [ ] xUnit covers repository.

## Out of scope
- Capturing events from the download endpoint (separate story).
- Admin endpoints/UI.

## Parent
Sub-story of the Admin Audit Console epic.
