# Automated Test Report — Ticket Tracker

_Generated: 2026-07-01 · commit `d08308e` (main)_

## 1. Regression run — summary

| Suite | Type | Files | Tests | Result | Duration |
|---|---|---|---:|---|---|
| **Backend** (`dotnet test`) | integration (HTTP) + unit | 16 | **234** | ✅ **234 passed / 0 failed / 0 skipped** | ~31 s |
| **Frontend** (`vitest`) | unit + component (jsdom) | 24 | **168** | ✅ **168 passed / 0 failed** | ~17 s |
| **Total automated (unit/component/integration)** | | 40 | **402** | ✅ **all green** | |
| Playwright E2E — **smoke** | browser (vs live prod) | 1 spec | **6** | ✅ **6 passed** (against https://honcharenko.pp.ua) | ~17 s |
| Playwright E2E — happy-path | browser end-to-end | 1 spec | **1** | ✅ **1 passed** on an isolated server stack (`tt-e2e` + Mailpit); CI-wired (`e2e` job) | ~10 s |

**Verdict: GO** — full regression is green; no failures, no skips.

## 2. How the tests run

- **Backend:** xUnit + FluentAssertions. Integration tests boot the real API via `WebApplicationFactory<Program>` over **in-memory SQLite** (`EnsureCreated`, `PRAGMA foreign_keys=ON`), exercising real HTTP + EF Core + the full middleware/auth pipeline **without Docker or PostgreSQL**. `IEmailSender`/`IClock` are faked. A couple of pure service unit tests use SQLite directly.
  - Run: `cd backend && dotnet test TicketTracker.sln`
  - Migration parity guard: `dotnet ef migrations has-pending-model-changes` → "No changes".
- **Frontend:** Vitest + React Testing Library in **jsdom**; network mocked with **MSW** (no backend needed).
  - Run: `cd frontend && npm test`  (build check: `npm run build`)
- **E2E (not part of this run):** Playwright — `smoke.spec.ts` (public pages, client validation) and `happy-path.spec.ts` (signup → verification link via Mailpit → login → team → epic → ticket → comment → drag). Requires `docker compose -f docker-compose.yml -f docker-compose.e2e.yml up` + `npm run e2e`; wired into CI (`.github/workflows/ci.yml`).

## 3. Backend coverage (202 tests)

| Test file | Tests | Area covered |
|---|---:|---|
| `Api/AuthFlowTests.cs` | 14 | Signup (non-enumerating), login (verified-only, equal-cost anti-enumeration), logout, verify-email (single-use, 24h expiry, resend invalidates prior), `/me`, unverified→403 |
| `Api/AuthorizationMatrixTests.cs` | 22 | **Access control**: admin-zone (member→403, anon→401, admin→ok), IDOR/team-scope on tickets/epics/comments/wip/team CRUD (read+write, 404-then-403 ordering, move-into-foreign-team), member team-list filtering, admin sees all |
| `Api/UserManagementTests.cs` | 20 | Admin user CRUD: create (chosen/generated password, active+pre-verified, dup email→409, unknown team→400), set role (last-admin guard), set teams, block/unblock (blocked login→401, sessions purged), reset password (once, purge, blocked→403), self-signup→Demo team, `/me` shape, SEC-4 no-store headers |
| `Api/TicketsTests.cs` | 18 | Ticket create (all fields), enum validation, `epic_team_mismatch` (create+update), `modified_at` rules (advance vs no-op), state change, delete cascades comments, 404/400 |
| `Api/WipLimitsTests.cs` + `Api/WipLimitsCoverageTests.cs` | 16 + 19 | WIP limits: set/validate (0/neg/fractional/non-numeric/>999/unknown-state→400, 401/404, below-count allowed); enforcement `409 wip_limit_reached` on create/PATCH/PUT/team-change; no-op & exit allowed; unlimited; board `total`/`wipLimit`; cross-team |
| `Api/UserNameTests.cs` | 13 | Display **Name**: create/set/clear, validation (>100→400, whitespace→null), name in `/me` / admin list / `createdByName` / `authorName`, set-name 404/403 |
| `Api/TeamsTests.cs` | 11 | Team create/list/rename/delete, case-insensitive uniqueness→409, blank→400, delete-with-children→409, no-op rename |
| `Api/EpicsTests.cs` | 10 | Epic CRUD, blank title→400, unknown team→400, team immutable, delete-referenced→409, list scoped to team |
| `Api/BoardTests.cs` | 9 | Exactly 5 columns in workflow order, within-column sort (modified desc), filters (type/epic/title search, AND), empty/unknown team |
| `Api/CommentsTests.cs` | 6 | Add comment, oldest-first, blank→400, unknown ticket→404, does NOT bump ticket `modified_at`, empty list |
| `Api/SecurityRegressionTests.cs` | 2 | Security regression guards (from secure-review) |
| `Unit/TicketServiceModifiedAtTests.cs` | 4 | `modified_at` no-op semantics at service level |
| `Unit/LastAdminGuardConcurrencyTests.cs` | 3 | **Last-admin guard race** (TOCTOU): parallel demote/block of the last two admins → exactly one succeeds, ≥1 admin remains |

## 4. Frontend coverage (145 tests)

| Test file | Tests | Area covered |
|---|---:|---|
| `features/users/usersFilter.test.ts` | 14 | User-list filter fn: search (name OR email, case-insensitive), role, team, verified, status, AND-combination |
| `features/users/UsersPage.test.tsx` | 13 | Users admin page: list (role/teams/status/created), create dialog + generated-password-once, filtering (all fields + Clear + empty state), `displayName` cell |
| `lib/time.test.ts` | 12 | Relative + UTC time formatting |
| `features/board/FilterBar.test.tsx` | 11 | Board filters: type/epic/search/Clear/count |
| `lib/errors.test.ts` | 11 | API error-envelope → message mapping (incl. new codes: `forbidden`, `account_blocked`, `wip_limit_reached`, …) |
| `features/board/keyboardCoordinates.test.ts` | 10 | Keyboard drag-and-drop coordinate getter (arrows → neighbour column, no wrap) |
| `components/ConfirmDialog.test.tsx` | 8 | Modal: focus-on-open, Escape, focus-trap (Tab/Shift+Tab), focus restore, confirm |
| `features/board/BoardColumn.test.tsx` | 7 | Five columns, UPPERCASE header, count / WIP badge states |
| `features/board/TicketCard.test.tsx` | 7 | Card render (type/title/epic/time), open on Enter/click not Space, drag-handle aria |
| `features/users/UsersFilterBar.test.tsx` | 7 | Filter-bar controls (aria labels, role/verified/status change, Clear) |
| `components/States.test.tsx` | 7 | Loading / empty / error (+retry) states |
| `lib/labels.test.ts` | 7 | State/type human labels |
| `features/auth/LoginPage.test.tsx` | 6 | Fields, resend on 403 unverified, anti-enumeration message, **blocked-account** message |
| `api/tokenStore.test.ts` | 5 | Token set/get/clear + localStorage mirror + subscribe |
| `features/board/useBoard.test.tsx` | 5 | Board normalize (always 5 cols, sort), optimistic move + rollback on error |
| `lib/displayName.test.ts` | 5 | `displayName = name || email` rule |
| `features/auth/SignupPage.test.tsx` | 4 | Min-length, confirm-mismatch, success banner, server-error banner |
| `auth/RequireAuth.test.tsx` | 4 | Redirect to /login when no token / unverified / 401 |
| `auth/RequireAdmin.test.tsx` | 2 | Admin-only route guard |

## 5. Coverage by feature

| Feature | Backend | Frontend | E2E |
|---|---|---|---|
| Authentication (signup/login/logout/verify/resend) | ✅ AuthFlow | ✅ Login/Signup/RequireAuth | ✅ happy-path |
| Authorization: admin role, team-scope, IDOR, blocking | ✅ AuthorizationMatrix, UserManagement, LastAdminGuard | ✅ RequireAdmin, UsersPage | ➖ |
| User management (CRUD, roles, teams, block, reset pw) | ✅ UserManagement | ✅ UsersPage/FilterBar | ➖ |
| Display Name + user filtering | ✅ UserName | ✅ usersFilter, UsersPage, displayName | ➖ |
| Teams | ✅ Teams | ➖ (via pages) | ✅ |
| Epics | ✅ Epics | ➖ | ✅ |
| Tickets + rules (modified_at, epic-team) | ✅ Tickets, modified-at unit | ✅ TicketCard | ✅ |
| Kanban board (columns, sort, filters, DnD) | ✅ Board | ✅ BoardColumn/FilterBar/useBoard/keyboardCoordinates | ✅ (drag) |
| Comments | ✅ Comments | ➖ | ✅ |
| WIP limits | ✅ WipLimits(+Coverage) | ✅ BoardColumn (badge) | ➖ |
| Error handling / envelope | ✅ (across suites) | ✅ errors | ➖ |
| Accessibility (focus trap, keyboard DnD, aria) | — | ✅ ConfirmDialog, keyboardCoordinates, FilterBar | ➖ |

## 6. Not covered by the automated run (honest gaps)

- **Playwright E2E — smoke** was executed against live prod (6/6 ✅). The **happy-path** spec has been **updated for the User-Management authz model**: after signup → verify → login it promotes the account to admin directly in the e2e Postgres (`docker compose exec db psql`) and reloads (the SPA refetches `/me`; isAdmin is read fresh per request), then drives team → epic → ticket → comment → drag → reload-persists. It is wired into the CI `e2e` job (Docker stack + Mailpit) and was **verified green (1 passed) on an isolated server stack** (separate compose project `tt-e2e` on port 8090 + Mailpit, prod untouched). Running it surfaced and fixed three stale selectors in the original (never-executed) spec: `Teams` heading and the epic cell needed `exact` matching, and team navigation now uses the nav link (robust when teams already exist / an admin sees all teams).
- **PostgreSQL-specific paths**: integration tests use SQLite (`EnsureCreated`). The Npgsql data-migration (existing users → admin), citext/collation nuances, and serializable-retry under real concurrency are exercised only on the Postgres/prod path + the CI parity guard — verified manually on deploy, not in this unit run.
- **Real SMTP / email delivery** (`IEmailSender` is faked) — verified manually on prod (relay1/mail.honcharenko.pp.ua).
- **Docker build / `docker compose up`** is not a test — validated on each deploy.
- **Performance / load** (e.g., "board usable with 100+ tickets" NFR) is not automated.
- **Real-browser visual & a11y** (colour contrast, screen-reader behaviour, visual DnD travel) — reasoned/statically covered, not automated.
- **User self-service profile** (self-edit of own Name) — out of scope, not implemented.

## 7. Raw regression output

```
Backend:  Passed! - Failed: 0, Passed: 234, Skipped: 0, Total: 234  (net10.0)
Frontend: Test Files 24 passed (24) · Tests 168 passed (168)
```

## 8. Gap-fill additions — User Management QA (2026-07-01)

A dedicated QA gap analysis of User Management added **55 tests** (backend +32, frontend +23), all green — no product defects found.

| New test file | Tests | Gaps closed |
|---|---:|---|
| `Api/UserAdminCrudGapsTests.cs` | 26 | set-teams (add/remove/empty/dedupe, unknown team→400, unknown user→404, idempotent, no role change), set-role idempotency, create validation (email format/blank, password min/max, isAdmin=true, teamIds=[]), block/unblock/reset on unknown→404, blocked can't resend, admin-list field completeness for filtering |
| `Api/SelfSignupAndMeGapsTests.cs` | 6 | self-signup when default team missing (no team, isAdmin=false) vs present (only that team); `/me` teams for member vs admin |
| `features/users/CreateUserDialog.test.tsx` | 7 | validation, request body shape, chosen vs generated password, email_in_use / validation errors |
| `features/users/EditUserDialog.test.tsx` | 6 | last-admin guard UI (409 → revert toggle), role/teams/name set-clear, no-op |
| `features/users/ResetPasswordDialog.test.tsx` | 3 | password shown once, blocked refusal, cancel |
| `features/users/GeneratedPasswordNotice.test.tsx` | 3 | copy-to-clipboard + "Copied" state |
| `components/AppLayout.test.tsx` | 4 | header displayName (name→name, blank→email), admin-only "Users" nav visibility |
