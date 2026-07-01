# ADR 0011 — Robust default-team auto-provisioning at self-signup verification (race-safe create-if-missing)

- **Status:** Accepted (supersedes one clause of ADR-0008)
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 1 approved feature F-10 (default-team auto-provisioning); [`WAVE1_DESIGN.md`](../WAVE1_DESIGN.md) §3.6, §4.6, §6.3
- **Related ADRs:** 0002 (SQLite test provider), 0003 (migrate-on-startup, **fresh DB = schema only, no seed** — V28), 0007 (authorization/membership), **0008 (default-team-at-verify; this ADR supersedes its "migration never auto-creates + warn-if-missing at runtime" clause)**

## Context

Today a self-registered user joins the configurable default team (`DEFAULT_SIGNUP_TEAM_NAME`, default `Demo Team`) at verify time **only if that team already exists**; if it is missing, the user gets **no** team and a warning is logged (ADR-0008 / `AuthService.GrantDefaultTeamMembershipAsync`). In practice this strands brand-new self-registered users on an empty board until an admin manually creates the team — a poor onboarding outcome for the common "fresh deployment, no admin has created a team yet" case. F-10 requires that a self-registered user **always** lands in a usable workspace.

The tension: ADR-0003/V28 mandate that a **fresh DB contains only schema + migration metadata (no seed data)**, and the ADR-0008 migration deliberately creates no team. So auto-provisioning must not be a migration seed; it must be lazy runtime creation, and it must be **idempotent and race-safe** because two users can verify near-simultaneously and both find the team missing.

## Decision

- **Auto-create the default team at verify time if it is missing**, then grant membership — instead of warn-and-skip. The change is entirely in `AuthService.GrantDefaultTeamMembershipAsync`; the contract of `POST /api/auth/verify-email` is unchanged (same request/response); only the side effect strengthens.
- **The migration still creates nothing.** V28 is preserved: a fresh DB is schema-only until a real user verifies. Creation is lazy runtime logic, not a seed.
- **Algorithm (inside the existing verify transaction, which already runs via `CreateExecutionStrategy().ExecuteAsync` + `BeginTransactionAsync`):**
  1. `normalizedName = NormalizeKey(DefaultSignupTeamName)`; if blank ⇒ skip + warning (unchanged degrade path, operator cleared the config).
  2. Look up team by `name_normalized`. If found ⇒ use it.
  3. If missing ⇒ insert `Team { Name = trimmed config value, NameNormalized = normalizedName, CreatedAt = ModifiedAt = now }`.
  4. Insert `UserTeam` if not already a member (idempotent `AnyAsync` guard, unchanged).
- **Race-safety (TOCTOU):** the `teams` table already has `HasIndex(x => x.NameNormalized).IsUnique()`. Two concurrent verifications that both reach step 3 cannot both insert — the second gets a unique-constraint violation (`DbUpdateException` / PG 23505). Handle it by **re-querying by normalized name and using the now-existing row** (the race loser joins the winner's team). Because verify already executes through the provider **execution strategy**, an Npgsql serialization/retry is replayed safely; on the retry the re-query finds the committed team. Never open a bare transaction (Npgsql retry constraint, fix `14e4424`).
- **Admin-created-user behaviour is unchanged.** Admin-created accounts are pre-verified and never traverse `verify-email`, so they never trigger auto-creation. F-10 explicitly does not alter that path.

## Consequences

- **Positive:** every self-registered user reliably lands on a usable board after verifying, with zero admin pre-setup — the intended onboarding outcome. Idempotent and race-safe; concurrent signups converge on one team and one membership each. No seed data added (V28 preserved). Contract unchanged; purely a stronger side effect.
- **Negative (superseding note):** this **supersedes ADR-0008's clause** that "the migration never auto-creates it; **if absent, the user gets no membership + a warning**." The migration still creates nothing (that half of ADR-0008 stands); but the **runtime** now auto-creates rather than warning-and-skipping. A deployment that intended an admin to explicitly own team creation will instead see `Demo Team` auto-created on the first self-signup. If the PO prefers admin-owned creation, revert F-10 to the ADR-0008 warn-and-skip behaviour (localized change) — flagged as open question #2 in WAVE1_DESIGN §11.
- **Negative:** the created team's display name is taken from the (trimmed) config value; if an operator sets `DEFAULT_SIGNUP_TEAM_NAME` to something that later collides case-insensitively with an admin-created team, the normalized-name lookup will find and reuse the existing one (correct, by design) rather than error — acceptable and consistent with the case-insensitive uniqueness key (V8).
