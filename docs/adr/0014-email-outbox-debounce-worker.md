# ADR 0014 — Email notifications via a DB-backed outbox drained by a thin hosted timer over a directly-callable coalescing dispatcher

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 2 approved scope (instant in-app + anti-noise coalesced email; DB outbox + background worker; no broker); [`WAVE2_DESIGN.md`](../WAVE2_DESIGN.md) §7
- **Related ADRs:** 0002 (SQLite tests / EnsureCreated), 0003 (hosted-service migrate pattern), 0004 (IEmailSender port), 0013 (notification model)

## Context

Notifications are created instantly in-app for every event (ADR-0013). Email must be per-event but anti-noise: **never to the actor** (already excluded at fan-out), and **rapid bursts to the same recipient coalesced into one email** within a short debounce window. The PO mandated a DB-backed outbox + a background worker (poll for un-emailed notifications older than the debounce interval, group by recipient, send one combined email, mark emailed) with **no message broker** — staying with the codebase's simple hosted-service style (`HostedServices/DatabaseInitializer`).

The overriding constraint (ADR-0002): everything must be testable on in-memory SQLite with a fake clock and fake email sender, **without relying on real timing or threads for correctness** — the background worker's core logic must be a synchronous, directly-callable service method that tests invoke deterministically.

## Decision

- **Outbox = the `notifications` table itself.** `emailed_at IS NULL` means "not yet emailed". No separate outbox table is needed — the notification row is the unit of work, and `emailed_at` is both the outbox marker and the idempotency key.
- **`NotificationEmailDispatcher.DrainOnceAsync(DateTime now, CancellationToken ct)`** (scoped Application service) contains the entire drain/coalesce/send/mark cycle and takes `now` as a parameter (never reads wall-clock). Algorithm, inside the Npgsql-retry-safe pattern (`CreateExecutionStrategy().ExecuteAsync` + `BeginTransactionAsync` + `CommitAsync`, per fix `14e4424`):
  1. Select `notifications WHERE emailed_at IS NULL AND created_at <= now - DEBOUNCE`, ordered by recipient then created_at.
  2. Group by `recipient_id`; for each recipient still eligible (not blocked, `email_notifications_enabled=true`), send **one** digest email with all their pending `summary` lines via `IEmailSender.SendNotificationDigestEmailAsync`. Email-off/blocked recipients are marked emailed **without** sending (so they never backlog).
  3. Set `emailed_at = now` for every processed row; commit.
  4. **Per-recipient try/catch**: a send failure for one recipient leaves that recipient's rows `null` (retried next tick) and logs; it must not block other recipients.
- **`NotificationEmailWorker : BackgroundService`** is a **thin** wrapper: a `PeriodicTimer` that, each tick, creates a DI scope (like `DatabaseInitializer`), resolves the dispatcher + `IClock`, and calls `DrainOnceAsync(clock.UtcNow, ct)`; it catches/logs exceptions so a bad tick does not kill the host. It owns only timing, scoping, and error-logging — **zero business logic.**
- **Coalescing window = 60s; poll = 15s** (`NOTIFICATION_EMAIL_DEBOUNCE_SECONDS`, `NOTIFICATION_WORKER_POLL_SECONDS`, env-bound via `NotificationOptions`), plus `NOTIFICATIONS_EMAIL_ENABLED` master switch. 60s coalesces a human's rapid edit burst into one email while a lone event still emails within ~75s; both are tunable without code changes.
- **New `IEmailSender.SendNotificationDigestEmailAsync(toEmail, lines, deepLinkBase, ct)`**, implemented in `SmtpEmailSender`, `LoggingEmailSender`, and the test `FakeEmailSender` (new captured kind so tests assert coalescing).
- **Tests drive it deterministically** (ASR-W2-3): the `CustomWebApplicationFactory` **removes** `NotificationEmailWorker` (as it already removes `DatabaseInitializer`), so no timer fires. Tests perform HTTP actions (creating notifications via the in-process fan-out), then call `DrainOnceAsync(Factory.Clock.UtcNow, ...)` directly: within the window → 0 emails; advance `TestClock` past 60s → exactly one digest per recipient with the expected lines; drain again → 0 (idempotent). This reuses the established `TestClock`+`FakeEmailSender` pattern.

## Consequences

- **Positive:** no broker, no new infra; the outbox is one nullable column; correctness lives entirely in a synchronous, clock-injected method that tests call directly (no sleeps, no flakiness); coalescing is a simple `GROUP BY recipient`; idempotency is `emailed_at`; the timer is trivial and disposable in tests.
- **Negative (accepted):** at-least-once email — a crash after send but before commit re-sends the digest next tick (a duplicate digest is benign). At-most-once for the digest is not attempted because it would require a pre-commit "sending" marker and a reconciliation pass — not worth it at this scale.
- **Negative:** email latency is bounded by debounce + poll (~75s worst case), by design (anti-noise). If "faster email" is later wanted, lower the two env values.
- **Operational:** `NOTIFICATIONS_EMAIL_ENABLED=false` disables the worker entirely (in-app unaffected) — a clean kill-switch if SMTP is down.
