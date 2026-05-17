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
