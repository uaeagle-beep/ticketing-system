# ADR 0013 — Notification model, watcher fan-out (exclude actor), stale-watcher skip, and the global email toggle

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 2 approved scope (Notifications = "involved + watch"; never notify the actor); [`WAVE2_DESIGN.md`](../WAVE2_DESIGN.md) §4.2–§4.3, §6.3–§6.8
- **Related ADRs:** 0007 (team-scope authorization), 0012 (event backbone), 0014 (email outbox worker)

## Context

The PO fixed the model: a ticket has **watchers**; a user is auto-watched when they create the ticket, are added as an assignee, or add a comment; others watch/unwatch manually. On each event the system fans out to all watchers **except the actor**. In-app notifications are created instantly; email is coalesced (ADR-0014). No per-event preferences; an optional global "email on/off" toggle is allowed if cheap.

Two shape questions: (1) does a notification store a rendered string or a structured payload; (2) what happens to a watcher who has lost access to the ticket's team.

## Decision

- **`TicketWatcher(ticket_id, user_id, created_at)`** join with a unique `(ticket_id, user_id)` index; both FKs **CASCADE** (a watch is an association owned by both ticket and user, like `UserTeam`). Auto-watch is an idempotent insert inside the mutation's transaction; manual watch/unwatch via `POST/DELETE /api/tickets/{id}/watch` (M(team of ticket)).
- **`Notification`** stores **both** a structured payload **and** a pre-rendered `summary`: columns `recipient_id` (FK users CASCADE), `actor_id` (FK users RESTRICT), `ticket_id` (**nullable, FK SET NULL** — so a `ticket_deleted` notification survives its ticket, see ADR-0014 / WAVE2_DESIGN §6.6), `comment_id` (nullable, **no FK** — a comment delete must neither cascade-nuke nor block the notification), `event_type` (text + CHECK), `summary` (rendered once at fan-out), `data_json` (nullable), `created_at`, `read_at` (nullable = unread), `emailed_at` (nullable = outbox marker). Indexes: `(recipient_id, read_at, created_at DESC)` for "my unread / my list"; `(emailed_at, created_at)` for the outbox scan.
- **Rendering:** the `summary` is rendered by the raising service/handler at fan-out time; keeping the structured columns as well preserves future render-on-read / filtering. Rendered strings use display-cased enum values for human readability.
- **Fan-out (`NotificationFanout`, ADR-0012 handler):** recipients = watchers of the ticket **minus the actor**, and **minus any watcher who no longer has team access** (blocked, or not a member of the ticket's team and not admin). One `Notification` row per eligible recipient per notifiable event; instant (no email — that is the worker's job).
- **Stale-watcher rule:** a watcher who lost team access is **skipped at fan-out and read**, but their `TicketWatcher` row is **preserved** (not eagerly pruned). Justification: (a) never deliver a notification — which discloses ticket title/activity — to someone who lost the right to see the ticket (read-side of ADR-0007's team-scope); (b) no need to hook every membership/block change to prune; filter at fan-out; (c) re-adding the user to the team resumes delivery automatically. Mirrors the Wave-1 "stale assignee tolerated" decision.
- **In-app API is Self** (`/api/notifications/*`, no id of another user is addressable): list (keyset-paged, newest-first, with `unreadCount`), `unread-count`, mark-one-read, mark-all-read. Another user's notification id → **404** (self-owned resource; 404-masking is correct here, unlike team resources).
- **Global email toggle:** `users.email_notifications_enabled` (bool, default true). Suppresses **email only** (in-app always created). `GET/PUT /api/me/notification-settings` (Self). The worker skips email-off recipients and marks their rows emailed (no send) so they do not backlog.

## Consequences

- **Positive:** cheap hot-path reads (pre-rendered summary + two targeted indexes); actor never notified (PO rule enforced in one `WHERE`); stale watchers can never leak a ticket; the toggle is a single column and two Self endpoints; keyset pagination is stable and cheap.
- **Negative:** `summary` is frozen at fan-out — if display labels change later, old notifications keep old wording (acceptable; `data_json` allows re-render if ever needed). `comment_id` can dangle after a comment delete (accepted, R-6). `TicketWatcher` rows for departed members linger until the ticket is deleted (harmless; filtered out).
- **Security:** fan-out and read both enforce the team-scope read rule; notifications are Self-scoped by construction (strongest anti-IDOR, like `/api/me/*`).
