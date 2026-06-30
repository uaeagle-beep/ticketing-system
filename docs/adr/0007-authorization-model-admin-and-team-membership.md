# ADR 0007 — Authorization model: admin role + per-team membership, enforced server-side per resource

- **Status:** Accepted
- **Date:** 2026-06-30
- **Deciders:** Architect
- **Source refs:** User Management approved requirements §1–§11; [`USER_MANAGEMENT_DESIGN.md`](../USER_MANAGEMENT_DESIGN.md)
- **Related ADRs:** 0001 (auth strategy / bearer sessions), 0006 (error codes), 0008 (migration + last-admin guard)

## Context

The system previously had **no authorization model**: every verified user could read and mutate every team's data (REQUIREMENTS_ANALYSIS explicitly states "no roles, admins, or per-team membership"). The approved User Management feature introduces:

- a global **admin** principal that ignores team scoping;
- **member** principals constrained to teams they belong to (many-to-many `User ↔ Team`);
- an **admin-only "Users" zone** for account lifecycle;
- account states `isBlocked` (hard auth denial) and `isAdmin` (privilege).

The dominant risk is **Broken Access Control (OWASP A01)** and **IDOR** on team-scoped resources (board, epics, tickets, comments, wip-limits, team settings). The requirements mandate that enforcement be **server-side per resource**, not merely list filtering.

### Options considered

1. **Filter lists only, trust the SPA for the rest.** Rejected — classic broken-access-control; a direct API call to `/api/tickets/{idOfOtherTeam}` would leak data. Fails the requirement.
2. **ASP.NET `[Authorize]` policies / attributes on controllers.** Useful for the coarse admin-zone gate, but cannot express "member of the *resource's* team" (data-dependent, requires loading the resource). Keeping authorization in attributes also splits authoritative rules across HTTP and the Application layer, contrary to ARCHITECTURE §3.2 (thin controllers, HTTP-agnostic services, all rules re-checked against the DB).
3. **Authorization in the Application services, identity loaded into `ICurrentUser` (chosen).** The bearer middleware loads `isAdmin` + membership team ids into `ICurrentUser`. Each team-scoped service resolves the resource, then calls a single `RequireTeamAccess(teamId)` helper (`admin ⇒ allow`; `member ⇒ allow iff member of that team`). The admin zone calls `RequireAdmin()`. This is unit-testable without HTTP, guarantees a bypassed/direct call is still checked, and matches the existing pattern where every rule is enforced in the service.

## Decision

- Extend `ICurrentUser` with `IsAdmin`, `TeamIds`, `RequireAdmin()`, `CanAccessTeam(teamId)`, `RequireTeamAccess(teamId)`. The bearer middleware populates these after session resolution.
- **Authentication layer** (`BearerAuthMiddleware` / `AuthService.ResolveSessionUserAsync`): reject blocked users → `401`; load `isAdmin` + memberships.
- **Authorization layer** (Application services): admin-zone services call `RequireAdmin()` first; team-scoped services resolve the resource and call `RequireTeamAccess(resource.TeamId)`. Lists filter to the member's teams; admins see all.
- **403-vs-404 ordering:** resolve first → absent = `404 not_found`; present-but-not-accessible = `403 forbidden`. GUID ids make leaking existence negligible; `403` is honest and keeps the contract clean.
- **New error codes (ADR-0006 taxonomy extension):** `forbidden` (403), `account_blocked` (401), `last_admin_required` (409), `email_in_use` (409, admin create path only — public signup stays non-enumerating).
- **Blocked login → 401 `account_blocked`** (not 403): a blocked account is treated as "not authenticated" uniformly across login and mid-session, so the SPA's existing 401 handler works unchanged and a 403-at-login credential-acceptance leak is avoided. Reversible if the BA insists on 403 at login (one-line change in `LoginAsync`).
- **Admins bypass scoping**; memberships on an admin are ignored while `isAdmin=true`.

## Consequences

- **Positive:** single, testable choke point per concern (`RequireAdmin`, `RequireTeamAccess`); direct-API/IDOR attempts are blocked at the service regardless of the SPA; uniform 401 for both unauthenticated and blocked simplifies the client; taxonomy extension is minimal (4 codes, one HTTP status each).
- **Negative:** every team-scoped service method must call `RequireTeamAccess` — an omission is a vulnerability. Mitigated by table-driven negative tests covering each endpoint and a code-review checklist item. Resolving the resource before authorizing adds one lookup on detail/mutation paths (acceptable; these already load the entity).
- **Negative:** existing tests assumed full access for any verified user and must be updated (the default test principal becomes admin for business-rule tests; dedicated member/blocked principals for authz tests). Scoped, mechanical change.
- **Migration coupling:** see ADR-0008 (existing users promoted to admin; last-admin guard).
