# ADR 0006 — Verification-token storage & HTTP status-code policy for conflicts

- **Status:** Accepted
- **Date:** 2026-06-30
- **Deciders:** Architect
- **Source refs:** REQUIREMENTS_SOURCE §3, §9; ANALYSIS A27, A31, US-AUTH-2/3, V3/V4, Open-risks 1 & 7
- **Related ADRs:** 0001 (auth)

## Context

Two decision points flagged by the BA needed an explicit, testable ruling:

1. **Verification-token storage.** Source is silent on whether to store the raw token. Open-risk 7 recommends storing only a hash so a DB compromise cannot leak live links.
2. **Status code for validation-style conflicts** — specifically duplicate team name. Source explicitly mandates `409` only for the two delete-guard cases (team-with-children, referenced-epic). Duplicate-name could be `409`, `422`, or `400` (A27). The whole API must be internally consistent.

## Decision

### A. Verification token

- Generate 32 bytes from a CSPRNG → base64url string = the raw token placed in the email link (`${FRONTEND_URL}/verify-email?token=<token>`). The raw token is the ONLY token allowed in a URL (§9).
- Persist an `EmailVerificationToken` row storing **`token_hash = SHA-256(rawToken)`** (never the raw token), `user_id`, `created_at`, `expires_at = created_at + TOKEN_TTL_HOURS` (default 24), and `consumed_at` (nullable).
- **Verify** (`POST /api/auth/verify-email { token }`): hash the incoming token, find a matching row; reject (`400 invalid_or_expired_token`) if not found, already consumed (`consumed_at != null`), or expired. **Expiry boundary (A31): `now >= expires_at` ⇒ expired** (i.e., strictly older than 24h is invalid; the instant of expiry counts as expired). On success: set `consumed_at = now` and `user.email_verified = true` atomically in one transaction (single-use, V3).
- **Resend / new-token issuance invalidates earlier unused tokens (V4):** within a transaction, mark all of the user's non-consumed, non-expired tokens as consumed (or delete them) before inserting the new one. So at most one live token exists per account at a time.
- Lookup is by `token_hash` (indexed), not by user, so a stolen DB yields only hashes.

### B. Status-code policy (uniform across the API)

| Situation | Code | Rationale |
|---|---|---|
| Malformed body / failed field validation (empty-after-trim, bad email syntax, password < 8, body too long) | **400** `validation_error` | Conventional; field-level `errors` map included. |
| Invalid enum value (`type`, `state`) or **missing/non-existent referenced entity in the request body** (team/epic that does not exist) | **400** `validation_error` | Treated as a bad request payload, not a routing miss — the client sent an invalid reference. (404 is reserved for the addressed resource in the URL path.) |
| Unauthenticated / invalid / logged-out / expired session token | **401** `unauthorized` | §3 auth gate; ANALYSIS US-AUTH-6. |
| Authenticated but account unverified (only after correct login creds) | **403** `account_not_verified` | Scoped exception A4; lets the UI show resend. |
| Resource addressed in the URL path does not exist (`GET/PUT/DELETE /api/tickets/{id}` where id unknown) | **404** `not_found` | REST convention A27. |
| **Duplicate team name (case-insensitive)** | **409** `conflict` (`code: "duplicate_team_name"`) | **Chosen: 409.** A name collision is a conflict with existing state, semantically identical in kind to the two mandated 409 delete-guards. Using 409 for all "your request conflicts with current persisted state" cases keeps one consistent rule the client can branch on, rather than splitting name-collision into 422. (A27 decision point resolved in favor of 409.) |
| Delete team that has tickets OR epics | **409** `conflict` (`code: "team_has_children"`) | Mandated by §9. |
| Delete epic referenced by tickets | **409** `conflict` (`code: "epic_referenced_by_tickets"`) | Mandated by §9. |

- **Rule of thumb encoded:** `400` = "your payload is invalid/ill-formed (including a reference that does not exist)"; `404` = "the thing in the URL path isn't there"; `409` = "your request conflicts with persisted state (uniqueness or a protective delete-guard)". This is uniform and testable.
- Every error response uses the single envelope defined in API_CONTRACT (`{ error: { code, message, errors? } }`).

## Consequences

- **Positive:** DB compromise leaks no usable verification links (hash-only); single-use & invalidation are atomic and testable; one coherent status-code taxonomy across all endpoints removes ambiguity for both frontend and QA.
- **Negative:** Choosing 409 (not 422) for duplicate-name is a judgment call; documented here so it is a conscious, consistent decision. Treating "referenced entity does not exist in body" as 400 (not 404) is deliberate — 404 stays reserved for the URL-addressed resource, avoiding the ambiguity of "which missing thing does the 404 refer to".
