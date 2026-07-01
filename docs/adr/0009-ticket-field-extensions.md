# ADR 0009 — Ticket field extensions: priority (fixed dictionary), multiple assignees, date-only due date

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 1 approved features F-03 (priority), F-02 (assignees), F-08 (due date); [`WAVE1_DESIGN.md`](../WAVE1_DESIGN.md) §3–§6
- **Related ADRs:** 0002 (test DB / SQLite EnsureCreated — provider-agnostic model), 0003 (migrate-on-startup, parity guard), 0006 (status-code taxonomy: bad body reference ⇒ 400), 0007 (team-scope authorization)

## Context

Wave 1 extends the `Ticket` aggregate with three attributes that all surface on the board card, the ticket form, and the board filters. Each carries a modelling judgment call that must be explicit and testable, and all three ship in one migration against a single `AppDbContextModelSnapshot`.

## Decision

### A. Priority — fixed dictionary as text + CHECK, default `medium`
- Dictionary = **`low` | `medium` | `high` | `urgent`**, modelled as a `TicketPriority` enum and stored as **canonical lowercase text with a DB CHECK** — identical to `TicketType`/`TicketState` (ARCHITECTURE §4.2), so it is portable to SQLite (ADR-0002) and avoids PG-native-enum migration friction.
- **Default `medium`** for new tickets (app-set, mirroring the `state` default `new`) and for the **backfill** of existing rows (`AddColumn defaultValue: "medium"`, one statement, satisfies `NOT NULL` at add-time).
- Priority does **not** affect board ordering (stays `modified_at DESC`, A22); it is a filter + badge only. Sort-by-priority is out of Wave 1 scope (additive later).
- Rejected: PG native `enum` (not SQLite-portable, migration friction); an integer scale (loses self-describing canonical strings the rest of the API uses); making priority affect sort (would silently break the "recently touched floats up" model and drag-to-top).

### B. Assignees — many-to-many via explicit `TicketAssignee` join, full-set replace, team-member eligibility
- **Multiple** assignees (PO-confirmed) modelled as an explicit `ticket_assignees` join entity (like `UserTeam`), carrying `created_at` and directly queryable/diffable. Unique index `(ticket_id, user_id)`; `ticket_id` FK **CASCADE** (assignment is not standalone content, mirrors `Ticket→Comment`), `user_id` FK **RESTRICT** (mirrors `created_by`/`author_id`; no user-delete in scope).
- **Mutation = full-set replace** via `PUT /api/tickets/{id}/assignees { userIds }`, mirroring the established authoritative-full-set pattern (wip-limits, admin `PUT .../teams`). No add/remove-delta endpoints (layerable later). A no-op set does **not** advance `modified_at` (assignment is metadata, like a comment add, V21) so re-assigning does not reorder the board.
- **Eligibility:** assignable = members of the ticket's team **∪ admins**. An ineligible or unknown user id ⇒ **`400 validation_error`** keyed `userIds` (a bad reference in the body → 400, ADR-0006 §B), **not** 403 (403 is reserved for the caller's own lack of team access, which is checked first via `RequireTeamAccess`).
- **Wave-2 readiness:** all assignment goes through one `SetAssigneesAsync` that computes `added`/`removed` diffs; Wave 2 fans out notifications at that point with no contract change.
- Rejected: EF implicit join (cannot carry `created_at`, harder to diff); single-assignee FK (PO wants multiple); 403 for ineligible user (conflates payload validity with caller authorization).

### C. Due date — nullable `DateOnly` (calendar day, UTC), backend-computed `isOverdue`
- Optional **date-only** due date stored as `date` (`DateOnly?`), serialized `"YYYY-MM-DD"` (a calendar day, not an instant — no `Z`). Maps cleanly to PG `date` and SQLite `TEXT`.
- **`isOverdue` is backend-computed and returned:** `dueDate != null && dueDate < today(UTC from IClock) && state != done`. Single source of truth for "today"; no client-clock skew; testable over HTTP with `TestClock`. `dueDate` is also returned raw for "due soon" client styling.
- Rejected: `timestamptz dueAt` (timezone-of-day ambiguity for a calendar deadline; larger surface; not needed for Wave 1) — documented alternative. Rejected: frontend-computed overdue (per-client clock drift; QA cannot assert it deterministically).

### D. One migration, one snapshot
All three schema changes (priority column+CHECK+backfill, `due_date`, `ticket_assignees`) plus F-01's `password_reset_tokens` ship in a **single** `AddWave1` migration, generated **after** all model+config edits, to avoid a second/interleaved ModelSnapshot diff. Parity guard `has-pending-model-changes` (ADR-0003) must be clean.

## Consequences

- **Positive:** priority/state/type share one canonical-text+CHECK pattern (uniform validation, SQLite-portable); assignees are queryable, diffable, and Wave-2-notification-ready with a clean cascade policy; due-date overdue is unambiguous and testable; one migration keeps the snapshot conflict-free.
- **Negative:** full-set-replace assignees means the SPA must send the complete set (not a delta) — acceptable and matches the form UX; a no-op-doesn't-bump-`modified_at` rule for assignment is a deliberate call QA must know. A user removed from a team can leave a stale assignment on existing tickets (tolerated; cannot be re-added; optional Wave-2 cleanup).
- **Negative:** `priority` backfill relies on a migration-only `defaultValue`; the developer must ensure the model carries no lingering store default that would trip the parity guard (documented in WAVE1_DESIGN §3.1/§8.G).
