$env:Path += ';C:\Program Files\GitHub CLI'
$env:GH_PAGER = ''
$ErrorActionPreference = 'Stop'

$ProjectId = 'PVT_kwHOAXfsQM4BXNq-'
$StatusFieldId = 'PVTSSF_lAHOAXfsQM4BXNq-zhScGRc'
$StatusMap = @{
  'Backlog'    = '46430833'
  'Ready'      = 'c1524d22'
  'Todo'       = 'f75ad846'
  'InProgress' = '47fc9ee4'
  'Review'     = '4f0e47cb'
  'Done'       = '98236657'
}

function New-Story {
  param(
    [string]$Title,
    [string]$Body,
    [string[]]$Labels,
    [string]$Status = 'Ready',
    [string]$ParentNodeId = $null
  )
  $tmp = "temp/issues/body-$([Guid]::NewGuid().ToString('N')).md"
  Set-Content -Path $tmp -Value $Body -Encoding UTF8
  $labelArgs = @()
  foreach ($l in $Labels) { $labelArgs += '--label'; $labelArgs += $l }
  $createOut = gh issue create --repo ArmyGuy255A/PhotoGallery --title $Title --body-file $tmp @labelArgs 2>&1
  $url = ($createOut | Where-Object { $_ -match 'github.com/.*/issues/\d+' } | Select-Object -Last 1).Trim()
  if (-not $url) { throw "Failed to create $Title : $createOut" }
  $num = [int]($url -replace '.*/','')

  $itemJson = gh project item-add 3 --owner ArmyGuy255A --url $url --format json | Out-String
  $item = $itemJson | ConvertFrom-Json
  $itemId = $item.id

  gh project item-edit --id $itemId --project-id $ProjectId --field-id $StatusFieldId --single-select-option-id $StatusMap[$Status] | Out-Null

  if ($ParentNodeId) {
    $childNodeId = (gh issue view $num --repo ArmyGuy255A/PhotoGallery --json id | ConvertFrom-Json).id
    $q = 'mutation($p:ID!,$c:ID!){addSubIssue(input:{issueId:$p,subIssueId:$c}){subIssue{number}}}'
    gh api graphql -f query=$q -f p=$ParentNodeId -f c=$childNodeId | Out-Null
  }
  Write-Host "  +#$num [$Status] $Title"
  return $num
}

# ============================================================================
# Parent epic node IDs
# ============================================================================
$Epic03 = 'I_kwDOSP2fYs8AAAABBxFNbQ'   # #4 Album Permissions + Audit
$Epic02b = 'I_kwDOSP2fYs8AAAABBxFNQA'  # #3 Multi-IDP Account Linking

$Created = @{}

# ============================================================================
# TASK 3a — Decompose #4 EPIC-03 Album Permissions + Audit Log
# ============================================================================
Write-Host "`n=== Decomposing #4 EPIC-03 ==="

$Created['4.1'] = New-Story `
  -Title "Backend: AlbumPermission entity + EF migration + repository" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $Epic03 `
  -Status 'Ready' `
  -Body @'
## Context
EPIC-03 (#4) replaces the binary owner/guest model with per-(album, user) grants of View / Modify / Admin. This story stands up the persistence layer.

## Problem
There is no per-album permission table today. We need an entity, EF Core migration, and repository before any UI or policy work can begin.

## Proposed approach
- New `AlbumPermission` entity: `Id`, `AlbumId` (FK), `UserId` (FK), `Level` (enum: View | Modify | Admin), `GrantedAt`, `GrantedByUserId`.
- Unique index on `(AlbumId, UserId)`.
- EF Core 9 code-first migration; runs on startup.
- `IAlbumPermissionRepository` with `GetByAlbum`, `GetByUser`, `Upsert`, `Remove`.

## Acceptance criteria
- [ ] **Given** the migration runs, **when** the schema is inspected, **then** an `AlbumPermissions` table exists with the unique `(AlbumId, UserId)` constraint.
- [ ] **Given** two grants for the same `(album, user)`, **when** the second is inserted, **then** the unique constraint rejects it.
- [ ] **Given** a repository call to `GetByAlbum`, **when** invoked, **then** all permissions for that album return.
- [ ] xUnit unit tests cover repository CRUD with EF Core in-memory.

## Out of scope
- HTTP endpoints (separate story).
- Authorization policy enforcement (separate story).
- UI (separate story).

## Parent
Sub-story of #4 [EPIC-03] Album Permissions + audit log.
'@

$Created['4.2'] = New-Story `
  -Title "Backend: album permission grant/revoke endpoints + authorization policy" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $Epic03 `
  -Status 'Ready' `
  -Body @'
## Context
With the `AlbumPermission` entity in place, expose grant/revoke endpoints (owner + Admin only) and enforce permissions on album reads/modifies.

## Problem
Owners cannot share albums today; nothing enforces View/Modify/Admin levels on existing endpoints.

## Proposed approach
- `POST /api/albums/{id}/permissions` (body: `{ email, level }`) — only resolves to existing registered users; reject otherwise with `409 Conflict` "user must register first".
- `DELETE /api/albums/{id}/permissions/{userId}`.
- `GET /api/albums/{id}/permissions` — list grants.
- ASP.NET Core authorization policy `AlbumLevel:View|Modify|Admin` via a requirement + handler that inspects `AlbumPermission` (and falls back to ownership).
- Apply to existing album/photo endpoints.

## Acceptance criteria
- [ ] **Given** an owner, **when** they `POST` a grant for a registered email, **then** an `AlbumPermission` row is written.
- [ ] **Given** an owner, **when** they `POST` a grant for an unknown email, **then** the response is 409 with a clear message.
- [ ] **Given** a user without grants, **when** they call a Modify endpoint on an album they don't own, **then** they receive 403.
- [ ] **Given** any grant/revoke, **when** it persists, **then** an `AuditEvent` row is written.
- [ ] xUnit + WebApplicationFactory functional tests cover all paths.

## Out of scope
- Audit-event scaffold itself (separate story).
- Frontend UI.

## Parent
Sub-story of #4 [EPIC-03].
'@

$Created['4.3'] = New-Story `
  -Title "Backend: AuditEvent scaffold (entity + IAuditService + migration)" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $Epic03 `
  -Status 'Ready' `
  -Body @'
## Context
EPIC-03 stands up the audit log scaffold that all later epics consume (admin audit console, payments, moderation).

## Problem
There is no central audit table or service today. Each feature would otherwise invent its own.

## Proposed approach
- `AuditEvent` entity: `Id`, `Actor` (UserId or `guest:<accessCode>`), `Action` (string), `TargetType`, `TargetId`, `TimestampUtc`, `MetadataJson` (JSON column), `IpAddress` (nullable).
- EF Core migration; index on `(Actor, TimestampUtc)` and `(TargetType, TargetId)`.
- `IAuditService.Record(action, target, metadata)` — DI-registered, async, append-only.
- Reusable from the upcoming Admin Audit Console epic — coordinate via shared metadata schema.

## Acceptance criteria
- [ ] **Given** any feature calls `IAuditService.Record(...)`, **when** the call returns, **then** a row exists with all fields populated.
- [ ] **Given** the audit table grows, **when** inspected, **then** queries by actor or target use the new indexes (verified via EF logging).
- [ ] xUnit tests cover service + repository.
- [ ] DESIGN_DECISIONS.md updated with audit-log model.

## Out of scope
- Retention/archival policy (deferred).
- Admin UI for browsing audit log (separate epic).
- Download-specific audit events (handled by Admin Audit Console epic, which extends this scaffold).

## Parent
Sub-story of #4 [EPIC-03].
'@

$Created['4.4'] = New-Story `
  -Title "Backend: transfer album ownership endpoint" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $Epic03 `
  -Status 'Ready' `
  -Body @'
## Context
Owners must be able to transfer ownership of an album to another registered user. Old owner is downgraded to Admin.

## Problem
Today ownership is fixed at creation time.

## Proposed approach
- `POST /api/albums/{id}/transfer-ownership` body `{ newOwnerEmail }`.
- Resolve to existing User; 409 if not found.
- Single EF transaction: update `Album.OwnerUserId`; upsert `AlbumPermission` row for old owner at Admin level.
- Emit `AuditEvent`.

## Acceptance criteria
- [ ] **Given** an owner, **when** they transfer to a registered user, **then** the new owner has full owner rights and the old owner is now Admin.
- [ ] **Given** the new-owner email is not registered, **then** the transfer returns 409.
- [ ] **Given** a non-owner attempts transfer, **then** the response is 403.
- [ ] **Given** transfer succeeds, **then** an `AuditEvent` row is written.
- [ ] xUnit + functional tests.

## Out of scope
- UI (covered by FE permissions story).

## Parent
Sub-story of #4 [EPIC-03].
'@

$Created['4.5'] = New-Story `
  -Title "FE: album permissions UI (grant/revoke by email + transfer ownership)" `
  -Labels @('enhancement','area: frontend') `
  -ParentNodeId $Epic03 `
  -Status 'Ready' `
  -Body @'
## Context
Album owners and Admin-level users need a UI to manage per-album grants and trigger ownership transfer.

## Problem
No UI exists today; backend endpoints from sibling stories are unconsumed.

## Proposed approach
- Album detail → "Sharing" panel (gated by owner/Admin level).
- CoreUI Pro smart table listing existing grants (email, level, granted-at).
- Reactive form: email input + level select (View/Modify/Admin) + Grant button.
- Inline error if backend returns 409 ("user must register first").
- "Transfer ownership" button → confirm modal → calls transfer endpoint.
- Karma + Jasmine specs for the component, form validation, and HTTP error paths.

## Acceptance criteria
- [ ] **Given** an owner on the album page, **when** they open Sharing, **then** they see all current grants.
- [ ] **Given** the owner enters a registered email and selects Modify, **when** they submit, **then** the row appears in the table.
- [ ] **Given** the owner enters an unknown email, **when** they submit, **then** an inline message states "user must register first".
- [ ] **Given** a non-Admin, **when** they view the album page, **then** the Sharing panel is hidden.
- [ ] Component + service unit specs pass.

## Out of scope
- Album artwork upload (separate story).
- Audit log viewing (separate epic).

## Parent
Sub-story of #4 [EPIC-03].
'@

$Created['4.6'] = New-Story `
  -Title "FE+BE: album artwork — auto first-photo default + Modify+ custom upload" `
  -Labels @('enhancement','area: frontend','area: backend') `
  -ParentNodeId $Epic03 `
  -Status 'Ready' `
  -Body @'
## Context
EPIC-03 F5: album artwork defaults to the first photo; Modify+ users can upload a custom image.

## Problem
Albums currently have no first-class artwork field; covers are derived ad-hoc.

## Proposed approach
- Backend: `Album.CoverPhotoId` (nullable FK) + `Album.CustomCoverBlobKey` (nullable). Resolution rule: custom > first photo by `UploadedAt`.
- Endpoint `PUT /api/albums/{id}/cover` accepting either `{ photoId }` or multipart upload (gated by Modify+ policy).
- FE: album header shows resolved cover; "Change cover" button visible to Modify+ opens a chooser (existing photo OR upload).

## Acceptance criteria
- [ ] **Given** a new album with photos, **when** rendered, **then** the first uploaded photo is the cover.
- [ ] **Given** a Modify+ user uploads a custom cover, **when** the page reloads, **then** the custom cover replaces the auto cover.
- [ ] **Given** a View-level user, **when** they load the page, **then** "Change cover" is hidden.
- [ ] xUnit + Karma specs.

## Out of scope
- Crop/edit tooling (post-MVP).

## Parent
Sub-story of #4 [EPIC-03].
'@

# ============================================================================
# TASK 3b — Decompose #3 EPIC-02b Multi-IDP Account Linking
# ============================================================================
Write-Host "`n=== Decomposing #3 EPIC-02b ==="

$Created['3.1'] = New-Story `
  -Title "Backend: UserExternalIdentity entity + migration + race-protection unique constraint" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $Epic02b `
  -Status 'Ready' `
  -Body @'
## Context
EPIC-02b (#3) introduces multi-IDP account linking. This story stands up persistence.

## Problem
Today external identities are implicit on the User row; we cannot link a second provider without duplicating the user.

## Proposed approach
- `UserExternalIdentity` entity: `Id`, `UserId` (FK), `Provider` (string), `Subject` (string), `Email`, `LinkedAtUtc`.
- Unique constraint on `(Provider, Subject)`.
- Migration of existing users — backfill one row per existing User from its current login provider.
- Per-email creation lock (table-based or `SemaphoreSlim` keyed by lowercased email) to prevent duplicate Users on simultaneous first-time logins.

## Acceptance criteria
- [ ] **Given** the migration runs, **when** the schema is inspected, **then** the table exists and existing users have a backfilled row.
- [ ] **Given** two simultaneous first-time logins for the same email, **when** they race, **then** exactly one User row is created (verified by integration test).
- [ ] **Given** a duplicate `(Provider, Subject)` insert, **when** attempted, **then** the unique constraint rejects it.

## Out of scope
- Verification code service (separate story).
- Login flow integration (separate story).

## Parent
Sub-story of #3 [EPIC-02b].
'@

$Created['3.2'] = New-Story `
  -Title "Backend: email-verification code service (rate-limit, expiry, attempts)" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $Epic02b `
  -Status 'Ready' `
  -Body @'
## Context
Linking a new IDP requires emailing a 6-digit code to the existing account holder.

## Problem
There is no verification-code primitive in the codebase yet.

## Proposed approach
- `EmailVerificationCode` entity: `Id`, `Email`, `CodeHash`, `Purpose` (`LinkIdentity`), `IssuedAtUtc`, `ExpiresAtUtc`, `AttemptsRemaining`, `Consumed`.
- 6-digit code; 10-minute expiry; 3 attempt max; 5 codes per email per hour rate limit.
- Reuse existing `IEmailService` (Azure Communication Services / Mock).
- Service surface: `IssueAsync(email, purpose)`, `VerifyAsync(email, purpose, code)`.

## Acceptance criteria
- [ ] **Given** 5 codes already issued in the past hour for `x@y.com`, **when** a 6th issue is attempted, **then** the request returns rate-limited.
- [ ] **Given** a code issued >10 minutes ago, **when** verified, **then** verification fails with `Expired`.
- [ ] **Given** 3 wrong attempts on a code, **when** a 4th is tried, **then** verification fails with `LockedOut`.
- [ ] **Given** a successful verify, **when** repeated, **then** the second attempt fails (`AlreadyConsumed`).
- [ ] xUnit covers all paths.

## Out of scope
- Login-flow integration (separate story).
- FE prompt UI (separate story).

## Parent
Sub-story of #3 [EPIC-02b].
'@

$Created['3.3'] = New-Story `
  -Title "Backend: detect email-match-different-IDP at login + trigger verification flow" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $Epic02b `
  -Status 'Ready' `
  -Body @'
## Context
At login, when an external identity's email matches an existing User but `(Provider, Subject)` is new, we must trigger the link-verification flow rather than auto-create a duplicate User.

## Problem
Today the IDP abstraction either matches existing user or creates a new one; there is no "pending link" branch.

## Proposed approach
- Login pipeline: after token validation, look up by `(Provider, Subject)`; if miss, look up by verified email.
- If email match + new subject + IDP returns `email_verified=true` → issue a verification code via the new service, return `202 Accepted` with `{ linkChallengeId }`.
- If IDP did not return a verified email → reject with clear error (do not auto-link).
- New endpoint `POST /api/auth/link/verify` `{ linkChallengeId, code }` → on success, create `UserExternalIdentity`, issue session as the existing User.

## Acceptance criteria
- [ ] **Given** a User linked to Google with email `x@y.com`, **when** they sign in via Microsoft for the same email, **then** they receive a 6-digit code at `x@y.com` and on entry the Microsoft identity is added to `UserExternalIdentity`.
- [ ] **Given** an IDP that returns `email_verified=false`, **when** the user attempts sign-in, **then** auto-link is blocked with a clear error.
- [ ] **Given** a wrong code 3 times, **when** the user retries, **then** they must restart the flow.
- [ ] xUnit + functional tests.

## Out of scope
- FE prompt UI.

## Parent
Sub-story of #3 [EPIC-02b].
'@

$Created['3.4'] = New-Story `
  -Title "FE: link-verification prompt during login (enter 6-digit code)" `
  -Labels @('enhancement','area: frontend') `
  -ParentNodeId $Epic02b `
  -Status 'Ready' `
  -Body @'
## Context
When the backend returns a 202 link-challenge during login, the UI must prompt the user for the 6-digit code emailed to their existing account.

## Problem
Login UI only handles success/failure today.

## Proposed approach
- New route/component `/login/link-verify?challengeId=...`.
- 6-digit code input with auto-advance, paste support, resend button (rate-limited).
- Calls `POST /api/auth/link/verify`; on success, completes login and routes to landing.
- Karma + Jasmine spec coverage.

## Acceptance criteria
- [ ] **Given** an in-flight link challenge, **when** the user enters the correct code, **then** they are signed in as the existing account.
- [ ] **Given** a wrong code, **when** entered, **then** an inline error shows remaining attempts.
- [ ] **Given** an expired challenge, **when** the page loads, **then** the user is offered a "request new code" path.

## Out of scope
- Account-settings linked-identities panel (separate story).

## Parent
Sub-story of #3 [EPIC-02b].
'@

$Created['3.5'] = New-Story `
  -Title "FE+BE: Account Settings — list & unlink linked identities (cannot unlink last)" `
  -Labels @('enhancement','area: frontend','area: backend') `
  -ParentNodeId $Epic02b `
  -Status 'Ready' `
  -Body @'
## Context
Users need visibility into which IDPs are linked to their account and the ability to unlink (except the last one).

## Problem
Today there is no surface for managing external identities.

## Proposed approach
- Backend: `GET /api/me/identities`, `DELETE /api/me/identities/{id}` — reject if it would remove the last linked identity (409).
- FE: extend Account Settings page with a "Linked Sign-In Methods" panel listing each identity (provider icon, email, linkedAt) + Unlink button.
- Confirm modal; success toast; error state for "cannot unlink last identity".

## Acceptance criteria
- [ ] **Given** a user with 2 linked identities, **when** they unlink one, **then** only the remaining identity stays.
- [ ] **Given** a user with 1 linked identity, **when** they try to unlink it, **then** the request is rejected (409) and the UI shows a clear message.
- [ ] xUnit + Karma specs.

## Out of scope
- Linking *additional* providers from inside Settings (post-MVP — link still happens via login flow).

## Parent
Sub-story of #3 [EPIC-02b].
'@

# ============================================================================
# TASK 4 — File the new Admin Audit Console epic + 7 sub-stories
# ============================================================================
Write-Host "`n=== Creating Admin Audit Console epic ==="

$auditEpicBody = @'
## Context
Admins need visibility into who is downloading what and which carts are currently active. This is a new MVP-adjacent feature that should NOT block today's Azure deploy — fast-follow.

## Goal
Two admin views: (1) Active Carts overview, (2) Download Events log.

## View 1 — Active Carts
Per-user row: username (or `guest <code>` if unauth), account id, email (if known), # of items in cart, total size of items, last-updated timestamp.

## View 2 — Download Events
For every cart-driven download, capture:
- Date/time (UTC)
- Username + account id + email (if registered)
- Guest access code (if unauth)
- IP address (best effort — must honor `X-Forwarded-For` behind Azure App Service / Front Door / App Gateway)
- # pictures downloaded
- Which pictures (photo ids + album id, linkable)
- Total bytes downloaded
- Quality variant (original / web / thumb / watermarked)

## Admin UI requirements
- Route `/admin/audit` gated by `adminGuard`
- Filters: date range, user, album, access code
- Sortable columns
- CSV export of download log

## Backend requirements
- `DownloadAuditEvent` entity + EF migration
- Cart visibility: live aggregation query OR `CartSnapshot` read model — pick cheaper for MVP
- `/api/admin/audit/...` endpoints with `[Authorize(Roles="Admin")]`
- Must hook the existing download endpoint(s) — capture every cart-driven download
- **REUSE the `AuditEvent` scaffold from EPIC-03 (#4) if compatible**. Coordinate to avoid two parallel audit tables.

## Acceptance criteria (epic-level)
- [ ] Given a registered user downloads a cart, when the download completes, then a `DownloadAuditEvent` row exists with all required fields
- [ ] Given a guest user (access code) downloads a cart, when the download completes, then the event row captures the access code instead of user id
- [ ] Given an admin visits `/admin/audit`, when they switch tabs, then they see Active Carts and Download Events
- [ ] Given an admin filters/sorts the download log, when they click CSV export, then a CSV downloads with the filtered rows
- [ ] Non-admins receive 403 on `/api/admin/audit/*`

## Out of scope
Real-time push updates (polling acceptable for MVP). Long-term retention/archival. Per-photo download counters in the photo entity.

## Coordination
Coordinate with #4 (EPIC-03) author/owner on `AuditEvent` scaffold reuse before backend work begins.
'@

$auditEpicNum = New-Story `
  -Title "[Epic] Admin Audit Console — download events + active carts" `
  -Labels @('enhancement','epic','area: backend','area: frontend') `
  -Status 'InProgress' `
  -Body $auditEpicBody

$Created['audit-epic'] = $auditEpicNum
$auditEpicNodeId = (gh issue view $auditEpicNum --repo ArmyGuy255A/PhotoGallery --json id | ConvertFrom-Json).id

# Sub-stories
$Created['audit.1'] = New-Story `
  -Title "Backend: DownloadAuditEvent entity + migration + repository" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $auditEpicNodeId `
  -Status 'Ready' `
  -Body @'
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
'@

$Created['audit.2'] = New-Story `
  -Title "Backend: capture cart-driven downloads (proxy-aware IP, all required fields)" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $auditEpicNodeId `
  -Status 'Ready' `
  -Body @'
## Context
Every cart-driven download must produce a `DownloadAuditEvent` row capturing user/guest, IP, photo ids, bytes, quality variant.

## Problem
No telemetry exists at the download endpoint(s).

## Proposed approach
- Hook the existing cart-download endpoint(s) (locate via grep on `DownloadController` / `CartController`).
- IP capture must respect `Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware` (configure `KnownNetworks` / `KnownProxies` for Azure App Service + Front Door + App Gateway). Document the config in `DESIGN_DECISIONS.md`.
- Resolve user vs guest from `HttpContext.User` (registered) or the access-code claim/cookie.
- Emit one `DownloadAuditEvent` per download with full field set.
- Wrap in try/catch — capture failures must not break the user's download.

## Acceptance criteria
- [ ] **Given** a registered user downloads a cart of N photos, **when** download completes, **then** exactly one event row exists with the right user id, photo ids, byte total, and quality.
- [ ] **Given** a guest (access code) download, **when** complete, **then** the event captures the access code and `UserId` is null.
- [ ] **Given** the request arrives with `X-Forwarded-For: client, proxy`, **when** `ForwardedHeaders` is configured, **then** the captured IP is the client IP.
- [ ] **Given** the audit write fails, **when** the request returns, **then** the user still gets their download (with an error logged).
- [ ] WebApplicationFactory functional tests cover both paths and the proxy-IP case.

## Out of scope
- Admin endpoints/UI.

## Parent
Sub-story of the Admin Audit Console epic.
'@

$Created['audit.3'] = New-Story `
  -Title "Backend: /api/admin/audit/downloads endpoints + CSV export" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $auditEpicNodeId `
  -Status 'Ready' `
  -Body @'
## Context
Admin UI needs paged/filterable download-events queries plus a CSV export.

## Problem
No admin-facing audit endpoints today.

## Proposed approach
- `GET /api/admin/audit/downloads?from&to&userId&albumId&accessCode&page&pageSize&sort` — `[Authorize(Roles="Admin")]`.
- `GET /api/admin/audit/downloads.csv?...` — streams CSV with the same filter set, no paging.
- DTOs include linkable photo ids + album id.

## Acceptance criteria
- [ ] **Given** an admin requests `/api/admin/audit/downloads` with filters, **when** results return, **then** they match the filter and are sorted.
- [ ] **Given** the CSV endpoint, **when** invoked, **then** it streams a valid CSV with header + one row per event.
- [ ] **Given** a non-admin, **when** any endpoint is called, **then** the response is 403.
- [ ] xUnit + functional tests.

## Out of scope
- Active-carts endpoint (separate story).
- UI (separate story).

## Parent
Sub-story of the Admin Audit Console epic.
'@

$Created['audit.4'] = New-Story `
  -Title "Backend: /api/admin/audit/carts active-cart aggregation endpoint" `
  -Labels @('enhancement','area: backend') `
  -ParentNodeId $auditEpicNodeId `
  -Status 'Ready' `
  -Body @'
## Context
Admin UI needs an "active carts" overview: per-user, item count, total size, last-updated.

## Problem
Cart state lives per-user/session today; no aggregate view exists.

## Proposed approach
- Decision: **live aggregation** if cart storage is already DB-backed (preferred for MVP simplicity); otherwise add a `CartSnapshot` read-model updated on cart mutations.
- `GET /api/admin/audit/carts` returns rows: `{ userId | accessCode, displayName, email, itemCount, totalBytes, lastUpdatedUtc }`.
- `[Authorize(Roles="Admin")]`.

## Acceptance criteria
- [ ] **Given** registered + guest carts, **when** an admin queries the endpoint, **then** both appear, distinguished by `username` vs `guest <code>`.
- [ ] **Given** a non-admin, **when** they call the endpoint, **then** 403.
- [ ] Performance: endpoint responds <500ms with 1k active carts (load-tested).

## Out of scope
- Real-time push.
- UI.

## Parent
Sub-story of the Admin Audit Console epic.
'@

$Created['audit.5'] = New-Story `
  -Title "FE: /admin/audit route + Downloads tab (filter, sort, CSV)" `
  -Labels @('enhancement','area: frontend') `
  -ParentNodeId $auditEpicNodeId `
  -Status 'Ready' `
  -Body @'
## Context
Admin needs a `/admin/audit` page with a Downloads tab: filterable, sortable smart table + CSV export button.

## Problem
No admin audit UI exists.

## Proposed approach
- Route `/admin/audit` gated by `adminGuard`.
- CoreUI Pro tabbed layout — Downloads tab default.
- CoreUI Pro smart table bound to the new `/api/admin/audit/downloads` endpoint (server-side paging/sort).
- Filter form: date range, user, album, access code.
- "Export CSV" button calls the CSV endpoint with current filters.
- Karma + Jasmine specs for component, filter wiring, and CSV link.

## Acceptance criteria
- [ ] **Given** an admin, **when** they navigate to `/admin/audit`, **then** the Downloads tab loads with the most recent events.
- [ ] **Given** filters set, **when** applied, **then** the table updates and CSV export uses the same filters.
- [ ] **Given** a non-admin, **when** they navigate to the route, **then** they are redirected away (`adminGuard`).
- [ ] Karma specs cover filter form, sort, and export trigger.

## Out of scope
- Carts tab (separate story).

## Parent
Sub-story of the Admin Audit Console epic.
'@

$Created['audit.6'] = New-Story `
  -Title "FE: /admin/audit Carts tab (active carts overview)" `
  -Labels @('enhancement','area: frontend') `
  -ParentNodeId $auditEpicNodeId `
  -Status 'Ready' `
  -Body @'
## Context
Second tab on `/admin/audit`: active carts.

## Problem
No UI consumes the active-carts endpoint.

## Proposed approach
- "Active Carts" tab inside `/admin/audit`.
- Smart table bound to `GET /api/admin/audit/carts`.
- Columns: User (or `guest <code>`), Account ID, Email, # items, Total size (human-readable), Last updated.
- Manual refresh button (polling acceptable for MVP — no SignalR).
- Karma specs.

## Acceptance criteria
- [ ] **Given** the Carts tab, **when** opened, **then** rows render with sortable columns.
- [ ] **Given** the refresh button is clicked, **when** data changes server-side, **then** the table updates.
- [ ] **Given** a non-admin, **when** they hit the route, **then** `adminGuard` blocks them.

## Out of scope
- Live-push updates.
- Drilling into a cart's contents (post-MVP).

## Parent
Sub-story of the Admin Audit Console epic.
'@

$Created['audit.7'] = New-Story `
  -Title "Tests: e2e + unit coverage for admin audit flows" `
  -Labels @('enhancement','area: e2e') `
  -ParentNodeId $auditEpicNodeId `
  -Status 'Ready' `
  -Body @'
## Context
End-to-end + unit coverage for the Admin Audit Console.

## Problem
The other sub-stories ship feature-level tests; this one ensures the whole flow is exercised.

## Proposed approach
- Playwright spec: admin signs in → triggers a registered-user download → triggers a guest (access-code) download → opens `/admin/audit` → asserts both rows present with correct fields → exports CSV → verifies the file content.
- Page-object class for `/admin/audit`.
- Non-admin negative case: regular user is redirected from `/admin/audit`.
- Unit tests filling any gaps left by the feature stories.

## Acceptance criteria
- [ ] **Given** the e2e suite, **when** run against a fresh stack, **then** the audit spec passes.
- [ ] **Given** the CSV download in the spec, **when** parsed, **then** it contains the seeded events.
- [ ] **Given** a non-admin, **when** they navigate to `/admin/audit`, **then** the spec asserts the redirect.

## Out of scope
- Performance/load testing.

## Parent
Sub-story of the Admin Audit Console epic.
'@

# ============================================================================
# Promote #69, #70 to Ready (already story-sized; AC already present)
# ============================================================================
Write-Host "`n=== Promoting #69, #70 to Ready ==="
gh project item-edit --id 'PVTI_lAHOAXfsQM4BXNq-zgsTHrY' --project-id $ProjectId --field-id $StatusFieldId --single-select-option-id $StatusMap['Ready'] | Out-Null
gh project item-edit --id 'PVTI_lAHOAXfsQM4BXNq-zgsTHs0' --project-id $ProjectId --field-id $StatusFieldId --single-select-option-id $StatusMap['Ready'] | Out-Null
Write-Host "  #69, #70 → Ready"

# ============================================================================
# Summary
# ============================================================================
Write-Host "`n=== CREATED ISSUES ==="
$Created.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host ("  {0,-12} -> #{1}" -f $_.Key, $_.Value) }
$Created | ConvertTo-Json | Set-Content -Path temp/issues/created.json -Encoding UTF8
Write-Host "`nDone."
