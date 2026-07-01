# ADR 0010 — Self-service account flows: password reset (dedicated token table) & profile self-edit

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 1 approved features F-01 (self-service password reset), F-04 (self-service profile); [`WAVE1_DESIGN.md`](../WAVE1_DESIGN.md) §3.4–§3.5, §4.4–§4.5, §6.1–§6.2
- **Related ADRs:** 0001 (stateful sessions), 0004 (IEmailSender abstraction, send-failure policy), 0006 (hashed single-use tokens, status-code taxonomy), 0007/0008 (blocked-user semantics, account_blocked, execution-strategy transactions)

## Context

Wave 1 adds two self-service account capabilities that previously only existed as admin-driven flows (`UserAdminService.ResetPasswordAsync`, `SetNameAsync`). Both must reuse the existing crypto/session/token machinery and stay consistent with the anti-enumeration and blocked-user rules already in place. Three judgment calls needed an explicit ruling: the reset token's storage home, the blocked/unverified request behaviour, and the session-purge policy on each of the three password-mutation paths (reset vs self-change vs admin-reset).

## Decision

### A. Password reset uses a dedicated `password_reset_tokens` table (not the verification-token table)
- New table structurally twin to `email_verification_tokens` (`user_id` FK CASCADE, fixed-64 `token_hash` indexed, `created_at`, `expires_at`, `consumed_at`), reusing `ITokenGenerator` (CSPRNG + SHA-256 + HMAC pepper) and the hash-only-storage rule (ADR-0006).
- **Why dedicated, not shared:** the two flows have different TTLs (reset 1h vs verify 24h), different purposes, and different consequences. Sharing one table would force a "token type" discriminator and risk a verification token being accepted on the reset endpoint (privilege confusion — verifying an email is not proof of intent to change a password). Separate tables keep each flow's single-use/expiry invariants clean and independently testable — consistent with `sessions` and `email_verification_tokens` already being separate.
- **TTL = 1h** (env `PASSWORD_RESET_TTL_HOURS`, default `1`), distinct from `TOKEN_TTL_HOURS`. Boundary `now >= expires_at` ⇒ expired (A31). Single-use (`consumed_at` set atomically); each new request invalidates the user's prior unused reset tokens (at most one live token, mirroring V4).

### B. Non-enumerating request; blocked AND unverified are silent no-ops
- `POST /api/auth/forgot-password` always returns the same `202` non-committal message regardless of whether the email is unknown, unverified, verified, or blocked — identical to `resend-verification`.
- A token is issued **only** for a user that exists AND is `emailVerified` AND `!isBlocked`. A **blocked** user is a no-op (a blocked user must not regain access via any self-service path — aligns with `account_blocked`). An **unverified** user is a no-op (the correct path is `resend-verification`; issuing a reset would let someone set a password on an unproven address).
- Defence-in-depth: `reset-password` re-checks the owner's `is_blocked` inside the transaction and treats a token whose owner became blocked as invalid.

### C. Session-purge policy differs per path, deliberately
| Path | Purge | Rationale |
|---|---|---|
| `reset-password` (F-01, public) | **ALL** sessions | Owner may have forgotten the password everywhere / possible compromise; force full re-login. |
| `POST /api/me/password` self-change (F-04) | **OTHER** sessions, **keep current** | The actor just authenticated with the current password and is present; least-surprise to keep them logged in while evicting other devices. |
| admin `reset-password` (existing) | **ALL** sessions | Actor is an admin, not necessarily the owner; force re-login. |

- Self-change requires **current-password re-auth** (`IPasswordHasher.Verify`); mismatch ⇒ `401 invalid_credentials` (reuse existing code — a re-auth failure is a credentials failure and must not leak more). This blocks a hijacked idle session from silently changing the password.
- The current session is identified by hashing the presented bearer token (`ITokenGenerator.Hash`) and excluding that row from the purge.

### D. `/api/me/*` is self-only by construction (no id in path)
- Both self endpoints (`PUT /api/me/profile`, `POST /api/me/password`) take **no** user id; they act on `ICurrentUser.RequireUserId()`. A user cannot form a request targeting another account — the strongest anti-IDOR posture (nothing to tamper with). Admin editing of other users stays exclusively under `/api/admin/*`. Name normalization reuses `UserAdminService.SetNameAsync` rules verbatim (trim; blank ⇒ null; >100 ⇒ 400 keyed `name`; idempotent).

### E. No new error codes; reuse existing taxonomy
`InvalidOrExpiredToken` (400), `ValidationError` (400), `InvalidCredentials` (401), `Unauthorized` (401) cover every case. No taxonomy growth.

### F. Atomicity via the execution strategy
Token issuance, token consumption, and password change all run inside `_db.Database.CreateExecutionStrategy().ExecuteAsync(...)` + `BeginTransactionAsync` (the Npgsql-retry-safe pattern, fix `14e4424`), exactly like `VerifyEmailAsync`/`ResendVerificationAsync`/admin reset. Never a bare transaction.

## Consequences

- **Positive:** clean separation of the two token flows (no discriminator, no cross-acceptance risk); consistent non-enumeration with the existing resend flow; a defensible, documented session-purge policy per path; the strongest anti-IDOR shape for self-service (no id); zero new error codes; email send reuses `IEmailSender` with a new method captured by `FakeEmailSender` for offline QA.
- **Negative:** a second token table to maintain (accepted — the invariants are cleaner apart than a shared table with a type flag). The self-change keep-current-session policy differs from reset/admin-reset — a deliberate inconsistency that must be documented for QA (it is, in WAVE1_DESIGN §8.E). Unverified users cannot use password reset to recover — intentional; they use resend-verification.
