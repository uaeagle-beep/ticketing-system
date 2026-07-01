# Ticketing System — REST API Contract

> **Status:** Authoritative. Developers implement endpoints **exactly** as specified here.
> **Companion:** [`ARCHITECTURE.md`](./ARCHITECTURE.md). Decisions: [`adr/`](./adr/).
> **Base path:** `/api` (proxied by nginx to the API container). **Base URL (dev):** `http://localhost:8080`.
> **Content type:** `application/json; charset=utf-8` for all request and response bodies unless noted.
> **Time:** all timestamps are ISO-8601 UTC with trailing `Z`. **Enums:** canonical lowercase exactly as listed.
> **IDs:** UUID (string).

---

## 1. Authentication & transport

- **Scheme:** opaque bearer token (DB-backed session) — **[ADR-0001]**.
- **Header:** `Authorization: Bearer <token>` on every authenticated endpoint.
- **Tokens are NEVER placed in URLs.** The only token allowed in a URL is the single-use email-verification token, and only in the emailed link (source §9, **[ADR-0006]**).
- **Public (no auth):** `POST /api/auth/signup`, `POST /api/auth/login`, `POST /api/auth/verify-email`, `POST /api/auth/resend-verification`, `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`, `GET /health/live`, `GET /health/ready`, and static frontend assets.
- **Authenticated (token required):** everything else, including `GET /api/auth/me`. Missing/invalid/expired/logged-out token → **401**.
- **Verified required:** authenticated endpoints additionally require `email_verified=true`. An authenticated-but-unverified state is not reachable because login does not issue a session to unverified accounts (it returns 403 first). A token whose user somehow became unverified → **403 account_not_verified**.
- **Blocked users (ADR-0007):** a blocked account cannot authenticate. Login returns **401 account_blocked**; any surviving session token resolves to **401 unauthorized** (blocked sessions are purged on block). "Blocked == not authenticated" is uniform across login and every protected request.
- **Authorization (ADR-0007):** two principal kinds. An **admin** (`isAdmin=true`) ignores team scoping. A **member** may only read/write resources of teams they belong to. `/api/admin/*` requires `isAdmin` (else **403 forbidden**). Team-scoped endpoints (teams list/wip-limits/members, epics, tickets, board, comments) require admin-or-membership; team **create/rename/delete** are admin-only. Comment **edit** is author-only; comment **delete** is author-or-admin (F-12, ADR-0015). Enforcement is **server-side per resource**, not merely list filtering.

---

## 2. Error envelope & status-code taxonomy **[ADR-0006]**

Every non-2xx response uses this exact shape:

```json
{
  "error": {
    "code": "validation_error",
    "message": "Human-readable summary.",
    "errors": { "email": ["Email is required."], "password": ["Must be at least 8 characters."] }
  }
}
```

- `code` — stable machine string (table below). `message` — human-readable. `errors` — optional per-field map, present for `validation_error`.

| HTTP | `code` | When |
|---|---|---|
| 400 | `validation_error` | Malformed body, empty-after-trim, bad email syntax, password < 8 or > 1024, oversized field, **invalid enum** (`type`/`state`), **referenced entity in body does not exist** (unknown `teamId`/`epicId`) |
| 400 | `epic_team_mismatch` | Ticket `epicId` belongs to a team other than the ticket's `teamId` |
| 401 | `unauthorized` | No/invalid/expired/logged-out bearer token; or a token whose user is now blocked |
| 401 | `invalid_credentials` | Login: wrong password or unknown email (anti-enumeration, identical message) |
| 401 | `account_blocked` | Login (or session resolution) for a blocked account (ADR-0007) |
| 403 | `account_not_verified` | Login with correct creds on an unverified account |
| 403 | `forbidden` | Authenticated but not allowed: non-admin in admin zone; member acting on a non-member team's resource; reset-password on a blocked user (ADR-0007) |
| 404 | `not_found` | Resource addressed in the URL path (`/{id}`) does not exist |
| 409 | `duplicate_team_name` | Team create/rename collides case-insensitively |
| 409 | `duplicate_label_name` | Label create/rename collides case-insensitively **within the team** (Wave 2, ADR-0016) |
| 409 | `team_has_children` | Delete team that has tickets or epics |
| 409 | `epic_referenced_by_tickets` | Delete epic referenced by ≥1 ticket |
| 409 | `wip_limit_reached` | Create/move a ticket INTO a (team, state) whose WIP limit is already reached |
| 409 | `last_admin_required` | Demote/block/delete that would leave zero active admins (ADR-0008) |
| 409 | `email_in_use` | Admin create-user with an email that already exists (admin zone only; public signup stays non-enumerating) |
| 400 | `invalid_or_expired_token` | verify-email: token unknown, consumed, or expired |

**Rule:** `400` = bad/ill-formed payload (incl. a non-existent reference passed in the body); `404` = the URL-path resource is absent; `409` = conflict with persisted state (uniqueness or protective delete-guard). Applied uniformly.

**403-vs-404 ordering (ADR-0007):** for a team-scoped resource addressed by id, resolve the resource **first**. If it does not exist → **404 not_found**. If it exists but the caller (a member) has no access to its team → **403 forbidden**. GUID ids make leaking existence negligible; this keeps the 404 semantics for genuinely missing ids intact.

---

## 3. Auth endpoints

### 3.1 `POST /api/auth/signup` — public

Creates an unverified account and sends a verification email. No session is issued (no auto-login).

**Request**
```json
{ "email": "  Alex@DataArt.com ", "password": "correct horse battery" }
```
- `email`: required; trimmed + lowercased for the uniqueness key; must be syntactically valid (V1, A6).
- `password`: required; **≥ 8** and **≤ 1024** chars (A5). (Confirm-password is client-only, not sent.)

**201 Created**
```json
{ "message": "Account created. Please check your email to verify your account before logging in." }
```
**Errors:** `400 validation_error` (bad email, password length, blank). Duplicate email → **409 duplicate_email**? **No** — to avoid enumeration, duplicate sign-up returns the **same 201 message** and does NOT create a second account; if you prefer an explicit signal, the SPA relies on the verify/login flow. (Implementers: silently no-op on existing normalized email, still 201, do not leak existence — consistent with A8.)

> Note: the BA's US-AUTH-1 shows a "duplicate email" message as acceptable client UX; the authoritative backend behavior here is the non-enumerating 201. The login/verify flow still works for the real owner.

### 3.2 `POST /api/auth/login` — public

**Request**
```json
{ "email": "alex@dataart.com", "password": "correct horse battery" }
```
- Email matched case-insensitively after trim.

**200 OK** (verified account)
```json
{
  "token": "9f2b...base64url-opaque-256bit...",
  "user": { "id": "8e29c1b4-...", "email": "alex@dataart.com", "name": "Alex Doe", "emailVerified": true,
            "isAdmin": false, "isBlocked": false, "teams": [ { "id": "f1...", "name": "Platform" } ] },
  "expiresAt": "2026-07-03T11:26:00Z"
}
```
- `user` carries the authorization context (`isAdmin`, `isBlocked`, `teams[]`) so the SPA can bootstrap nav + team-selector from a single response (mirror of `/api/auth/me`).
- `name` is the optional **display name** (`null` when unset). Display rule everywhere a person is shown: `displayName = name?.trim() || email`. Email stays the login/account key; `name` is purely cosmetic.

**Errors:**
- `401 invalid_credentials` — wrong password OR unknown email (identical response, A3).
- `401 account_blocked` — correct creds on a blocked account (ADR-0007). Checked **before** the unverified branch, so a blocked-and-unverified account reports blocked:
```json
{ "error": { "code": "account_blocked", "message": "This account has been blocked. Contact an administrator." } }
```
- `403 account_not_verified` — correct creds, unverified (A4):
```json
{ "error": { "code": "account_not_verified", "message": "Your account is not verified. Check your email or request a new verification link." } }
```

### 3.3 `POST /api/auth/logout` — authenticated

Invalidates the current session token (deletes the session row).

**Request:** empty body; token in `Authorization` header.
**204 No Content.** Subsequent use of the same token → `401 unauthorized` (EC15).

### 3.4 `POST /api/auth/verify-email` — public

Consumes a single-use verification token. The token arrives via the emailed link `${FRONTEND_URL}/verify-email?token=<token>`; the SPA reads the query param and POSTs it (so the token is not retained in app navigation/history beyond the verify call).

**Request**
```json
{ "token": "raw-base64url-token-from-email" }
```

**200 OK**
```json
{ "message": "Email verified — your account is ready to use." }
```
**Side effect (ADR-0007/0008/0011):** on success, in the **same transaction**, the user is granted membership in the configurable default team (`DEFAULT_SIGNUP_TEAM_NAME`, default `Demo Team`), matched case-insensitively by name. As of Wave 1 (F-10, ADR-0011) that team is **auto-created if it does not exist** (race-safely), so a self-registered user reliably lands on a usable board with no admin pre-setup. The migration still auto-creates nothing (a fresh DB is schema-only; creation is lazy runtime logic). If `DEFAULT_SIGNUP_TEAM_NAME` is **blank**, the step is skipped with a warning (the user gets no membership). Admin-created users are pre-verified and never traverse this path.

**Errors:** `400 invalid_or_expired_token` — token unknown, already consumed (single-use), or expired (`now >= expires_at`, A31). The SPA shows the error state with a **resend** action.

### 3.5 `POST /api/auth/resend-verification` — public

Issues a fresh token, invalidating all earlier unused/unexpired tokens for that account (V4), and emails it. Non-committal response (anti-enumeration, A8).

**Request**
```json
{ "email": "alex@dataart.com" }
```

**202 Accepted** (always, regardless of whether the account exists or is already verified)
```json
{ "message": "If an account needs verification, a new email has been sent." }
```
- For a non-existent, already-verified, **or blocked** email, no usable token is created and the response is identical (A8, ADR-0007). A blocked user cannot use verification to regain access. Light rate-limiting recommended (A32).

### 3.6 `GET /api/auth/me` — authenticated

SPA bootstrap: returns the current user for the presented token.

**200 OK**
```json
{
  "id": "8e29c1b4-...", "email": "alex@dataart.com", "name": "Alex Doe", "emailVerified": true,
  "isAdmin": false, "isBlocked": false,
  "teams": [ { "id": "f1...", "name": "Platform" } ]
}
```
- `isAdmin` drives the admin-only "Users" nav item; `teams[]` drives the board team-selector and the "load last/first team" client logic (ADR-0007). An admin's `teams[]` may be empty (admins ignore scoping).
- `name` is the optional display name (`null` when unset); the SPA shows it in the header (falling back to the email). Email remains the account key.

**Errors:** `401 unauthorized` (incl. a token whose user became blocked).

### 3.7 `POST /api/auth/forgot-password` — public (F-01)

Requests a password-reset link. **Non-committal / non-enumerating**: always returns the same `202`, regardless of whether the email is unknown, unverified, verified, or blocked.

**Request**
```json
{ "email": "alex@dataart.com" }
```

**202 Accepted**
```json
{ "message": "If an account exists for that address, a password reset link has been sent." }
```
- A reset token is issued (and emailed) **only** for a user that exists AND is `emailVerified` AND `!isBlocked`. For a blocked or unverified account it is a **silent no-op** (no token, no email; the correct recovery path for an unverified account is `resend-verification`). Prior unused reset tokens are invalidated on each new request (at most one live reset token per account). Token = SHA-256-hashed at rest; the raw token appears only in the emailed link `${FRONTEND_URL}/reset-password?token=<token>`. TTL = `PASSWORD_RESET_TTL_HOURS` (default 1h). Light rate-limiting recommended (optional).

### 3.8 `POST /api/auth/reset-password` — public (F-01)

Consumes a single-use reset token and sets a new password.

**Request**
```json
{ "token": "raw-base64url-token-from-email", "password": "new correct horse" }
```
- `password`: **≥ 8** and **≤ 1024** chars (reuses the signup policy).

**200 OK**
```json
{ "message": "Your password has been reset. Please log in with your new password." }
```
- On success: the password hash is replaced (Argon2id), the token is consumed (single-use, atomic), and **all** of the user's sessions are purged (force re-login everywhere).

**Errors:** `400 validation_error` keyed `password` (too short/long); `400 invalid_or_expired_token` — token unknown, already consumed, expired (`now >= expires_at`), or the owning user is now blocked (defence-in-depth). No 401/403/404 — the endpoint is public and non-enumerating (the token is the secret).

---

## 3a. Me — self-service account (authenticated, self-only)

> **Authorization (ADR-0010):** both endpoints are **Self** — there is **no user id in the path**; the target is always the authenticated principal (`RequireUserId()`), so a user can never address another account (strongest anti-IDOR posture). These live under `/api/me/*` (neither public nor `/api/admin/*`), so a valid, verified, non-blocked session is required by the bearer middleware.

### 3a.1 `PUT /api/me/profile` — self (F-04)

Sets or clears the caller's own display name.

**Request**
```json
{ "name": "Alex Doe" }
```
(or `{ "name": null }` / blank to clear.)

**200 OK** → the updated `user` object (same shape as `/api/auth/me`), so the SPA can refresh its cached identity. Normalization matches admin `PUT /api/admin/users/{id}/name`: trim; blank/whitespace ⇒ stored `null`; idempotent no-op when unchanged.

**Errors:** `400 validation_error` keyed `name` (> 100 chars); `401 unauthorized` (no/blocked session).

### 3a.2 `POST /api/me/password` — self (F-04)

Changes the caller's own password, requiring current-password re-authentication.

**Request**
```json
{ "currentPassword": "correct horse battery", "newPassword": "new correct horse" }
```

**204 No Content** — the caller's **current** session stays valid; **all OTHER** sessions are purged (every other device is signed out). Behaviour:
1. Verify `currentPassword` against the stored hash — mismatch ⇒ **`401 invalid_credentials`** (a re-auth failure is a credentials failure; nothing more is leaked).
2. Validate `newPassword` (≥ 8, ≤ 1024) ⇒ `400 validation_error` keyed `newPassword` on failure.
3. Replace the hash (Argon2id) and delete every session for the user **except** the one presenting the current bearer token.

**Errors:** `401 invalid_credentials` (wrong current password); `401 unauthorized` (no/blocked session); `400 validation_error` keyed `newPassword`.

---

## 4. Teams

Team object:
```json
{
  "id": "f1c2...", "name": "Platform",
  "ticketCount": 12, "epicCount": 3,
  "createdAt": "2026-06-20T08:00:00Z", "modifiedAt": "2026-06-22T10:15:00Z",
  "wipLimits": { "new": null, "ready_for_implementation": 5, "in_progress": 3,
                 "ready_for_acceptance": null, "done": null }
}
```
- `wipLimits` — the per-team WIP (Work-In-Progress) cap per state. **All five states are always present**; a value of `null` means that state is **unlimited**, an integer (1–999) is the cap. A fresh team has every state `null`. (UX_LIMITS spec; ADR-0006.)

### 4.1 `GET /api/teams` — authenticated (membership-scoped, ADR-0007)
Lists teams with counts (Wireframe 4). An **admin** sees all teams; a **member** sees only the teams they belong to (server-side filter). An empty result is `[]`.

**200 OK**
```json
[
  { "id": "f1c2...", "name": "Platform", "ticketCount": 12, "epicCount": 3, "createdAt": "2026-06-20T08:00:00Z", "modifiedAt": "2026-06-22T10:15:00Z", "wipLimits": { "new": null, "ready_for_implementation": 5, "in_progress": 3, "ready_for_acceptance": null, "done": null } },
  { "id": "a7d9...", "name": "Payments", "ticketCount": 0, "epicCount": 0, "createdAt": "2026-06-21T09:00:00Z", "modifiedAt": "2026-06-21T09:00:00Z", "wipLimits": { "new": null, "ready_for_implementation": null, "in_progress": null, "ready_for_acceptance": null, "done": null } }
]
```
Empty: `[]` (SPA shows "no teams" empty state, EC9).

### 4.2 `POST /api/teams` — admin only (ADR-0007)
A non-admin caller → **403 forbidden**.

**Request**
```json
{ "name": "  Platform  " }
```
- `name`: required, non-empty after trim (stored trimmed); unique case-insensitively (V8).

**201 Created** → the created team object (counts 0/0; `createdAt == modifiedAt`).
**Errors:**
- `400 validation_error` — blank/whitespace-only name (EC1).
- `409 duplicate_team_name` — collides with existing name case/whitespace-insensitively (EC2):
```json
{ "error": { "code": "duplicate_team_name", "message": "A team with this name already exists." } }
```

### 4.3 `PUT /api/teams/{id}` — admin only (rename, ADR-0007)
A non-admin caller → **403 forbidden**.

**Request**
```json
{ "name": "Payments" }
```
**200 OK** → updated team object.
- No-op rule: if the normalized new name equals the stored normalized name → nothing persisted, `modifiedAt` unchanged (A10), still 200 with the unchanged object.
**Errors:** `404 not_found` (unknown id); `400 validation_error` (blank); `409 duplicate_team_name` (collides with a *different* team, US-TEAM-2).

### 4.4 `DELETE /api/teams/{id}` — admin only (ADR-0007)
A non-admin caller → **403 forbidden**. **204 No Content** when the team has zero tickets and zero epics.
**Errors:**
- `404 not_found` — unknown id.
- `409 team_has_children` — team has any ticket OR epic; nothing deleted, no cascade (V9, EC7):
```json
{ "error": { "code": "team_has_children", "message": "Cannot delete a team that still has tickets or epics. Remove them first." } }
```

### 4.5 `PUT /api/teams/{id}/wip-limits` — admin OR member of the team (ADR-0007)
Resolve the team first: unknown id → **404 not_found**; a member who is not in the team → **403 forbidden**.

Replaces this team's per-state WIP caps in one request. The body is a map of canonical state → limit; a value of `null` (or an **omitted** state) means **unlimited** for that state. The request is the authoritative full set: a state not present is cleared to unlimited.

**Request**
```json
{ "wipLimits": { "in_progress": 3, "ready_for_implementation": 5, "new": null } }
```
- Each value must be **`null`** (unlimited) **or an integer in `[1, 999]`**. Anything else — `0`, negative, fractional (`2.5`), non-numeric (`"abc"`), `> 999`, or an **unknown state key** — is rejected.
- Setting a cap **below** the current count in that column is **allowed** — only *new* arrivals are blocked; existing over-limit tickets remain (the column then reads as over-limit on the board).

**200 OK** → the updated **Team object** (with the new `wipLimits` map for all five states).

**Errors:**
- `404 not_found` — unknown team id.
- `400 validation_error` — one or more invalid values; `errors` is keyed by the offending **state** name:
```json
{ "error": { "code": "validation_error", "message": "One or more WIP limits are invalid.",
  "errors": { "in_progress": ["Enter a whole number of 1 or more, or leave blank for no limit."],
              "done": ["Enter a number no greater than 999."] } } }
```

### 4.6 `GET /api/teams/{id}/members` — admin OR member of the team (ADR-0017, Wave 2)

Member-visible list of a team's members for pickers (assignee / watch / label). Resolve the team first: unknown id → **404 not_found**; a member who is not in the team → **403 forbidden** (resolve-then-check). Returns the team's members only — admins are global and use the admin surface; the assignee-eligibility rule (team members ∪ admins) stays enforced server-side on assignment. `displayName` is computed server-side (`name?.trim() || email`). Ordered by display name.

**200 OK** →
```json
[ { "id": "8e29...", "displayName": "Alex Doe", "isAdmin": false },
  { "id": "a71f...", "displayName": "Sam Lee", "isAdmin": true } ]
```

**Errors:** `401 unauthorized`; `403 forbidden` (non-member non-admin); `404 not_found` (unknown team).

---

## 5. Epics

> **Authorization (ADR-0007):** every epic operation is **M(team)** — admin, or a member of the epic's team. List/create check the request `teamId`; edit/delete resolve the epic, then check its team (404-then-403 ordering). A member acting on a non-member team's epic → **403 forbidden**.

Epic object:
```json
{
  "id": "ep01...", "teamId": "f1c2...", "title": "Billing Revamp",
  "description": "Optional text or null", "ticketCount": 5,
  "createdAt": "2026-06-20T09:00:00Z", "modifiedAt": "2026-06-23T12:00:00Z"
}
```

### 5.1 `GET /api/epics?teamId={teamId}` — authenticated
Lists epics for one team with referencing-ticket counts (Wireframe 5). `teamId` is **required**.

**200 OK** → array of epic objects for that team (empty `[]` → empty state).
**Errors:** `400 validation_error` if `teamId` missing/invalid; `404 not_found` if the team does not exist.

### 5.2 `POST /api/epics` — authenticated
**Request**
```json
{ "teamId": "f1c2...", "title": "  Billing Revamp  ", "description": "optional" }
```
- `teamId`: required, must exist (else `400 validation_error`, reference-not-found). Team is fixed at creation and immutable thereafter (FR-E3-1, A13).
- `title`: required, non-empty after trim (V11); not unique (duplicates allowed, A11).
- `description`: optional, nullable; sane max length (A12).

**201 Created** → epic object (`ticketCount` 0; `createdAt == modifiedAt`).
**Errors:** `400 validation_error` (blank title, missing/unknown team).

### 5.3 `PUT /api/epics/{id}` — authenticated (edit title/description only)
**Request**
```json
{ "title": "Billing v2", "description": "updated" }
```
- **Team is read-only** — any `teamId` in the body is ignored; the epic stays in its team (US-EPIC-2). Title non-empty after trim.
- No-op rule: normalized-identical values → nothing persisted, `modifiedAt` unchanged (A14), 200 with unchanged object.

**200 OK** → updated epic object.
**Errors:** `404 not_found`; `400 validation_error` (blank title).

### 5.4 `DELETE /api/epics/{id}` — authenticated
**204 No Content** when no ticket references the epic.
**Errors:**
- `404 not_found`.
- `409 epic_referenced_by_tickets` — referenced by ≥1 ticket; nothing deleted (V12, EC7):
```json
{ "error": { "code": "epic_referenced_by_tickets", "message": "Cannot delete an epic that is referenced by tickets. Reassign or remove those tickets first." } }
```

---

## 6. Tickets

> **Authorization (ADR-0007):** every ticket/board operation is **M(team)** — admin, or a member of the ticket's team. Board/detail resolve then check (IDOR guard, 404-then-403). On create the body `teamId` must be accessible; on edit a member must have access to **both** the current team and the target team (cannot move a ticket into a team they don't belong to). A member acting on a non-member team → **403 forbidden**.

Ticket object (detail):
```json
{
  "id": "tk1042...", "teamId": "f1c2...",
  "epicId": "ep01...", "epicTitle": "Billing Revamp",
  "type": "bug", "state": "in_progress", "priority": "high",
  "title": "Login fails", "body": "Steps to reproduce...",
  "dueDate": "2026-07-05", "isOverdue": false,
  "assignees": [ { "id": "8e29...", "displayName": "Alex Doe" } ],
  "labels": [ { "id": "lb01...", "name": "Backend", "color": "#3b82f6" } ],
  "createdAt": "2026-06-22T09:15:00Z", "modifiedAt": "2026-06-23T12:40:00Z",
  "createdBy": "8e29c1b4-...", "createdByEmail": "alex@dataart.com", "createdByName": "Alex Doe",
  "isWatching": true
}
```
- `epicId`/`epicTitle` are `null` when no epic. `type` ∈ {`bug`,`feature`,`fix`}; `state` ∈ {`new`,`ready_for_implementation`,`in_progress`,`ready_for_acceptance`,`done`}.
- **`priority`** (F-03, ADR-0009) ∈ {`low`,`medium`,`high`,`urgent`}; always present; defaults to `medium`.
- **`dueDate`** (F-08) is an optional calendar day `"YYYY-MM-DD"` (UTC, no time-of-day; `null` when unset). **`isOverdue`** is server-computed: `dueDate != null && dueDate < today(UTC) && state != done`.
- **`assignees`** (F-02) is an array of `{ id, displayName }` (empty when unassigned); `displayName = name?.trim() || email`, computed server-side.
- **`labels`** (Wave 2, ADR-0016) is an array of `{ id, name, color }` (empty when none); `color` is `#rrggbb` (lowercased). Present on **both** the ticket detail and the board card. Assignment is via `PUT /api/tickets/{id}/labels` (§6.8); labels never bump `modified_at` and raise no events.
- `createdByName` is the creator's optional display name (`null` when unset); the SPA shows `displayName(createdByName, createdByEmail)` in the "Created by" line.
- **`isWatching`** (Wave 2, §6.7) is `true` when the **current user** watches this ticket; drives the detail watch toggle. Board cards do not carry it (keeps the board query lean).

### 6.1 `GET /api/tickets?teamId={teamId}&type=&epicId=&search=&priority=&assigneeId=&assignedToMe=&dueFilter=&labelId=` — authenticated
Board data for one team. `teamId` **required**. Optional filters combine with **AND** (A24):
- `type` — one of the three enum values; filters by type.
- `epicId` — UUID; filters to tickets referencing that epic.
- `search` — case-insensitive substring over **title only** (A24).
- **`priority`** (F-03) — one of {`low`,`medium`,`high`,`urgent`}; bad value ⇒ `400 validation_error`.
- **`assigneeId`** (F-02) — UUID; filters to tickets assigned to that user.
- **`assignedToMe`** (F-02) — `true` ⇒ filters to the current user's assignments (sugar for `assigneeId = me`). If both `assignedToMe=true` and `assigneeId` are sent, **`assignedToMe` wins**.
- **`dueFilter`** (F-08) — one of {`overdue`,`has_due_date`,`no_due_date`}; `overdue` ⇒ `due_date < today AND state != done`; bad value ⇒ `400 validation_error`.
- **`labelId`** (Wave 2, ADR-0016) — UUID; filters to tickets carrying that label. An unknown id simply matches nothing (no 400, consistent with `epicId`).

All new filters run inside the already team-scoped query, so they cannot reach another team's data.

**200 OK** — tickets grouped by state, in workflow order, each group ordered by `modifiedAt DESC` (A22). Per-column `count` reflects the **filtered** set (A23); per-column `total` and `wipLimit` support the WIP badge:
```json
{
  "teamId": "f1c2...",
  "total": 37,
  "columns": [
    { "state": "new", "count": 10, "total": 10, "wipLimit": null, "tickets": [ { "id": "...", "type": "bug", "state": "new", "priority": "high", "title": "Login fails", "epicId": "ep01...", "epicTitle": "Billing Revamp", "dueDate": "2026-07-05", "isOverdue": false, "assignees": [ { "id": "8e29...", "displayName": "Alex Doe" } ], "modifiedAt": "2026-06-23T12:40:00Z" } ] },
    { "state": "ready_for_implementation", "count": 6, "total": 6, "wipLimit": 5, "tickets": [] },
    { "state": "in_progress", "count": 8, "total": 8, "wipLimit": 3, "tickets": [] },
    { "state": "ready_for_acceptance", "count": 5, "total": 5, "wipLimit": null, "tickets": [] },
    { "state": "done", "count": 8, "total": 8, "wipLimit": null, "tickets": [] }
  ]
}
```
- Card payload includes at minimum `title`, `type`, `epicTitle`, `modifiedAt` (Wireframe 1). The five `columns` are always present in workflow order even when empty (each `tickets: []`, `count: 0`, `total: 0`) so the SPA renders exactly five columns (FR-E6-2).
- `total` (board-level) and each column `count` are **post-filter** (A23). At 100+ tickets server-side filtering is preferred (NFR-PERF-1); equivalent client-side filtering is permitted by source §8.
- Each column also carries (WIP, UX_LIMITS spec §3.1):
  - `total` — the **UNFILTERED** per-state ticket count for the team. The WIP badge `N / max` numerator and the full/over comparison use this, so an active type/epic/search filter cannot make a full column look not-full.
  - `wipLimit` — the team's cap for this state, or `null` when unlimited.

**Errors:** `400 validation_error` (`teamId` missing, bad `type` enum, bad `epicId`); `404 not_found` (unknown team).

### 6.2 `GET /api/tickets/{id}` — authenticated
**200 OK** → full ticket detail object (§6 top). **Errors:** `404 not_found`.

### 6.3 `POST /api/tickets` — authenticated
**Request**
```json
{ "teamId": "f1c2...", "type": "bug", "title": "  Login fails  ", "body": "Steps...", "epicId": "ep01...", "state": "new", "priority": "high", "dueDate": "2026-07-05", "assigneeIds": ["8e29..."] }
```
- `teamId`: required, must exist (V15).
- `type`: required, ∈ enum (V13).
- `state`: optional; defaults to `new` (A15); if provided must ∈ enum (V14).
- `priority` (F-03): optional; defaults to `medium` when omitted/null; if provided must ∈ {`low`,`medium`,`high`,`urgent`} else `400 validation_error` keyed `priority`.
- `dueDate` (F-08): optional; `"YYYY-MM-DD"` or null; any valid calendar date is allowed (a past date is simply overdue, not invalid); an ill-formed string ⇒ `400`.
- `assigneeIds` (F-02): optional; when provided, each id must be an existing user who is a **member of `teamId` or an admin**, else `400 validation_error` keyed `userIds` (de-duplicated).
- `title`: required non-empty after trim (V17); `body`: required non-empty after trim (V17). Sane max lengths (title ≤ 512, body large, A17) → overflow `400`.
- `epicId`: optional/nullable; if set, the epic must belong to `teamId` (V16) else `400 epic_team_mismatch`.

**201 Created** → full ticket detail. Server sets `createdAt = modifiedAt = now` (UTC), `createdBy` = authenticated user (V18, A16). Card lands in its state column.
**Errors:** `400 validation_error` (blank title/body, invalid `type`/`state` enum, unknown `teamId`/`epicId`); `400 epic_team_mismatch` (cross-team epic, EC5/EC13); `409 wip_limit_reached` — the target `(teamId, state)` has a WIP limit and the **unfiltered** count already in that state is ≥ the limit (UX_LIMITS spec §4.3):
```json
{ "error": { "code": "wip_limit_reached", "message": "This status already has the maximum number of tickets — finish existing ones first." } }
```

### 6.4 `PUT /api/tickets/{id}` — authenticated (edit)
Editable: `teamId`, `type`, `epicId`, `title`, `body`, `state`, `priority`, `dueDate`. `createdAt`/`createdBy` immutable (A16).

**Request**
```json
{ "teamId": "f1c2...", "type": "feature", "epicId": null, "title": "Login fails on Safari", "body": "Updated...", "state": "in_progress", "priority": "urgent", "dueDate": "2026-07-05" }
```
**200 OK** → updated ticket detail.
- `priority` (F-03): **required in the edit body** (like `type`/`state`); must ∈ enum else `400 validation_error` keyed `priority`. Participates in the modified_at no-op diff.
- `dueDate` (F-08): `"YYYY-MM-DD"` or `null` to clear; participates in the modified_at no-op diff (a change bumps `modified_at`; clearing to null is a change).
- `assigneeIds`: **not** used on this endpoint in Wave 1 — assignment is done via the dedicated `PUT /api/tickets/{id}/assignees` sub-resource (§6.7). (The field is accepted but, when omitted/null, the assignee set is left untouched, so a normal field edit never wipes assignees.)
- **modified_at semantics (V19/V20, A19):** the server normalizes incoming values (trim strings, compare refs by id, enums by value). If every field equals the stored value → **no-op**: nothing persisted, `modifiedAt` NOT advanced (EC6), 200 with unchanged object. If any differs → apply and set `modifiedAt = now`. **Assignment is metadata and does NOT participate in this diff** (an assignee change never bumps `modified_at`, so re-assigning does not reorder the board — consistent with "comment add never bumps modified_at", V21).
- **Same-team epic (V16):** if `epicId` non-null it must belong to the (possibly new) `teamId`, else `400 epic_team_mismatch` — enforced even on direct API calls and on team change (EC5). The SPA clears the epic on team change client-side (FR-E4-5).

**Errors:** `404 not_found`; `400 validation_error` (blank title/body, invalid enum incl. `priority`, unknown `teamId`/`epicId`); `400 epic_team_mismatch`; `409 wip_limit_reached` — when the edit MOVES the ticket into a different `(teamId, state)` that is already at its WIP limit. A no-op edit (same state) or an edit that leaves the state is never blocked (UX_LIMITS spec §4.3).

### 6.5 `PATCH /api/tickets/{id}/state` — authenticated (drag-and-drop)
Dedicated minimal endpoint for column moves; persists immediately (V25).

**Request**
```json
{ "state": "done" }
```
- `state`: required, ∈ enum (else `400 validation_error`). Any-to-any moves allowed (no sequential enforcement, FR-E6-6).

**200 OK** → updated ticket card/detail with advanced `modifiedAt` (so it sorts to the top of the target column, A22):
```json
{ "id": "tk1042...", "state": "done", "modifiedAt": "2026-06-24T08:00:00Z" }
```
**Errors:** `404 not_found`; `400 validation_error` (invalid target state); `409 wip_limit_reached` — the target state has a WIP limit and is already full (and the ticket isn't already in it). A same-state drop is a no-op and is never blocked; leaving a full state is always allowed (UX_LIMITS spec §4.2). On any non-2xx the SPA rolls the card back to its previous column and shows an error (FR-E6-5, EC10).

> If the new state equals the current state, this is a no-op: `modifiedAt` is not advanced (consistent with §6.2), 200 returned.

### 6.6 `DELETE /api/tickets/{id}` — authenticated
Deletes the ticket and **cascades to its comments** (V22, the only mandated cascade). UI confirms first (FR-E4-6); confirmation is a client concern.

**204 No Content.** **Errors:** `404 not_found`. Deleting a ticket also cascades to its `ticket_assignees` (F-02) and `ticket_watchers`/`activity_entries` (Wave 2). A `ticket_deleted` **notification** is fanned out to watchers **before** the row is removed and **outlives** the ticket (its `ticketId` becomes `null`; the SPA renders it as a non-navigable tombstone, ADR-0014 §6.6).

### 6.7 `PUT /api/tickets/{id}/assignees` — authenticated (M(team of ticket), F-02)
Replaces the ticket's **full** assignee set (authoritative complete set, mirroring wip-limits / admin `PUT .../teams`).

**Request**
```json
{ "userIds": ["8e29c1b4-...", "a71f..."] }
```
**200 OK** → the updated **ticket detail** (so the SPA refreshes card + detail from one response); the body carries the new `assignees[]`.

- **Semantics:** the request is the authoritative complete set. The service de-duplicates ids, diffs against the current set (add new, remove absent, leave unchanged). A no-op (same set) does **not** advance `modified_at` (assignment is metadata). `userIds` null/omitted ⇒ the empty set (clears all assignees).
- **Eligibility (payload rule → 400, not 403):** the caller must first pass team access on the ticket (`403 forbidden` otherwise). Then each requested id must reference an **existing user** who is a **member of the ticket's team OR an admin**; an unknown id ⇒ `400 validation_error` keyed `userIds` ("One or more users do not exist."), an ineligible id ⇒ `400 validation_error` keyed `userIds` ("One or more users are not members of this ticket's team.").
- **Stale assignees:** a user later removed from the team keeps existing assignments (they are tolerated read-side) but cannot be re-added.

**Errors:** `404 not_found` (unknown ticket); `403 forbidden` (caller not admin/member of the ticket's team); `400 validation_error` keyed `userIds` (unknown or ineligible user).

### 6.7a `PUT /api/tickets/{id}/labels` — authenticated (M(team of ticket), Wave 2, ADR-0016)
Replaces the ticket's **full** label set (authoritative complete set, mirroring assignees §6.7).

**Request**
```json
{ "labelIds": ["lb01-backend", "lb02-urgent"] }
```
**200 OK** → the updated **ticket detail** (so the SPA refreshes card + detail from one response); the body carries the new `labels[]`.

- **Semantics:** the request is the authoritative complete set. The service de-duplicates ids, diffs against the current set (add new, remove absent, leave unchanged). This never advances `modified_at` (labels are metadata) and raises **no** event (W2-LABEL-NOEVENTS). `labelIds` null/omitted ⇒ the empty set (clears all labels).
- **Eligibility (payload rule → 400, not 403):** the caller must first pass team access on the ticket (`403 forbidden` otherwise). Then each requested id must reference an **existing label that belongs to the ticket's team**; an unknown OR cross-team id ⇒ `400 validation_error` keyed `labelIds` ("One or more labels do not exist or belong to another team.").

**Errors:** `404 not_found` (unknown ticket); `403 forbidden` (caller not admin/member of the ticket's team); `400 validation_error` keyed `labelIds` (unknown or cross-team label).

### 6.8 Watch / unwatch (Wave 2, M(team of ticket), ADR-0013)

A **watcher** receives in-app notifications (and coalesced email) for a ticket's events, minus their own actions. Auto-watch triggers: creating the ticket, being added as an assignee, adding a comment (§6.3 of WAVE2_DESIGN). Manual watch/unwatch below. All three resolve the ticket → **404 not_found**, then `RequireTeamAccess` → **403 forbidden**. A stale watcher (lost team access) is skipped at read and fan-out but their row is preserved. **Watching never bumps `modified_at`.**

**`GET /api/tickets/{id}/watchers`** → **200**
```json
{ "watching": true, "watchers": [ { "id": "8e29...", "displayName": "Alex Doe" } ] }
```
`watching` is the **caller's** own flag; `watchers` is the full (access-filtered) list.

**`POST /api/tickets/{id}/watch`** — idempotent → **200** `{ "watching": true }`.
**`DELETE /api/tickets/{id}/watch`** — idempotent → **200** `{ "watching": false }`.
**Errors (all):** `404 not_found`; `403 forbidden`; `401`.

### 6.9 `GET /api/tickets/{id}/activity` — M(team of ticket) (Wave 2, ADR-0012)

The ticket's activity timeline, **newest-first**, keyset-paginated. Resolve ticket → **404**, `RequireTeamAccess` → **403**. `limit` clamped to `[1,100]` (default 50); `cursor` = opaque base64 of the last item's `(createdAt,id)`. This is the user-facing per-ticket history, distinct from any SEC-3 admin audit.

**200 OK**
```json
{ "items": [
    { "id":"ac1...", "eventType":"ticket_moved", "summary":"Alex Doe moved this from In progress to Done",
      "actorId":"8e29...", "actorDisplayName":"Alex Doe", "createdAt":"2026-07-01T14:00:00Z" }
  ], "hasMore": false, "nextCursor": null }
```
**Errors:** `404 not_found`; `403 forbidden`; `401`.

---

## 7. Comments

> **Authorization (ADR-0007):** list/add resolve the comment's ticket → its team, then check access — **M(team of ticket)**. Unknown ticket → **404 not_found**; a member acting on a non-member team's ticket → **403 forbidden**.

Comment object:
```json
{ "id": "cm01...", "ticketId": "tk1042...", "authorId": "8e29c1b4-...", "authorEmail": "alex@dataart.com", "authorName": "Alex Doe", "body": "Looks fixed.", "createdAt": "2026-06-23T13:00:00Z", "edited": false, "editedAt": null }
```
- `authorName` is the author's optional display name (`null` when unset); the SPA shows `displayName(authorName, authorEmail)`.
- `edited` (Wave 2, F-12) is `true` once the body has been edited; `editedAt` is the UTC edit timestamp (`null` when never edited).

### 7.1 `GET /api/tickets/{id}/comments` — authenticated
Lists a ticket's comments **oldest-first** (V23, FR-E5-4).

**200 OK** → array ordered by `createdAt ASC`. Empty `[]` → empty state (US-COMMENT-2).
**Errors:** `404 not_found` (unknown ticket).

### 7.2 `POST /api/tickets/{id}/comments` — authenticated
**Request**
```json
{ "body": "Looks fixed." }
```
- `body`: required, non-empty after trim (V23, EC1). `author` and `createdAt` are server-set (V23, A20/A21); never accepted from the client.

**201 Created** → the created comment object.
- **Does NOT touch the ticket's `modifiedAt`** (V21) → the card does not reorder on the board (EC8).
**Errors:** `404 not_found` (unknown ticket); `400 validation_error` (blank body).

### 7.3 `PUT /api/comments/{id}` — author only (F-12, Wave 2, ADR-0015)

Edit own comment. Note the path is the **top-level** `/api/comments/{id}` (a comment id is globally unique; the author check is the primary gate). Authorization ordering (anti-IDOR): resolve the comment → **404 not_found** if absent; resolve its ticket's team → **403 forbidden** if the caller cannot see the ticket; then require `authorId == currentUserId` → else **403 forbidden**. **No admin override on edit** — even an admin may not edit another user's words (ADR-0015).

**Request**
```json
{ "body": "Actually still broken on Safari." }
```
- `body`: required, non-empty after trim, ≤ 20000 chars.

**200 OK** → the updated comment object (`edited: true`, `editedAt` = now).
- **No-op rule:** if the normalized new body equals the stored body, nothing is persisted, `editedAt` is **not** set/advanced, and the unchanged object is returned (mirrors the `modifiedAt` no-op philosophy).
- **Does NOT touch the ticket's `modifiedAt`.**

**Errors:** `404 not_found` (unknown comment); `403 forbidden` (not the author, or no team access); `400 validation_error` (blank/oversize body).

### 7.4 `DELETE /api/comments/{id}` — author OR admin (F-12, Wave 2, ADR-0015)

Delete a comment. Same resolve-then-check ordering as §7.3. Then require `authorId == currentUserId` **OR** the caller is an admin (moderation override, ADR-0015) → else **403 forbidden**.

**204 No Content.** Hard delete of the comment row.

**Errors:** `404 not_found` (unknown comment); `403 forbidden` (not author and not admin, or no team access).

---

## 7a. Notifications & settings (Wave 2, Self, ADR-0013)

In-app notifications are **Self-scoped by construction** — every route targets the authenticated recipient; no other user's notification id is addressable (strongest anti-IDOR, same posture as `/api/me/*`). Comment **edited/deleted** raise activity only (no notification); everything else a watcher cares about notifies. Refresh in the SPA is **polling + refetch-on-focus** (no websockets, ADR-0016). Email is coalesced by a background worker (ADR-0014); in-app is instant.

Notification object:
```json
{ "id": "nt01...", "eventType": "ticket_moved", "summary": "Alex Doe moved this from New to In progress",
  "ticketId": "tk1042...", "commentId": null, "actorId": "8e29...", "actorDisplayName": "Alex Doe",
  "createdAt": "2026-07-01T14:00:00Z", "readAt": null }
```
- `summary` is rendered once at fan-out (display-cased). `readAt` `null` ⇒ unread. **`ticketId` `null` ⇒ deleted-ticket tombstone** (non-navigable; still markable).

### 7a.1 `GET /api/notifications?limit=20&cursor=<opaque>` — Self
Newest-first (`createdAt DESC, id DESC`). `limit` clamped to `[1,50]` (default 20); `cursor` = opaque base64 of the last item's `(createdAt,id)` (keyset pagination). **200**
```json
{ "items": [ /* NotificationDto[] */ ], "unreadCount": 3, "hasMore": true, "nextCursor": "..." }
```
`unreadCount` is included so the bell updates from the same call. **Errors:** `401`.

### 7a.2 `GET /api/notifications/unread-count` — Self
The cheap poll target. **200** `{ "unreadCount": 3 }`. **Errors:** `401`.

### 7a.3 `POST /api/notifications/{id}/read` — Self
Marks the addressed notification read (`readAt = now` if null; idempotent). Resolves by id **and** `recipientId == currentUserId`; another user's id ⇒ **404 not_found** (self-owned 404-masking). **200** `{ "unreadCount": <new count> }`. **Errors:** `404`; `401`.

### 7a.4 `POST /api/notifications/read-all` — Self
Marks all the caller's unread rows read. **200** `{ "unreadCount": 0 }`. **Errors:** `401`.

### 7a.5 `GET /api/me/notification-settings` — Self
**200** `{ "emailNotificationsEnabled": true }`. Suppresses **email only** (in-app always created; the worker skips email-off recipients and marks their rows emailed without sending). **Errors:** `401`.

### 7a.6 `PUT /api/me/notification-settings` — Self
**Request** `{ "emailNotificationsEnabled": false }` → **200** with the same shape. **Errors:** `400 validation_error` (missing flag); `401`.

---

## 7b. Labels (Wave 2, ADR-0016)

> **Authorization (ADR-0007):** labels are **team-scoped** and **member-managed** — every endpoint is **M(team)** (admin, or any member of the label's team). Each resolves the target team/label first (**404 not_found**) then checks access (**403 forbidden**) — 404-then-403 ordering (anti-IDOR). A label name is unique **within a team**, case-insensitively (two teams may each have "bug"); a collision ⇒ **409 duplicate_label_name**. Delete is disposable (no in-use guard): the label and its ticket associations cascade away. Labels raise **no** activity/notification events.

Label object:
```json
{ "id": "lb01...", "teamId": "f1c2...", "name": "Backend", "color": "#3b82f6" }
```
- `name` is trimmed, non-empty, ≤ **50** chars. `color` is `#rrggbb` (validated `^#[0-9a-fA-F]{6}$`, stored lowercased).

### 7b.1 `GET /api/labels?teamId={teamId}` — M(team)
`teamId` **required**. Resolve team → **404**, `RequireTeamAccess` → **403**. Returns the team's labels ordered by normalized name.
**200** `[ { "id":"lb01...", "teamId":"f1...", "name":"Backend", "color":"#3b82f6" } ]`. **Errors:** `400 validation_error` (missing `teamId`); `404`; `403`; `401`.

### 7b.2 `POST /api/labels` — M(team)
**Request** `{ "teamId": "f1...", "name": "  Backend  ", "color": "#3B82F6" }` — `name` trimmed + required, `color` required + `#rrggbb` (lowercased). **201** → the created **Label**. **Errors:** `400 validation_error` (keyed `name`/`color`/`teamId`); `404` (unknown team); `403`; `409 duplicate_label_name` (same normalized name already in the team).

### 7b.3 `PUT /api/labels/{id}` — M(team of label)
**Request** `{ "name": "Backend", "color": "#2563eb" }`. Resolve label → **404**, `RequireTeamAccess(label.teamId)` → **403**. Team is immutable. A no-op (same normalized name + color) persists nothing. **200** → the updated **Label**. **Errors:** `404`; `403`; `400`; `409 duplicate_label_name` (collides with a *different* label in the same team).

### 7b.4 `DELETE /api/labels/{id}` — M(team of label)
Resolve → **404**, `RequireTeamAccess` → **403**. Removes the label and its `ticket_labels` rows (cascade). **No** in-use guard (a label is disposable metadata). **204 No Content.** **Errors:** `404`; `403`; `401`.

> Label **assignment** on a ticket is a ticket sub-resource: `PUT /api/tickets/{id}/labels` (§6.7a).

---

## 8. Admin — User Management (ADR-0007 / ADR-0008)

All endpoints under `/api/admin/*` are **admin only**: a valid, verified, non-blocked session **and** `isAdmin=true`. A non-admin authenticated caller → **403 forbidden**.

**User object (admin list/detail):**
```json
{
  "id": "8e29c1b4-...", "email": "alex@dataart.com", "name": "Alex Doe",
  "isAdmin": true, "isBlocked": false, "emailVerified": true,
  "status": "active", "createdAt": "2026-06-30T11:26:00Z",
  "teams": [ { "id": "f1...", "name": "Platform" } ]
}
```
- `status` is **derived**: `blocked` if `isBlocked`; else `unverified` if `!emailVerified`; else `active`.
- `name` is the optional display name (`null` when unset). The Users list shows `displayName(name, email)` as the primary value with the email beneath; email stays the account key. Length ≤ 100.

### 8.1 `GET /api/admin/users` — admin
Lists **all** users (no team filter — admins are global), ordered by `createdAt` asc. **200 OK** → array of user objects. **Errors:** `401`; `403 forbidden`.

> **Filtering (SPA, client-side):** the Users screen filters the returned list **in the browser** (the list is admin-only and small). Filters combine with **AND**: free-text search over **name OR email** (case-insensitive substring), role (all/admin/member), team (all/specific, by membership), email verification (all/verified/unverified), and status (all/active/blocked), plus a Clear control and a match count. No server-side query params are added; this endpoint remains a plain full list (server-side `search`/`limit` may be added later without a contract break, [ПРИПУЩЕННЯ UM-8]).

### 8.2 `POST /api/admin/users` — admin
**Request**
```json
{ "email": "newdev@dataart.com", "password": null, "name": "New Dev", "isAdmin": false, "teamIds": ["f1..."] }
```
- `email`: required, valid, normalized-unique → else **409 email_in_use**.
- `password`: optional; null/blank ⇒ the server generates a strong password (≥16, mixed classes) and returns it once. If provided, must satisfy ≥8 / ≤1024.
- `name`: optional display name; trimmed, blank/whitespace ⇒ stored as `null`. Overflow (> 100 chars) ⇒ **400 validation_error** keyed `name`.
- `isAdmin`: optional (default false). `teamIds`: optional; each must reference an existing team (else **400 validation_error** keyed `teamIds`); de-duplicated.
- The account is created `emailVerified=true`, `isBlocked=false`, with **no** verification token and **no** email sent.

**201 Created**
```json
{ "user": { "...": "user object" }, "generatedPassword": "Xk9$mPq2vLr7Wn4t" }
```
`generatedPassword` is `null` when the admin supplied the password. **Errors:** `400`; `409 email_in_use`; `401`/`403`.

### 8.3 `PUT /api/admin/users/{id}/role` — admin
**Request:** `{ "isAdmin": false }` → **200 OK** with the updated user object. Idempotent (same value = no-op success). Demoting the **last active admin** → **409 last_admin_required**. Promotion is always allowed. **Errors:** `404`; `409 last_admin_required`; `401`/`403`.

### 8.3.1 `PUT /api/admin/users/{id}/name` — admin
Set or clear a user's optional display name.
**Request:** `{ "name": "Alex Doe" }` (or `{ "name": null }` / blank to clear) → **200 OK** with the updated user object. Trimmed; blank/whitespace ⇒ stored as `null`. Idempotent (unchanged value = no-op success). **Errors:** `404` (unknown user); `400 validation_error` keyed `name` (> 100 chars); `401`/`403`. There is no self-service profile edit — name is set by an admin only (a future enhancement may add self-edit).

### 8.4 `PUT /api/admin/users/{id}/teams` — admin
**Request:** `{ "teamIds": ["f1...", "a2..."] }` — replaces the full membership set. Each id must exist (else **400 validation_error**); de-duplicated; empty/null ⇒ no teams. **200 OK** with the updated user object. Does not affect `isAdmin`. **Errors:** `400`; `404`; `401`/`403`.

### 8.5 `POST /api/admin/users/{id}/block` — admin
Empty body. Sets `isBlocked=true` and **deletes all the user's sessions** in one transaction. Blocking the **last active admin** → **409 last_admin_required**. Idempotent. **200 OK** with the updated user object. **Errors:** `404`; `409 last_admin_required`; `401`/`403`.

### 8.6 `POST /api/admin/users/{id}/unblock` — admin
Empty body. Sets `isBlocked=false` (no session restoration; the user logs in again). Idempotent. **200 OK** with the updated user object. **Errors:** `404`; `401`/`403`.

### 8.7 `POST /api/admin/users/{id}/reset-password` — admin
Empty body. Generates a strong password, stores only its Argon2id hash, **deletes all the user's sessions**, and returns the plaintext once. Reset on a **blocked** user → **403 forbidden** ("Unblock the account before resetting its password.").

**200 OK**
```json
{ "generatedPassword": "Xk9$mPq2vLr7Wn4t" }
```
**Errors:** `404`; `403 forbidden` (target blocked, or caller not admin); `401`.

---

## 9. Health

### 9.1 `GET /health/live` — public
Liveness. **200 OK** `{ "status": "live" }`. Always 200 if the process is up.

### 9.2 `GET /health/ready` — public
Readiness: DB reachable AND migrations applied **[ADR-0003]**.
- **200 OK** `{ "status": "ready" }`
- **503 Service Unavailable** `{ "status": "not-ready", "reason": "database" | "migrations" }` while the DB is unreachable or migrations are still running.

---

## 10. Conventions recap (binding)

- Auth: `Authorization: Bearer <token>`; never in URLs; verify token only in the emailed link (§9 source, ADR-0001/0006).
- Timestamps: ISO-8601 UTC, trailing `Z`. Enums: canonical lowercase exactly as listed; the API never accepts/returns display-cased values.
- Status codes follow §2 uniformly: 400 invalid payload (incl. non-existent referenced entity in body), 401 unauthenticated, 403 unverified-after-login, 404 URL-path resource missing, 409 conflict (duplicate name / delete-guards).
- All create/update/delete go through these endpoints and persist in PostgreSQL (source §9); the SPA never uses local storage as the system of record (auth token mirror excepted, ADR-0001).
- Last-write-wins; no concurrent-edit conflict detection (source §9).
