# ADR 0008 — User Management migration: promote existing users to admin, last-admin guard, self-signup default team

- **Status:** Accepted
- **Date:** 2026-06-30
- **Deciders:** Architect
- **Source refs:** User Management approved requirements §2, §6, §7, §8; [`USER_MANAGEMENT_DESIGN.md`](../USER_MANAGEMENT_DESIGN.md)
- **Related ADRs:** 0002 (test DB / SQLite EnsureCreated), 0003 (migrate-on-startup, no seed), 0007 (authorization model)

## Context

Introducing `isAdmin`/`isBlocked` on `users` and a `user_teams` join requires a schema migration with three judgment calls:

1. **What role do existing users get?** The requirements mandate **all existing users → `isAdmin=true`** (§7), so a deployment does not lock everyone out of the data they already manage (previously every verified user had full access; demoting them all to team-less members on upgrade would strand their work).
2. **How is the "never lose the last admin" invariant enforced?** (§2: last admin cannot be demoted/blocked/deleted.)
3. **How does a self-registered member get an initial workspace?** (§8: after verification, join a configurable default team, default "Demo Team"; if absent, no team + warning.)

Constraints from existing ADRs: production applies **EF migrations** at startup (ADR-0003); tests build schema from the model via **`EnsureCreated()`** (ADR-0002); a **fresh DB must contain only schema + migration metadata, no seed data** (ADR-0003, V28).

### Options considered

- **Promotion as a data step inside the EF migration (chosen)** vs. a runtime startup routine. The migration is the right home: it runs exactly once per database via the EF history table, is idempotent (`UPDATE users SET is_admin = true`), and keeps the one-time concern out of request/startup code.
- **Last-admin guard as a DB trigger** vs. **Application-layer guard (chosen).** A trigger cannot produce the precise `409 last_admin_required` envelope and is a cross-row business rule; the guard belongs in the service, reused by role-change/block/(future)delete.
- **Auto-create "Demo Team" in the migration** vs. **runtime, no-seed (chosen).** Seeding a team in the migration violates "fresh DB = schema only" (ADR-0003). Instead, membership is granted at verify-time if the team exists; if not, the user gets no team and a warning is logged, and an admin assigns later.

## Decision

- **Schema:** add `users.is_admin boolean NOT NULL DEFAULT false`, `users.is_blocked boolean NOT NULL DEFAULT false`; create `user_teams (id, user_id, team_id, created_at)` with `UNIQUE (user_id, team_id)`, an index on `team_id`, and both FKs `ON DELETE CASCADE`. Defaults configured via `.HasDefaultValue(false)` so Npgsql and SQLite agree.
- **Data migration:** in `Up()`, after the `AddColumn` calls, run `migrationBuilder.Sql("UPDATE users SET is_admin = true;")`. Existing users become admins; users created after the migration default to member (`false`). The statement is idempotent and plain ANSI.
- **Test parity:** tests use `EnsureCreated()` and create users with explicit flags, so the data-`UPDATE` never runs under SQLite — acceptable, and the schema itself is model-derived and provider-agnostic (ADR-0002). The CI parity guard (`has-pending-model-changes`, ADR-0003) must stay green after adding the model config and migration.
- **Last-admin guard:** Application-layer invariant `COUNT(users WHERE is_admin AND NOT is_blocked) >= 1` must hold after any role-demote, block, or delete; violation → `409 last_admin_required`. Counts **active** admins (a blocked admin provides no coverage). Applies to self-actions too.
- **Self-signup default team:** new env `DEFAULT_SIGNUP_TEAM_NAME` (default `Demo Team`) bound into `AuthOptions`. On `verify-email` success, in the same transaction, grant membership to the team whose normalized name matches; if none exists, log a warning and proceed with no membership. Admin-created users are pre-verified and never traverse this path.

## Consequences

- **Positive:** zero-lockout upgrade (existing users keep full access as admins); one-time, idempotent, history-tracked promotion; no seed data added (V28 preserved); the last-admin invariant is centralized and testable; default-team behavior is configurable and degrades safely when the team is missing.
- **Negative:** `Down()` cannot restore the original `is_admin` distribution (data migration is not reversible) — forward-fix is preferred once live; documented. After the upgrade, **every** pre-existing account is an admin, so operators should review and demote accounts that should be members (a deliberate, documented operational step, not an automatic one).
- **Negative:** block/reset must set the flag and delete sessions atomically; this uses the provider execution strategy + explicit transaction (per the Npgsql retry constraint, fix `14e4424`) exactly as the existing verify/resend flows.
