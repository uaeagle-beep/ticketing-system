# ADR 0002 — Test database provider: SQLite in-memory via swappable DbContext registration

- **Status:** Accepted
- **Date:** 2026-06-30
- **Deciders:** Architect
- **Source refs:** REQUIREMENTS_SOURCE §11 (Testing); ANALYSIS NFR-TST-1/2, FR-E8-6
- **Related ADRs:** 0003 (migrations), 0004 (email isolation)

## Context

The hackathon requires automated tests: at least one backend business flow and at least one API flow (§11). A hard constraint from the task brief is that **integration tests must run locally without Docker and without PostgreSQL** — i.e., on a developer/CI machine with only the .NET SDK. This forces the production DbContext registration (Npgsql → PostgreSQL) to be swappable at test time.

### Options considered

1. **EF Core InMemory provider.** Fastest to set up. But it is NOT a relational store: it ignores unique indexes, foreign keys, `HasFilter` filtered indexes, and check constraints, and does not run raw SQL. Our design leans on a **case-insensitive unique index on `lower(email)` and `lower(team.name)`** and on FK restrict behavior for the 409 delete-guard. InMemory would silently pass tests that production would reject, so it cannot validate our most important integrity rules.
2. **SQLite in-memory (`DataSource=:memory:` / shared cache, chosen).** A real relational engine: enforces FKs (with `PRAGMA foreign_keys=ON`), unique indexes, and check constraints. Runs entirely in-process, no Docker, no server. Close enough to PostgreSQL for our schema. Differences (no `citext`, slightly different SQL dialect, no real `timestamptz`) are handled by keeping case-insensitivity in EF via a normalized stored column (`email_normalized`, `name_normalized`) rather than relying on a PG-specific `citext`/`lower()` expression index — so the SAME model validates identically on both engines.

## Decision

Tests use **EF Core with the SQLite provider over an in-memory connection**. To make this swappable:

- `Program.cs` registers `AppDbContext` via a single extension method `AddAppPersistence(IServiceCollection, IConfiguration)` that reads provider config and calls `UseNpgsql(...)` in production.
- The xUnit integration suite uses a custom `WebApplicationFactory<Program>` that, in `ConfigureWebHost`, **removes** the existing `DbContextOptions<AppDbContext>` and `AppDbContext` registrations and re-adds the context with `UseSqlite(connection)` where `connection` is an explicitly-opened `SqliteConnection("DataSource=:memory:")` kept open for the test's lifetime (closing it drops the DB).
- Schema for tests is created via `db.Database.EnsureCreated()` (NOT `Migrate()`), because EF Core PostgreSQL-targeted migrations contain Npgsql-specific SQL. `EnsureCreated()` builds the schema from the model, which is provider-agnostic. See ADR 0003 for why production uses `Migrate()` and tests use `EnsureCreated()` and how we keep them equivalent.
- Case-insensitivity and uniqueness are modeled as **normalized companion columns with a plain unique index** (portable across PG and SQLite), so the same constraints are exercised in tests as in production.

Each test class gets a fresh database (fresh open `SqliteConnection`) for isolation; tests do not share state.

## Consequences

- **Positive:** Real relational-constraint coverage (unique indexes, FK restrict → our 409 guard, cascade delete of comments) without Docker/PostgreSQL; fast (<1s) in-process startup via `WebApplicationFactory`; CI needs only the .NET SDK.
- **Negative:** SQLite is not byte-identical to PostgreSQL (no `timestamptz`, integer/affinity typing, no native `uuid`). We mitigate by storing GUIDs as `TEXT`/`uuid` via EF value conversion uniformly and storing timestamps as UTC `DateTime` (`timestamptz` in PG, ISO text in SQLite) — both round-trip correctly through EF. The handful of behaviors that are genuinely PG-only (e.g., advisory locks) are not used.
- **Negative:** `EnsureCreated()` vs `Migrate()` divergence risk — addressed in ADR 0003 by a CI check that the migrations recreate the same model snapshot the tests build from.
