# ADR 0021 — Outbound webhooks via a fourth event handler + DB-outbox worker (mirroring the email dispatcher), and API keys as prefix-routed hashed bearer tokens scoped to a versioned public API

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 3 approved scope (API / webhooks); [`WAVE3_DESIGN.md`](../WAVE3_DESIGN.md) §4.3/§4.4/§4.5/§5.5/§5.6/§7.3/§7.4/§8
- **Related ADRs:** 0014 (email outbox: directly-callable `DrainOnceAsync` + thin hosted timer, idempotent, per-item try/catch), 0012 (event backbone + `ITicketEventHandler`), 0001/0006 (opaque bearer sessions + hashed tokens via `ITokenGenerator`), 0007 (team-scoped + admin authz), 0002 (SQLite tests / faked adapters)

## Context

Wave 3 adds two "API" capabilities: **outbound webhooks** (notify external systems on ticket events) and **public API access via API keys** (personal access tokens). Both must fit the no-broker, single-server, fully-testable constraints. Webhooks make the app's **first outbound network calls** — an SSRF surface — and must be reliable (retry/backoff) and verifiable (signatures). API keys are the **first non-session credential** and must coexist with the existing opaque-bearer session middleware without becoming an admin/destructive credential if leaked.

## Decision

### Webhooks
- **Enqueue via a fourth `ITicketEventHandler`.** `WebhookEnqueuer` is registered alongside `ActivityRecorder`/`NotificationFanout`/`RealtimeNotifier`; on each after-commit event it inserts one `WebhookDelivery{ status=pending, next_attempt_at=now, attempts=0, payload_json=render(event) }` per active team subscription whose `event_types` matches. It writes rows only (like `NotificationFanout`), logs and swallows on failure — the request path never touches the subscriber.
- **Deliver via a DB-outbox worker mirroring [ADR-0014] exactly.** `WebhookDeliveryDispatcher.DrainOnceAsync(DateTime now, CancellationToken ct)` holds **all** delivery correctness (select `status='pending' AND next_attempt_at <= now`; sign; send via `IHttpClientFactory`; update status/attempts/`next_attempt_at`), takes `now` as a parameter, and runs inside `CreateExecutionStrategy().ExecuteAsync` + `BeginTransactionAsync` (Npgsql-retry-safe; correct on SQLite). A thin `WebhookDeliveryWorker : BackgroundService` is a `PeriodicTimer` over it. **Per-delivery try/catch** isolates a dead endpoint (mirrors the email per-recipient try/catch). The `CustomWebApplicationFactory` **removes** the worker (extend the existing filter) so tests drive `DrainOnceAsync` directly with a fake `IWebhookSender` + `TestClock`.
- **Retry:** max 5 attempts, exponential backoff (~1m/5m/30m/2h/6h via `next_attempt_at`), 10s per-attempt timeout, success = 2xx; then `failed` (kept for audit). Config via `WebhookOptions`/env.
- **Signature (verifiable payload):** `X-TicketTracker-Signature: sha256=HMAC_SHA256(secret, rawBody)` + `X-TicketTracker-Event`/`-Delivery`/`-Timestamp`. Subscribers verify by recomputing the HMAC; the `Delivery` id enables idempotency against at-least-once re-sends.
- **Secret is encrypted, not hashed.** Because the worker must **re-sign every delivery**, the signing secret must be recoverable → stored **AES-GCM-encrypted** (via `ISecretProtector`, key `WEBHOOK_SIGNING_KEY` from env, fail-fast in Production) and shown **once** on create/rotate. (This is the deliberate difference from passwords/API-keys, which are verify-only → one-way hash.)
- **SSRF policy: https-only + private-IP block at send time.** URLs must be `https://` (http only with `WEBHOOKS_ALLOW_INSECURE`). At **delivery time** (not only subscribe) the resolved host is rejected if private/loopback/link-local/ULA/metadata (`127/8`, `10/8`, `172.16/12`, `192.168/16`, `169.254/16` incl. `169.254.169.254`, `::1`, `fc00::/7`) — this defeats DNS-rebinding. Redirects are not followed. This blocks a subscription from probing the compose network or cloud metadata.

### API keys
- **Prefix-routed bearer, coexisting with sessions.** A key is a `ptk_<32 bytes base64url>` token presented as `Authorization: Bearer ptk_…`. `BearerAuthMiddleware` routes a `ptk_`-prefixed token to `ApiKeyAuthenticator` and everything else to the existing `AuthService.ResolveSessionUserAsync` — one header, one middleware, one `ICurrentUser`. The key is **hashed at rest** (SHA-256(HMAC pepper) via `ITokenGenerator`, like sessions/verification tokens, [ADR-0006]) with a stored 8-char `prefix` for display + lookup narrowing; the raw is shown **once** on create.
- **Live authz, scope-limited, never admin.** A key request derives its owner's **live** team memberships + admin flag, but is accepted **only on `/api/v1/*`** (a `ptk_` token on any other path → 401) and gated by coarse scopes (`tickets:read`, `tickets:write`; write implies read; insufficient → **403 insufficient_scope**). The v1 surface is **read + safe writes** (board/tickets/comments read; create/update/patch-state/comment write) — **no delete, no admin, no file transfer**. A leaked key can never reach `/api/admin/*` or destroy data.
- **v1 controllers are thin and reuse the same services.** `/api/v1/*` calls the exact `TicketService`/`CommentService` the UI uses — same validation, same team-scoped authz, same DTOs (minus `isWatching`). Only auth + scope + route-version differ.
- **`last_used_at`** is recorded (throttled to ≤ once/60s per key) for abuse visibility; **no enforced rate-limit** in Wave 3 (hook noted). Revocation (`revoked_at`) takes effect on the next request (no principal caching).

## Consequences

- **Positive:** webhooks reuse the proven, testable outbox pattern (no broker, deterministic tests, per-item isolation, idempotency); the event backbone gains a fourth consumer with no emission changes; API keys add a public API with one header, least privilege, and strong at-rest hashing; both credentials' secrets are shown once and stored safely (encrypted secret vs hashed key — the right tool for each).
- **Negative (accepted):** at-least-once webhook delivery (crash after send before commit re-sends; the `Delivery` id lets subscribers dedupe, R-A11). SSRF protection depends on correct IP-block logic (a security-pass target, §7.7). API keys are long-lived — mitigated by hashing, one-time reveal, scoping, and admin-exclusion.
- **Operational:** `WEBHOOK_SIGNING_KEY` is required in Production (fail-fast, like `AUTH_TOKEN_SECRET`); the api host must allow outbound HTTPS; both new secrets (webhook + api-key) reuse the one-time-reveal UX from user management.
