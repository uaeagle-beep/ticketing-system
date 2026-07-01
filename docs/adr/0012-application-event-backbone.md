# ADR 0012 — Application-level domain-event backbone (explicit after-commit publisher, not an EF interceptor)

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 2 approved scope (Notifications, Activity history); [`WAVE2_DESIGN.md`](../WAVE2_DESIGN.md) §2, §6
- **Related ADRs:** 0002 (SQLite test provider), 0007 (authorization/team-scope), 0013 (notification model), 0014 (email outbox worker), 0015 (comment events)

## Context

Wave 2 adds two consumers of "everything that happens to a ticket": a per-ticket **activity/audit timeline** and a **notifications** fan-out to watchers. The PO requires that "all changes" reach both, from a single mutation, deterministically. We need one emission point per mutation so the two consumers never drift (e.g. an edit that logs activity but forgets to notify, or vice-versa).

The system is a layered ASP.NET Core app (thin controllers, HTTP-agnostic Application services, EF Core over Npgsql in prod / SQLite in tests). There is **no** message broker and none is wanted (ARCHITECTURE, ADR-0005). Existing services already compute a normalized field diff for `modified_at` semantics (ARCHITECTURE §6.2), which is exactly the information the activity/notification summaries need.

### Options considered

1. **EF `SaveChanges` interceptor** that inspects the change-tracker and emits events. Rejected: it hides emission from the service code (not greppable), cannot cleanly express domain semantics ("state changed" as a distinct event from "priority changed", "assignee added vs removed"), fires inside the save transaction (so a notification-write failure could roll back the user's edit), and is awkward to unit-test without a full EF context. It also couples the domain-event meaning to persistence internals.
2. **A full in-process mediator library (e.g. MediatR)**. Rejected as over-engineering for two handlers; adds a dependency and indirection the codebase's "one service per aggregate, explicit calls" style deliberately avoids.
3. **Explicit application-level publisher, called after commit (chosen).** Each mutating service, after its `SaveChangesAsync`, builds a small list of `TicketEvent` records and calls `IDomainEventPublisher.PublishAsync(events, ct)`. The publisher invokes in-process handlers synchronously within the request scope: `ActivityRecorder` (writes `ActivityEntry` rows) and `NotificationFanout` (writes `Notification` rows to eligible watchers, excluding the actor). Handlers persist their own rows and swallow+log their own failures.

## Decision

- Introduce `IDomainEventPublisher` (Application/Abstractions) and a `TicketEvent` record carrying `Type` (an `EventType` enum, canonical text), `TicketId`, `ActorId`, optional `CommentId`, a small `DataJson`, and pre-rendered `SummaryForActivity`/`SummaryForNotification`.
- Mutating methods in `TicketService` (create/update/patch-state/set-assignees/delete) and `CommentService` (add/edit/delete) **raise events explicitly, after their mutation has committed.** The service reuses the field diff it already computes for `modified_at`.
- Two in-process `ITicketEventHandler`s consume every event synchronously: `ActivityRecorder` and `NotificationFanout`. Each owns its own insert + `SaveChangesAsync` and logs (does not rethrow) on failure, so a logging/notification failure cannot undo the user's action.
- **Not** an EF interceptor. Emission is explicit, in the service, and testable at the service level.
- **Canonical event set** (WAVE2_DESIGN §6.1): `ticket_created`, `ticket_field_changed` (one per changed field), `ticket_moved`, `ticket_assignees_changed`, `comment_added`, `comment_edited`, `comment_deleted`, `ticket_deleted`. Stored as canonical lowercase text + CHECK on `notifications.event_type` and `activity_entries.event_type` (parity with ticket type/state, ADR-0002).
- **Activity vs security audit are separate concerns.** `ActivityEntry` is the user-facing, team-scoped, per-ticket timeline; it is not the SEC-3 security/admin audit (which is not a persisted table today and, if ever built, is a separate feature). No overlap.

## Consequences

- **Positive:** one greppable emission point per mutation feeds both consumers, so they cannot drift; after-commit isolation guarantees a notification/activity write failure never rolls back the user's real edit; in-process keeps the no-broker constraint; fully unit/integration-testable on SQLite (handlers just write rows). Reuses the existing field-diff logic.
- **Negative (accepted):** at-most-once semantics — a process crash between the mutation commit and `PublishAsync` loses that event's activity+notification. The window is microseconds in a single process; activity/notifications are non-critical relative to the mutation itself. A transactional-outbox for *events* (not just emails) is deferred; if durability becomes a requirement, events can later be written to an outbox table in the same transaction as the mutation and published by a worker (additive, no contract change).
- **Negative:** every new mutating path must remember to publish. Mitigated by concentrating mutations in two services and by table-driven activity tests (WAVE2_DESIGN §10.G) that assert the expected entries per action.
