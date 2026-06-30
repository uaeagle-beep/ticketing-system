# ADR 0001 — Authentication strategy: stateful opaque bearer token (DB-backed sessions)

- **Status:** Accepted
- **Date:** 2026-06-30
- **Deciders:** Architect
- **Source refs:** REQUIREMENTS_SOURCE §3, §9, §11; ANALYSIS US-AUTH-1..6, FR-E1-9, NFR-SEC-1/5/6, A1, A3, A4
- **Related ADRs:** 0006 (verification-token storage)

## Context

The source allows either cookie-based sessions or bearer-token auth and mandates:

- Tokens (session/access/bearer) MUST NOT appear in URLs (§9).
- All business endpoints require authentication except sign-up, login, verify-email, resend (§3).
- Logout MUST invalidate the token so a reused token returns 401 (ANALYSIS US-AUTH-5, EC15).
- Unverified accounts MUST receive no usable business session (ANALYSIS A1, US-AUTH-4).

The frontend is a separate Vite SPA served behind an nginx reverse proxy that forwards `/api` to the backend (see ADR 0005). Both are same-origin from the browser's point of view (browser → nginp:80; nginx → backend:8080), so cookie SameSite concerns are minimal, but token revocation on logout is an explicit hard requirement.

### Options considered

1. **JWT bearer token (stateless).** Simple to issue/validate; no server store. But a plain JWT cannot be revoked on logout without an additional server-side denylist — which reintroduces server state anyway. To satisfy "logout invalidates the token → 401" we would need a denylist keyed by jti, so the stateless advantage evaporates.
2. **httpOnly cookie session.** Good XSS posture (JS cannot read the token). Requires CSRF defense for state-changing requests. The SPA cannot inspect auth state from the cookie and must rely on a `/api/auth/me` probe.
3. **Stateful opaque bearer token stored in DB (chosen).** Server issues a high-entropy random token, stores only its SHA-256 hash in a `sessions` table with an expiry. Client sends it as `Authorization: Bearer <token>`. Logout deletes the row → immediate, guaranteed revocation. No token contents in URL. No CSRF token needed because auth is via a custom `Authorization` header, not an ambient cookie (a cross-site form cannot set custom headers).

## Decision

Use a **stateful opaque bearer token** ("session token") transported in the `Authorization: Bearer <token>` header.

- On successful login of a **verified** account, generate 32 bytes from a CSPRNG, base64url-encode it (the value returned to the client, never logged), and persist a `Session` row storing `token_hash = SHA-256(token)`, `user_id`, `created_at`, `expires_at = now + SESSION_TTL_HOURS`.
- Authentication middleware reads the `Authorization` header, hashes the presented token, looks up a non-expired `Session`, loads the user, and requires `user.email_verified = true`. On any miss → `401`.
- **Logout** deletes the session row by token hash → subsequent use returns `401`.
- **Unverified login** never creates a session; the endpoint returns `403 account_not_verified` with a resend hint (ANALYSIS A4 — the single scoped enumeration exception, justified because it only triggers after correct credentials).
- The SPA stores the token in memory and mirrors it to `localStorage` only as a convenience for page-refresh continuity. This does NOT violate §9 "no local storage as system of record" — the RDBMS remains the system of record for all application data; `localStorage` holds only the auth token, exactly as a cookie would. (If the team prefers zero token-in-JS exposure, the same backend works unchanged behind an httpOnly cookie; that is a frontend transport swap only.)

Token entropy: 256 bits. Only the hash is stored, so a DB read cannot reconstruct a live token.

## Consequences

- **Positive:** Trivial, reliable logout/revocation (DB delete); no token payload in URLs; no CSRF machinery; opaque tokens leak nothing if logged accidentally (still treated as secret); uniform 401 path simplifies the auth gate.
- **Negative:** Every authenticated request does one indexed lookup on `sessions.token_hash` (acceptable at hackathon scale; index makes it O(log n)). Requires periodic cleanup of expired sessions (a lightweight background sweep or lazy delete on lookup — we lazy-delete on expired hit and rely on TTL).
- **Anti-enumeration:** login wrong-credentials and unknown-email both return identical `401 invalid_credentials` (ANALYSIS A3). Resend always returns `202` regardless of whether the account exists/needs verification (A8). The only deviation is `403 account_not_verified` after correct credentials (A4), an accepted, documented trade-off.
