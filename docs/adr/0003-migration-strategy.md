# ADR 0003 — Migration & schema-initialization strategy: EF Core migrations applied at container startup

- **Status:** Accepted
- **Date:** 2026-06-30
- **Deciders:** Architect
- **Source refs:** REQUIREMENTS_SOURCE §9 (automated migrations, fresh-DB-no-data), §13; ANALYSIS FR-E7-8/9, FR-E8-2, V28, A29
- **Related ADRs:** 0002 (test DB), 0005 (docker topology)

## Context

The source requires:

- Schema creation MUST be automated via migrations or an equivalent repeatable mechanism (§9).
- `docker compose up --build` from the repo root must start the whole solution with no host-installed runtime (§2) — so migrations cannot be a manual developer step; they must run automatically.
- After init, a **fresh DB must contain only schema + migration metadata; no seed/sample data on the default startup path** (§9, §13, V28).

### Options considered

1. **Run `dotnet ef database update` as a separate compose step / init container.** Requires the EF CLI tooling in the image and orchestration ordering; heavier image.
2. **Apply migrations in application code at startup via `context.Database.Migrate()` (chosen).** No CLI in the runtime image; the API waits for the DB to be reachable, applies any pending migrations, then serves traffic. Idempotent — re-running adds nothing.
3. **`EnsureCreated()` in production.** Rejected for production: it does not use the migrations history table, cannot evolve schema, and the source explicitly favors migrations. (We DO use `EnsureCreated()` in tests — ADR 0002 — because PG-targeted migration SQL is not SQLite-compatible.)

## Decision

- Author EF Core migrations targeting **Npgsql/PostgreSQL** under `TicketTracker.Infrastructure/Migrations`.
- On startup, the API host runs a small `DatabaseInitializer` hosted routine that:
  1. Retries connecting to PostgreSQL with bounded backoff (DB container may still be starting; compose `depends_on: condition: service_healthy` reduces but does not eliminate the race — see ADR 0005).
  2. Calls `await context.Database.MigrateAsync()` to apply all pending migrations.
  3. Does **NOT** insert any application rows. No seeding. The only non-schema rows are EF's `__EFMigrationsHistory` (migration metadata, explicitly allowed by §9).
- A startup flag `RUN_MIGRATIONS_ON_STARTUP` (default `true`) lets QA/ops disable auto-migration if they ever apply migrations out-of-band; the default path satisfies the DoD.
- **Test/production parity guard:** because tests build the schema from the model via `EnsureCreated()` while production uses migrations, CI runs `dotnet ef migrations has-pending-model-changes` (or equivalent) so a model change without a matching migration fails the build. This keeps the migration-built (PG) schema and the model-built (SQLite test) schema in lock-step.

## Consequences

- **Positive:** Zero manual steps for QA; idempotent and repeatable; satisfies "no seed data" by construction (we simply never seed); migration metadata is the only allowed non-application data. Restart-safe: a restart re-runs `Migrate()` which is a no-op when up to date, so persisted data is preserved (NFR-REL-1).
- **Negative:** Startup briefly couples API readiness to DB migration success; if a migration fails the API should fail fast (crash-loop visible in compose logs) rather than serve a half-migrated schema — this is the desired behavior. The readiness endpoint (`/health/ready`) reports unready until migrations complete (ADR 0005).
- **Negative:** Migrations are PG-specific; the parity guard (above) is mandatory to avoid drift between the test schema and the production schema.
