# ADR 0017 — Member-visible team-members endpoint (Wave-1 debt) and self-scoped notification endpoints

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 1 debt (member-visible team members for the assignee picker); Wave 2 notifications; [`WAVE2_DESIGN.md`](../WAVE2_DESIGN.md) §5.3, §5.8, §8bis; frontend `features/tickets/useTeamMembers.ts` (documented gap)
- **Related ADRs:** 0007 (admin + team-membership authorization, resolve-then-check), 0010 (self-only `/api/me/*` no-id anti-IDOR posture), 0013 (notification model)

## Context

**Wave-1 gap:** the assignee picker needs a team's members, but there was no member-visible endpoint. `GET /api/admin/users` is admin-only; `/api/auth/me` exposes only the caller's own memberships. The frontend `useTeamMembers` hook documents this and degrades to an **empty** candidate pool for non-admin members — a real UI gap (a member cannot add a teammate as an assignee from the UI, though the backend would accept it). Wave 2's watch UI and label flows need the same member list.

**Wave 2 notifications** introduce user-owned resources (my notifications, my unread count, my email toggle) that must be addressable safely.

## Decision

- **`GET /api/teams/{id}/members` — `M(team)`.** Resolve the team → 404 if absent; `RequireTeamAccess(teamId)` → 403 for a non-member non-admin (resolve-then-check, ADR-0007). Returns the team's members as `TeamMemberDto(Id, DisplayName, IsAdmin)`, ordered by display name. `displayName = name?.Trim() || email` (computed server-side). This is a **read** limited to members of that team (or admins), so it discloses only teammates a member already collaborates with — no broader exposure than the board already implies.
  - Lives on `TeamsController` → `TeamService.ListMembersAsync`. The frontend `useTeamMembers` switches to it (drop the admin-only gate), fixing the assignee-picker gap and serving the watch/label member-pickers.
  - Scope of the response: **team members only** (admins are global and use the admin surface; the assignee-eligibility rule "team members ∪ admins" remains enforced server-side on assignment, so nothing is lost). Keeps the payload the minimal, common case.
- **Notification endpoints are Self (no id in path).** `GET /api/notifications`, `GET /api/notifications/unread-count`, `POST /api/notifications/{id}/read`, `POST /api/notifications/read-all`, `GET/PUT /api/me/notification-settings` all act on the authenticated principal. The one that takes a `{id}` (mark-one-read) resolves the row **scoped to `recipient_id = currentUserId`**; a row owned by another user → **404** (self-owned resource; 404-masking is correct here, distinct from the 403 used for team-scoped resources per ADR-0007 §3.3). This is the strongest anti-IDOR posture — the caller can only ever address their own notifications — consistent with the `/api/me/*` no-id design (ADR-0010).

## Consequences

- **Positive:** closes the Wave-1 assignee-picker gap for non-admin members; one reusable member list serves assignees, watchers, and label pickers; notification endpoints have no cross-user IDOR surface (Self by construction); reuses the established resolve-then-check (404-then-403) ordering for the team-members read and the Self-404 masking for notifications.
- **Negative:** `GET /api/teams/{id}/members` returns team members only, so an admin who is not a member of a team but wants to assign themselves must use the admin surface — accepted (admins are global and rare in the per-team picker). Reversible: include admins in the response if the PO wants it (additive).
- **Security:** the members read is team-scoped (a non-member gets 403); it exposes displayName + isAdmin only (no email beyond what displayName already surfaces, no status/blocked flags — those stay admin-only).
