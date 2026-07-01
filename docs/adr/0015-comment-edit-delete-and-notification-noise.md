# ADR 0015 — Comment edit/delete-own (F-12): author-only edit, author-or-admin delete, and activity-only (no email) for edit/delete

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 2 approved scope (F-12 comments edit/delete own); [`WAVE2_DESIGN.md`](../WAVE2_DESIGN.md) §5.2, §6.1
- **Related ADRs:** 0006 (error codes), 0007 (team-scope + resolve-then-check), 0012 (event backbone), 0013 (notifications)

## Context

Mandatory scope made comments immutable (V24, "no PUT/PATCH/DELETE comment endpoint"). Wave 2 approves F-12: a user may edit and delete their **own** comments. Two decisions: (1) who may act — author-only vs admin override, and whether edit and delete differ; (2) whether edit/delete generate notifications and email (noise) or only an activity-log line.

## Decision

- **New endpoints** `PUT /api/comments/{id}` and `DELETE /api/comments/{id}` (top-level `/api/comments`, because a comment id is globally unique and the author check is the primary gate; the ticket is resolved from the comment for the team-scope check).
- **Authorization ordering (anti-IDOR, ADR-0007):** resolve the comment → 404 if absent; resolve its ticket → team; `RequireTeamAccess(team)` → 403 if the caller cannot even see the ticket; then the author/role gate.
- **Edit is author-only.** Even an admin may **not** edit another user's comment → 403. Justification: editing someone else's words changes what they said — a stronger, more misleading action than removal. Nobody but the author should rewrite an author's comment. `comments.edited_at` records the edit; a no-op edit (same normalized body) does not set it.
- **Delete is author OR admin (override).** Justification: admins need a moderation lever to remove abusive/mistaken content; deletion (unlike edit) does not put words in someone's mouth. A non-author non-admin → 403.
- **Notification noise (W2-COMMENT-EVENTS):**
  - `comment_added` → **activity + notification** (email included) — a new comment is exactly the kind of update a watcher wants.
  - `comment_edited` / `comment_deleted` → **activity-log only; no notification, no email.** Justification: an edit or removal of an existing comment is rarely worth interrupting watchers by email, but it IS worth an audit line in the ticket timeline for transparency. This keeps the feed low-noise (the PO's explicit anti-noise goal) while preserving history.
- **Errors:** `404 not_found`; `403 forbidden` (not author / no team access / non-admin delete of another's comment); `400 validation_error` (blank or oversize body). No new error code needed.

## Consequences

- **Positive:** clean, least-surprise permissions (you own your words; admins can moderate removals); the timeline stays truthful (`edited_at` + activity entries) without spamming watchers; reuses the existing resolve-then-check ordering and error taxonomy (no new codes).
- **Negative:** an admin cannot fix a typo in a member's comment (must ask the member or delete it). Accepted — integrity of authorship outweighs the convenience.
- **Negative:** a watcher who cares about wording changes won't be emailed on an edit. Accepted (anti-noise); reversible by moving `comment_edited` into the notifiable set (one line in the fan-out decision) if the PO disagrees.
