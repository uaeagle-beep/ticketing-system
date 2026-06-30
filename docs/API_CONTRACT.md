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
- **Public (no auth):** `POST /api/auth/signup`, `POST /api/auth/login`, `POST /api/auth/verify-email`, `POST /api/auth/resend-verification`, `GET /health/live`, `GET /health/ready`, and static frontend assets.
- **Authenticated (token required):** everything else, including `GET /api/auth/me`. Missing/invalid/expired/logged-out token → **401**.
- **Verified required:** authenticated endpoints additionally require `email_verified=true`. An authenticated-but-unverified state is not reachable because login does not issue a session to unverified accounts (it returns 403 first). A token whose user somehow became unverified → **403 account_not_verified**.

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
| 401 | `unauthorized` | No/invalid/expired/logged-out bearer token |
| 401 | `invalid_credentials` | Login: wrong password or unknown email (anti-enumeration, identical message) |
| 403 | `account_not_verified` | Login with correct creds on an unverified account |
| 404 | `not_found` | Resource addressed in the URL path (`/{id}`) does not exist |
| 409 | `duplicate_team_name` | Team create/rename collides case-insensitively |
| 409 | `team_has_children` | Delete team that has tickets or epics |
| 409 | `epic_referenced_by_tickets` | Delete epic referenced by ≥1 ticket |
| 409 | `wip_limit_reached` | Create/move a ticket INTO a (team, state) whose WIP limit is already reached |
| 400 | `invalid_or_expired_token` | verify-email: token unknown, consumed, or expired |

**Rule:** `400` = bad/ill-formed payload (incl. a non-existent reference passed in the body); `404` = the URL-path resource is absent; `409` = conflict with persisted state (uniqueness or protective delete-guard). Applied uniformly.

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
  "user": { "id": "8e29c1b4-...", "email": "alex@dataart.com", "emailVerified": true },
  "expiresAt": "2026-07-03T11:26:00Z"
}
```
**Errors:**
- `401 invalid_credentials` — wrong password OR unknown email (identical response, A3).
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
- For a non-existent or already-verified email, no usable token is created and the response is identical (A8). Light rate-limiting recommended (A32).

### 3.6 `GET /api/auth/me` — authenticated

SPA bootstrap: returns the current user for the presented token.

**200 OK**
```json
{ "id": "8e29c1b4-...", "email": "alex@dataart.com", "emailVerified": true }
```
**Errors:** `401 unauthorized`.

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

### 4.1 `GET /api/teams` — authenticated
Lists all teams (no ownership; all verified users see all — source §4) with counts (Wireframe 4).

**200 OK**
```json
[
  { "id": "f1c2...", "name": "Platform", "ticketCount": 12, "epicCount": 3, "createdAt": "2026-06-20T08:00:00Z", "modifiedAt": "2026-06-22T10:15:00Z", "wipLimits": { "new": null, "ready_for_implementation": 5, "in_progress": 3, "ready_for_acceptance": null, "done": null } },
  { "id": "a7d9...", "name": "Payments", "ticketCount": 0, "epicCount": 0, "createdAt": "2026-06-21T09:00:00Z", "modifiedAt": "2026-06-21T09:00:00Z", "wipLimits": { "new": null, "ready_for_implementation": null, "in_progress": null, "ready_for_acceptance": null, "done": null } }
]
```
Empty: `[]` (SPA shows "no teams" empty state, EC9).

### 4.2 `POST /api/teams` — authenticated
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

### 4.3 `PUT /api/teams/{id}` — authenticated (rename)
**Request**
```json
{ "name": "Payments" }
```
**200 OK** → updated team object.
- No-op rule: if the normalized new name equals the stored normalized name → nothing persisted, `modifiedAt` unchanged (A10), still 200 with the unchanged object.
**Errors:** `404 not_found` (unknown id); `400 validation_error` (blank); `409 duplicate_team_name` (collides with a *different* team, US-TEAM-2).

### 4.4 `DELETE /api/teams/{id}` — authenticated
**204 No Content** when the team has zero tickets and zero epics.
**Errors:**
- `404 not_found` — unknown id.
- `409 team_has_children` — team has any ticket OR epic; nothing deleted, no cascade (V9, EC7):
```json
{ "error": { "code": "team_has_children", "message": "Cannot delete a team that still has tickets or epics. Remove them first." } }
```

### 4.5 `PUT /api/teams/{id}/wip-limits` — authenticated (set per-state WIP limits)

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

---

## 5. Epics

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

Ticket object (detail):
```json
{
  "id": "tk1042...", "teamId": "f1c2...",
  "epicId": "ep01...", "epicTitle": "Billing Revamp",
  "type": "bug", "state": "in_progress",
  "title": "Login fails", "body": "Steps to reproduce...",
  "createdAt": "2026-06-22T09:15:00Z", "modifiedAt": "2026-06-23T12:40:00Z",
  "createdBy": "8e29c1b4-...", "createdByEmail": "alex@dataart.com"
}
```
- `epicId`/`epicTitle` are `null` when no epic. `type` ∈ {`bug`,`feature`,`fix`}; `state` ∈ {`new`,`ready_for_implementation`,`in_progress`,`ready_for_acceptance`,`done`}.

### 6.1 `GET /api/tickets?teamId={teamId}&type=&epicId=&search=` — authenticated
Board data for one team. `teamId` **required**. Optional filters combine with **AND** (A24):
- `type` — one of the three enum values; filters by type.
- `epicId` — UUID; filters to tickets referencing that epic.
- `search` — case-insensitive substring over **title only** (A24).

**200 OK** — tickets grouped by state, in workflow order, each group ordered by `modifiedAt DESC` (A22). Per-column `count` reflects the **filtered** set (A23); per-column `total` and `wipLimit` support the WIP badge:
```json
{
  "teamId": "f1c2...",
  "total": 37,
  "columns": [
    { "state": "new", "count": 10, "total": 10, "wipLimit": null, "tickets": [ { "id": "...", "type": "bug", "state": "new", "title": "Login fails", "epicId": "ep01...", "epicTitle": "Billing Revamp", "modifiedAt": "2026-06-23T12:40:00Z" } ] },
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
{ "teamId": "f1c2...", "type": "bug", "title": "  Login fails  ", "body": "Steps...", "epicId": "ep01...", "state": "new" }
```
- `teamId`: required, must exist (V15).
- `type`: required, ∈ enum (V13).
- `state`: optional; defaults to `new` (A15); if provided must ∈ enum (V14).
- `title`: required non-empty after trim (V17); `body`: required non-empty after trim (V17). Sane max lengths (title ≤ 512, body large, A17) → overflow `400`.
- `epicId`: optional/nullable; if set, the epic must belong to `teamId` (V16) else `400 epic_team_mismatch`.

**201 Created** → full ticket detail. Server sets `createdAt = modifiedAt = now` (UTC), `createdBy` = authenticated user (V18, A16). Card lands in its state column.
**Errors:** `400 validation_error` (blank title/body, invalid `type`/`state` enum, unknown `teamId`/`epicId`); `400 epic_team_mismatch` (cross-team epic, EC5/EC13); `409 wip_limit_reached` — the target `(teamId, state)` has a WIP limit and the **unfiltered** count already in that state is ≥ the limit (UX_LIMITS spec §4.3):
```json
{ "error": { "code": "wip_limit_reached", "message": "This status already has the maximum number of tickets — finish existing ones first." } }
```

### 6.4 `PUT /api/tickets/{id}` — authenticated (edit)
Editable: `teamId`, `type`, `epicId`, `title`, `body`, `state`. `createdAt`/`createdBy` immutable (A16).

**Request**
```json
{ "teamId": "f1c2...", "type": "feature", "epicId": null, "title": "Login fails on Safari", "body": "Updated...", "state": "in_progress" }
```
**200 OK** → updated ticket detail.
- **modified_at semantics (V19/V20, A19):** the server normalizes incoming values (trim strings, compare refs by id, enums by value). If every field equals the stored value → **no-op**: nothing persisted, `modifiedAt` NOT advanced (EC6), 200 with unchanged object. If any differs → apply and set `modifiedAt = now`.
- **Same-team epic (V16):** if `epicId` non-null it must belong to the (possibly new) `teamId`, else `400 epic_team_mismatch` — enforced even on direct API calls and on team change (EC5). The SPA clears the epic on team change client-side (FR-E4-5).

**Errors:** `404 not_found`; `400 validation_error` (blank title/body, invalid enum, unknown `teamId`/`epicId`); `400 epic_team_mismatch`; `409 wip_limit_reached` — when the edit MOVES the ticket into a different `(teamId, state)` that is already at its WIP limit. A no-op edit (same state) or an edit that leaves the state is never blocked (UX_LIMITS spec §4.3).

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

**204 No Content.** **Errors:** `404 not_found`.

---

## 7. Comments

Comment object:
```json
{ "id": "cm01...", "ticketId": "tk1042...", "authorId": "8e29c1b4-...", "authorEmail": "alex@dataart.com", "body": "Looks fixed.", "createdAt": "2026-06-23T13:00:00Z" }
```

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

> Comments are immutable in mandatory scope: there is **no** PUT/PATCH/DELETE comment endpoint (V24, US-COMMENT-3). Edit/delete-own-comment is an explicit stretch feature only.

---

## 8. Health

### 8.1 `GET /health/live` — public
Liveness. **200 OK** `{ "status": "live" }`. Always 200 if the process is up.

### 8.2 `GET /health/ready` — public
Readiness: DB reachable AND migrations applied **[ADR-0003]**.
- **200 OK** `{ "status": "ready" }`
- **503 Service Unavailable** `{ "status": "not-ready", "reason": "database" | "migrations" }` while the DB is unreachable or migrations are still running.

---

## 9. Conventions recap (binding)

- Auth: `Authorization: Bearer <token>`; never in URLs; verify token only in the emailed link (§9 source, ADR-0001/0006).
- Timestamps: ISO-8601 UTC, trailing `Z`. Enums: canonical lowercase exactly as listed; the API never accepts/returns display-cased values.
- Status codes follow §2 uniformly: 400 invalid payload (incl. non-existent referenced entity in body), 401 unauthenticated, 403 unverified-after-login, 404 URL-path resource missing, 409 conflict (duplicate name / delete-guards).
- All create/update/delete go through these endpoints and persist in PostgreSQL (source §9); the SPA never uses local storage as the system of record (auth token mirror excepted, ADR-0001).
- Last-write-wins; no concurrent-edit conflict detection (source §9).
