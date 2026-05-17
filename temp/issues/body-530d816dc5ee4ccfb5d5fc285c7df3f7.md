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
