# ADR 0016 — Labels are team-scoped, member-managed, assigned by full-set replace, disposable on delete, and raise no events in Wave 2

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 2 approved scope (Labels/tags); [`WAVE2_DESIGN.md`](../WAVE2_DESIGN.md) §4.4/§4.8, §5.6/§5.7, §8
- **Related ADRs:** 0002 (normalized uniqueness key + text/CHECK portability), 0006 (error codes), 0007 (team-scope authorization), 0009 (assignee full-set-replace precedent)

## Context

Wave 2 adds labels/tags: a `Label` (team-scoped: `team_id`, `name`, `color`, unique per team) and a `TicketLabel` M:N join, with board filtering by label. Open decisions: who manages labels (members vs admins), the assign/remove endpoint shape, delete semantics (guard vs disposable), and whether label changes feed the event backbone.

## Decision

- **Team-scoped.** A `Label` belongs to a team; uniqueness is per-team, case-insensitive, via a `name_normalized` companion column + composite unique index `ux_labels_team_name (team_id, name_normalized)` (same pattern as team name, ADR-0002). Two teams may each have a "bug" label. Collision → **409 `duplicate_label_name`** (one new error code).
- **Member-managed (`M(team)`).** Any member of the team (or an admin) may create/rename/recolor/delete the team's labels and assign/remove them on that team's tickets. Justification: labels are collaborative daily-use metadata; gating creation behind admins adds friction that defeats the feature. This mirrors WIP-limits and epics being `M(team)` rather than admin-only. Sprawl risk is low and reversible (any member can delete a bad label). Reversal to admin-only is a one-line-per-method guard swap.
- **Assign by full-set replace.** `PUT /api/tickets/{id}/labels { labelIds: [...] }` — the authoritative complete set, de-duplicated, diffed (add/remove), mirroring `SetAssigneesAsync` (ADR-0009) and wip-limits. Eligibility: each `labelId` must exist AND belong to the ticket's team → else `400 validation_error` keyed `labelIds` (a bad body reference → 400, ADR-0006 §B). Does **not** bump `modified_at` (labels are metadata, like assignees).
- **Color** is `#RRGGBB`, validated by regex and lowercased in `LabelService`; the authoritative check is in the service (SQLite cannot easily regex-CHECK), consistent with how WIP bounds are service-enforced. `varchar(7)`.
- **Delete is disposable — no 409 guard.** Deleting a label removes it from all tickets via `TicketLabel` CASCADE and returns 204. Unlike epics (which RESTRICT-guard against referencing tickets, V12), a label is throwaway organizational metadata; blocking its deletion because it is in use would be user-hostile. `Team → Label` is CASCADE, but the existing `team_has_children` guard (tickets/epics) still governs team deletion — labels never block it.
- **No events in Wave 2 (W2-LABEL-NOEVENTS).** Label create/rename/delete and label assign/remove do **not** raise activity or notification events. Justification: labels are lightweight metadata (assignment was likewise event-less pre-Wave-2); wiring a `label_changed` event is a clean additive step later if the PO wants it in the timeline.

## Consequences

- **Positive:** members self-serve labels (no admin bottleneck); per-team uniqueness avoids cross-team collisions; full-set-replace reuses a proven, idempotent pattern and keeps the SPA simple (a multi-select whose value is the set); disposable delete matches user expectations; no event wiring keeps Phase 3 fully independent of the event backbone.
- **Negative:** label sprawl is possible (any member can create) — accepted (R-10), reversible to admin-only. Labels changes are invisible in the activity timeline — accepted, additive later.
- **Migration:** one migration `AddWave2Labels` (two tables + indexes); provider-agnostic; `EnsureCreated` builds it for tests.
