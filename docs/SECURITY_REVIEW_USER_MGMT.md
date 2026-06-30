# Security Review — User Management / Authorization Model

- **Scope:** Feature "User Management" (admin role, team membership, blocking, admin-only Users zone), commit `65a317c`.
- **Type:** Authorized internal secure code review (defensive). No code changed.
- **Reviewer role:** Application Security Engineer.
- **Date:** 2026-06-30.
- **Method:** Static read of design (`USER_MANAGEMENT_DESIGN.md`, ADR-0007, ADR-0008) and the real backend code: auth pipeline (`BearerAuthMiddleware`, `CurrentUserAccessor`, `AuthService`), all team-scoped services (`Team/Epic/Ticket/Comment`), the admin zone (`UserAdminService`, `AdminUsersController`), DTOs, error infra, `Program.cs`, EF model (`AppDbContext`), the `AddUserManagement` migration, and the existing authorization-matrix/user-management test suites. Grep sweeps for raw SQL, secret logging, and mass-assignment surfaces.
- **Context:** Internal tool; RBAC is newly in scope and is the primary attack surface. Severity is rated for this internal-tool context (an authenticated, semi-trusted user base), not a hostile public internet.
- **OWASP focus:** A01 Broken Access Control (primary), A07 Auth failures, A04 Insecure Design (race conditions), A02 Crypto, A09 Logging.

---

## Executive summary

The authorization model is **well designed and, in the large, correctly implemented**. The core defensive intent of ADR-0007 — *server-side, per-resource enforcement on the resource's own `team_id` (not the request's), with resolve-then-check (404-then-403) ordering* — is implemented consistently across **every** team-scoped service method I inspected (board read, ticket detail/create/update/patch/delete, epic list/create/update/delete, comment list/add, wip-limits). The admin zone is genuinely **double-gated** (middleware + `RequireAdmin()` first line in every `UserAdminService` method). The two highest-impact classic vulnerabilities the design set out to prevent — **IDOR / cross-team access** and **privilege escalation via mass-assignment** — are **not present**: I could not find a team-scoped path that authorizes on the request's `teamId` instead of the resource's, and the privilege fields (`isAdmin`, `isBlocked`, `teamIds`) are simply **absent from every non-admin DTO** (signup, login, verify, me), so there is no over-posting surface. The **stale-session** risk (the item flagged as critical to check) is **handled correctly**: `IsAdmin` and `TeamIds` are loaded **fresh from the DB on every request** in `ResolveSessionUserAsync` and nothing about authorization is cached in the opaque token — a demote / team change / block takes effect on the user's very next request.

No **Critical** or **High** findings. There are **no production blockers** from a security standpoint.

The one substantive issue is a **TOCTOU race in the last-admin guard** (Medium): the guard does a non-transactional `COUNT(active admins)` and then an `UPDATE`, and on the `SetRoleAsync` (demote) path there is no surrounding transaction or locking at all. Two concurrent demote/block requests targeting the two final admins can each independently see "another active admin still exists" and both succeed, leaving the system with **zero usable admins** (a self-lockout / availability problem — exactly the invariant the guard exists to protect, INV-2 / ASR-3). It is low-likelihood (needs two simultaneous privileged requests at the precise two-admins-left moment) and the impact is operational lockout rather than data compromise, hence Medium. The remaining findings are Low / Info hardening items (generated-password lifetime in SPA/logs is correct server-side but worth a defense-in-depth note; reset-password does not re-verify the actor; admin enumeration via `email_in_use` is an accepted, documented trade-off).

---

## Findings (sorted by severity)

| ID | Severity | Area | File:line | Status | Exploit (1-line) | Recommendation |
|----|----------|------|-----------|--------|------------------|----------------|
| SEC-1 | **Medium** | Race / A04 last-admin guard | `UserAdminService.cs:123-141` (role) & `:186-213` (block) | CONFIRMED | Two admins left; fire concurrent demote-A + demote-B (or block both): each COUNTs "1 other active admin" before either commits → both succeed → 0 active admins, nobody can reach `/api/admin/*`. | Make guard-then-mutate atomic and serialized: run inside the existing execution-strategy transaction (as block/reset already do) AND take a write lock / re-count inside the tx (e.g. `SELECT ... FOR UPDATE` on the admin rows, or a guarded conditional `UPDATE ... WHERE`-with-count, or a unique "is the system-admin" sentinel). Cover with a concurrency test (currently none exists). |
| SEC-2 | **Low** | A04 / availability — `SetRoleAsync` not transactional | `UserAdminService.cs:131-138` | CONFIRMED | Even single-threaded, the demote path's guard + write are two separate round-trips with no tx; under Npgsql retry a partial/interleaved failure is theoretically possible (block/reset avoid this by using a tx). | Wrap the demote mutation in the same `CreateExecutionStrategy()` + `BeginTransactionAsync` pattern used by `BlockAsync`/`ResetPasswordAsync` (R-9). Folds into SEC-1's fix. |
| SEC-3 | **Low** | A09 / A04 — no audit log for privileged actions | `UserAdminService.cs` (whole file) | CONFIRMED | An admin who creates/blocks/demotes/resets another admin's account leaves no security audit trail; insider misuse or a compromised admin session is not attributable after the fact. | Emit a structured audit log (actor `CurrentUser.UserId`, target id, action, timestamp) for create/role/teams/block/unblock/reset — never including the generated password or hash. Defensive monitoring, not a vuln. |
| SEC-4 | **Low** | A02 / defense-in-depth — generated password returned in body | `UserAdminService.cs:118,263`; `CryptoPasswordGenerator.cs` | CONFIRMED | Plaintext password is returned in the HTTP response (by design, shown once). Server-side handling is correct (CSPRNG, never logged, only Argon2id hash stored), but the plaintext transits any proxy/SPA logging/browser history that captures response bodies. | Keep the once-shown design. Defense-in-depth: confirm no reverse-proxy/access-log captures response bodies; ensure the SPA never persists it (localStorage/console); document that TLS is mandatory for these endpoints. No server change required. |
| SEC-5 | **Info** | A01 (accepted) — admin self-demote/self-block guarded but self-actions allowed | `UserAdminService.cs:123-213` | CONFIRMED | An admin may act on their own account (UM-6); the only protection is the last-admin guard. With ≥2 admins, an admin can demote/block themselves — intended per design. | No change. Documented decision (UM-6). The last-admin guard correctly prevents the only dangerous case (sole admin). |
| SEC-6 | **Info** | A01 (accepted) — user enumeration via `email_in_use` on admin create | `UserAdminService.cs:91-93` | CONFIRMED | `POST /api/admin/users` returns `409 email_in_use` for an existing email, revealing account existence — but the caller is already a trusted admin. Public signup remains non-enumerating (`AuthService.SignupAsync:80`). | No change. Accepted trade-off (R-10, ADR-0007); correctly confined to the admin path. |
| SEC-7 | **Info** | A01 (accepted) — 403 (not 404) leaks resource existence to members | `Ticket/Epic/Comment/Team` services (resolve-then-check) | CONFIRMED | A member hitting another team's GUID gets `403`, confirming the id exists. GUIDs are non-enumerable, so leak value is negligible (documented §3.3). | No change. Conscious design decision; aligns with OWASP guidance against 404-masking as security-by-obscurity. |
| SEC-8 | **Info** | Migration / operational — all existing users promoted to admin | `20260630202157_AddUserManagement.cs:33` | CONFIRMED | `UPDATE users SET is_admin = true;` makes **every** pre-existing account an admin post-upgrade; if any legacy account is stale/compromised it now has global + admin-zone access. | No code change (zero-lockout is the intended behavior, ASR-5). Operational: after deploy, review the user list and demote accounts that should be members (already documented in ADR-0008 consequences). |

---

## Verified and safe (what is implemented correctly)

These were specifically probed and found sound — they are the bulk of the feature and the reason there are no High/Critical findings:

1. **No IDOR / cross-team access (A01) — the primary goal.** Every team-scoped mutation/read resolves the resource first and authorizes on the **resource's own `team_id`**, never on a client-supplied value:
   - `TicketService.GetByIdAsync:139`, `UpdateAsync:216`, `PatchStateAsync:292`, `DeleteAsync:317` — resolve-then-`RequireTeamAccess(ticket.TeamId)`.
   - `EpicService.UpdateAsync:99`, `DeleteAsync:133` — resolve-then-`RequireTeamAccess(epic.TeamId)`; team is **immutable on edit** (`:112`), so an epic can't be smuggled into another team.
   - `CommentService.ListAsync:31-32`, `AddAsync:51-52` — resolve the parent ticket's team, then check.
   - `TeamService.SetWipLimitsAsync:156-158` — resolve team, then check.
2. **Move-into-foreign-team is blocked (A01).** `TicketService.UpdateAsync` checks access on **both** the current team (`:216`) and the **target** team (`:250`) before allowing a team change — a member cannot push a ticket into a team they don't belong to. Confirmed tested (`AuthorizationMatrixTests` "Member_cannot_move_a_ticket_into_a_foreign_team...").
3. **404-then-403 ordering** is uniform (resolve → 404 if absent → 403 if present-but-foreign), tested for tickets/epics/comments.
4. **List endpoints filter to membership.** `TeamService.ListAsync:34-38` restricts non-admins to `TeamIds`; `EpicService.ListByTeamAsync` / `TicketService.GetBoardAsync` reject a non-member `teamId` with 403 after existence check.
5. **Admin zone is double-gated (A01/privilege).** `BearerAuthMiddleware:66-70` fast-403s a non-admin on `/api/admin/*`, **and** every `UserAdminService` method's first line is `_currentUser.RequireAdmin()` (`:43,69,125,147,188,219,237`). Direct/bypassed calls are still authorized. Tested table-driven across all 7 endpoints.
6. **No privilege escalation via mass-assignment (A01/A08).** `SignupRequest`/`LoginRequest`/`VerifyEmailRequest` (`AuthDtos.cs`) contain **no** `isAdmin`/`isBlocked`/`teamIds` fields; `UserDto` (me/login) is read-only output. Role/teams/block are mutable **only** through admin endpoints. `EpicService.UpdateAsync` and `TicketService` ignore/validate `teamId` correctly; ticket `CreatedBy` is server-set (`TicketService.CreateAsync:201`), comment `AuthorId` server-set (`CommentService.AddAsync:60`).
7. **STALE SESSION handled correctly (A07) — the flagged critical item.** `AuthService.ResolveSessionUserAsync:283-311` re-loads the user, re-checks `IsBlocked`/`EmailVerified`, and re-queries `IsAdmin` + `TeamIds` **from the DB on every request**. Nothing authorization-relevant is cached in the opaque token. A demote, team change, or block therefore applies on the user's next request — a former admin does **not** retain admin access until TTL.
8. **Blocked-user denial is multi-layered (A07).** Login refuses blocked (`AuthService.LoginAsync:134`, before the verified check); session resolution treats blocked as no session (`:304`); block purges all sessions in a transaction (`UserAdminService.BlockAsync:198-209`); resend-verification is non-committal for blocked (`AuthService:236`); reset-password refuses blocked (`UserAdminService:244`). Blocked users cannot login, ride an existing token, regain access via verification, or have a password reset.
9. **Crypto is correct (A02).** Generated passwords use `RandomNumberGenerator` (CSPRNG) with rejection-sampling via `GetInt32` (no modulo bias), guaranteed mixed classes, Fisher–Yates shuffle, 16 chars (`CryptoPasswordGenerator.cs`). Passwords stored only as Argon2id hash (`User.cs:18-19`); login uses constant-cost anti-enumeration dummy verify (`AuthService:119-122`).
10. **No injection (A03).** All persistence is EF LINQ (parameterized). The only raw SQL is the static, parameterless migration `UPDATE users SET is_admin = true;`. Board search uses `EF.Functions.Like` with explicit `%`/`_`/`\` escaping (`TicketService.EscapeLike:386-387`).
11. **No secret leakage in logs (A09).** Grep found no logging of password/token/hash. `ExceptionHandlingMiddleware` maps to a generic envelope and never leaks stack traces or provider internals to clients; email-send failures log without the token (`AuthService:407`).
12. **No CSRF exposure.** Auth is `Authorization: Bearer` header (not cookies), so cross-site form/credential auto-submission does not apply; CORS is locked to a single origin in production (`Program.cs:80-102`).
13. **Atomicity for block/reset (A04).** Both use `CreateExecutionStrategy()` + explicit transaction to set the flag/hash and purge sessions together (`UserAdminService:199-209, 251-261`), per the Npgsql retry constraint (fix 14e4424).
14. **Committed config uses placeholders, not real secrets** (`.env.example`: `change-me-local`, `replace-with-a-long-random-secret`, empty `SMTP_PASSWORD`).

---

## Quick wins

1. **SEC-1/SEC-2 (one fix):** wrap the `SetRoleAsync` demote and reuse the `BlockAsync` transaction so the last-admin **count + mutate** is atomic and serialized (lock the admin set inside the tx, or use a conditional `UPDATE`). Add a concurrency test firing two parallel demote/block requests at the final two admins and asserting exactly one succeeds. This closes the only non-Info finding and the single untested invariant.
2. **SEC-3:** add structured audit logging for all 7 admin actions (actor, target, action, time — no secrets). Pure addition, high defensive value for an internal tool.
3. **SEC-4 (verify, don't change):** confirm reverse-proxy access logs do not capture response bodies and the SPA never persists the once-shown password; add a one-line note that these endpoints require TLS.
4. **SEC-8 (operational checklist item):** add a post-deploy step "review promoted admins and demote non-admins" to the release runbook.

---

## Security verdict: **GO** (no production blockers)

- **Basis:** The new authorization surface is enforced server-side per resource with consistent resolve-then-check; IDOR, privilege-escalation/mass-assignment, stale-session, and blocked-bypass — the four highest-impact risks for this feature — are all correctly mitigated and (mostly) covered by tests. No Critical/High findings.
- **Blockers:** none.
- **Recommended before/shortly after release (not blocking):** SEC-1 (last-admin TOCTOU — Medium, availability/self-lockout only, low likelihood) and its concurrency test; SEC-3 audit logging.
- **Accepted residual risk (owner sign-off, already documented in design):** SEC-6 (admin-path enumeration), SEC-7 (403 existence leak on non-enumerable GUIDs), SEC-8 (mass admin promotion on migration — operational review step).
- **Not verified / out of scope of this static review:** no DAST was run (no authorized running environment was provided); SPA-side handling of the once-shown password (frontend) and TLS/reverse-proxy logging configuration were assessed only by design/inference, not by inspecting the deployed proxy. Argon2id hasher parameters were assumed correct from prior ADR-0001 (not re-audited here).
