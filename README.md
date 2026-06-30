# Ticket Tracker

A small, working Kanban-style ticket tracker built as a three-tier single-page
application backed by a relational database. Mandatory scope: authentication
with email verification, teams, epics, tickets, comments, and a draggable Kanban
board.

The entire solution starts from this repository root with a single command and
requires **only Docker** on the host — no host-installed .NET, Node, or
PostgreSQL runtime.

---

## Architecture overview (three tiers)

```
 Browser (Chrome/Edge/Firefox)
        │  http://localhost:8080
        ▼
 ┌──────────────────────────────────────────────────────────────┐
 │ Docker Compose network                                         │
 │                                                                │
 │  web  (nginx)            api  (ASP.NET Core .NET 10)    db      │
 │  serves the SPA   ──/api──►  EF Core / Npgsql   ──5432──► Postgres 17
 │  + reverse proxy            Argon2id, MailKit SMTP      (pgdata │
 │  :80 (host 8080)            http://+:8080 (internal)     volume)│
 └──────────────────────────────────────────────────────────────┘
        │ MailKit SMTP (587)
        ▼
 relay1.dataart.com   (verification emails)
```

- **Presentation** — React + TypeScript + Vite SPA, served as static assets by
  **nginx**. nginx also reverse-proxies `/api/*` to the API, so the browser is
  single-origin (no CORS in the common path) and falls back unknown routes to
  `/index.html` for SPA history routing.
- **Application / API** — ASP.NET Core Web API (.NET 10), layered into
  API → Application → Domain with EF Core (Npgsql), Argon2id password hashing,
  and MailKit for SMTP. Listens on `http://+:8080` **inside the network only**.
- **Persistence** — PostgreSQL 17 in its own container with a named volume so
  data survives restarts.

Only the `web` service publishes a host port (`8080`). The `api` and `db`
services are reachable only inside the compose network.

The authoritative design lives in [`docs/`](./docs):

| Document | Purpose |
|---|---|
| [`docs/REQUIREMENTS_SOURCE.md`](./docs/REQUIREMENTS_SOURCE.md) | Canonical requirements (source of truth) |
| [`docs/REQUIREMENTS_ANALYSIS.md`](./docs/REQUIREMENTS_ANALYSIS.md) | Business analysis: user stories + acceptance criteria |
| [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) | Technical design, data model, configuration |
| [`docs/API_CONTRACT.md`](./docs/API_CONTRACT.md) | REST API contract |
| [`docs/adr/`](./docs/adr) | Architecture Decision Records (ADR-0001 … ADR-0006) |

The Docker topology is fixed by [ADR-0005](./docs/adr/0005-docker-topology.md).

---

## Prerequisites

- **Docker Desktop** (Windows / macOS) or **Docker Engine** (Linux) with the
  **Docker Compose v2** plugin (the `docker compose` subcommand).
  - Verify with: `docker --version` and `docker compose version`.
- Nothing else. .NET, Node, and PostgreSQL all run inside containers.

---

## Quick start

From the repository root:

```bash
# 1. Create your local .env from the template (only needed once).
cp .env.example .env            # Linux / macOS
# Copy-Item .env.example .env   # Windows PowerShell

# 2. (Optional) edit .env — set AUTH_TOKEN_SECRET and, if your relay needs it,
#    the SMTP_* credentials. The defaults are fine for a first local run.

# 3. Build and start the whole stack.
docker compose up --build
```

> If no `.env` is present, Compose still starts using the values baked into
> `docker-compose.yml` defaults where provided, but creating `.env` from
> `.env.example` is the supported path and is required to set secrets.

Then open:

- **App:** http://localhost:8080

To stop: `Ctrl+C`, then `docker compose down`. To stop **and wipe the database**
(fresh schema next start): `docker compose down -v`.

The API and database deliberately do **not** expose host ports — everything is
reached through http://localhost:8080.

---

## Configuration (environment variables)

All configuration comes from `.env` (gitignored). `.env.example` is committed
with safe local defaults and a comment on every variable. Compose injects them
into the containers. **No secrets are committed.**

| Variable | Consumed by | Default in `.env.example` | Purpose |
|---|---|---|---|
| `POSTGRES_USER` | db, api | `ticketing` | PostgreSQL user |
| `POSTGRES_PASSWORD` | db, api | `change-me-local` | PostgreSQL password (local dev only) |
| `POSTGRES_DB` | db, api | `ticketing` | PostgreSQL database name |
| `ConnectionStrings__Default` | api | `Host=db;Port=5432;Database=ticketing;Username=ticketing;Password=change-me-local` | EF Core / Npgsql connection string |
| `AUTH_TOKEN_SECRET` | api | `replace-with-a-long-random-secret` | HMAC pepper for the opaque bearer-token model (ADR-0001) — set a strong random value |
| `SESSION_TTL_HOURS` | api | `72` | Session (bearer) token lifetime in hours |
| `TOKEN_TTL_HOURS` | api | `24` | Email-verification token lifetime (source §3: 24h, single-use) |
| `SMTP_HOST` | api | `relay1.dataart.com` | SMTP relay host (must support relay1.dataart.com) |
| `SMTP_PORT` | api | `587` | SMTP port (587 = STARTTLS submission) |
| `SMTP_USERNAME` | api | _(empty)_ | SMTP auth user (leave blank if the relay is unauthenticated) |
| `SMTP_PASSWORD` | api | _(empty)_ | SMTP auth password — never committed |
| `SMTP_USE_STARTTLS` | api | `true` | Upgrade the SMTP connection to TLS via STARTTLS |
| `EMAIL_FROM` | api | `no-reply@ticketing.local` | `From:` header on verification emails |
| `FRONTEND_URL` | api | `http://localhost:8080` | Base for verification links (`${FRONTEND_URL}/verify-email?token=…`) |
| `RUN_MIGRATIONS_ON_STARTUP` | api | `true` | Apply EF Core migrations on startup (ADR-0003); never seeds data |
| `WEB_PORT` | compose | `8080` | Host port mapped to nginx:80 |

---

## Configuring SMTP (email verification)

After sign-up the API sends a verification email through the configured SMTP
relay. The system must support **`relay1.dataart.com`** (source §3).

1. In `.env`, set:
   - `SMTP_HOST=relay1.dataart.com`
   - `SMTP_PORT=587`
   - `SMTP_USE_STARTTLS=true`
   - `EMAIL_FROM=no-reply@ticketing.local` (or your sender address)
2. If the relay requires authentication, set `SMTP_USERNAME` / `SMTP_PASSWORD`.
   If it is open on your network, leave both **blank**.
3. `FRONTEND_URL` must be the address QA actually opens (default
   `http://localhost:8080`) so the emailed verification link resolves.

**Local testing without a real relay:** point `SMTP_HOST` at a local mail
catcher (e.g. MailDev or Mailpit) and set `SMTP_USE_STARTTLS=false` and the
appropriate port. The verification email then lands in the catcher's inbox.

> A transient SMTP failure does not roll back account creation (ADR-0004): the
> account is still created, and the user can request a new verification email
> via the **resend** action.

---

## Testing

Three layers of tests: backend (.NET), frontend unit/component (Vitest), and
end-to-end (Playwright against the full Docker stack).

### Backend (`dotnet test`)

The integration tests run with **only the .NET SDK** — no Docker, no PostgreSQL
(they use an in-memory SQLite database and a fake email sender, per ADR-0002 /
ADR-0004):

```bash
cd backend
dotnet test
```

This covers at least one backend business flow and one full API/HTTP flow
(see [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) §10).

### Frontend unit/component (Vitest)

Fast jsdom tests for the SPA; the network is mocked with MSW, so **no backend is
required**:

```bash
cd frontend
npm i           # first run only (installs dev dependencies)
npm test        # vitest run (one-shot); `npm run test:watch` for watch mode
```

### End-to-end (Playwright)

The E2E suite drives a real browser against the **full Docker stack**. Because
the happy path needs to read the email-verification link, an override compose
file adds a **Mailpit** mail catcher and points the API's SMTP at it (so the
verification email is captured instead of being relayed to `relay1.dataart.com`).

1. Bring up the stack with the E2E override (from the repo root):

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.e2e.yml up --build -d
   ```

   This publishes the app on http://localhost:8080 and Mailpit's UI/REST API on
   http://localhost:8025.

2. Run the tests:

   ```bash
   cd frontend
   npm i                          # first run only
   npx playwright install         # first run only (downloads browsers)
   npm run e2e                     # headless run
   # npm run e2e:ui                # interactive Playwright UI mode
   ```

   Override the targets if needed with `E2E_BASE_URL` (default
   `http://localhost:8080`) and `MAILPIT_BASE_URL` (default
   `http://localhost:8025`).

3. Tear the stack down (and wipe the DB volume):

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.e2e.yml down -v
   ```

The specs live in [`frontend/e2e/`](./frontend/e2e):

- **`smoke.spec.ts`** — public pages only (no account needed): `/login` and
  `/signup` render, client-side validation (password < 8, mismatched passwords),
  login ↔ signup navigation, and the verify-email screen showing an error +
  resend action for an invalid token.
- **`happy-path.spec.ts`** — the full flow: signup → read the verification link
  from Mailpit → verify → login → create a team → create an epic → create a
  ticket → open it → add a comment → return to the board → drag the card to a new
  column → reload and assert the move persisted. The verification link is read
  via the Mailpit REST API (`frontend/e2e/helpers/mailpit.ts`).

### Continuous integration

[`.github/workflows/ci.yml`](./.github/workflows/ci.yml) runs three jobs on every
push and pull request:

| Job | What it does |
|---|---|
| **backend** | `actions/setup-dotnet` (10.x) → `dotnet test backend/TicketTracker.sln`. |
| **frontend-unit** | `actions/setup-node` (20.x) → install deps → `npm run test -- --run` (Vitest). |
| **e2e** | Brings up the stack with the Mailpit override, waits for `GET /health/ready`, installs Node + Playwright browsers, runs `npm run e2e`, uploads the HTML report + traces as artifacts, then `docker compose … down -v`. It runs `continue-on-error` (does not block the merge signal) while the suite is being stabilized in CI. |

---

## Scope

**In scope (mandatory):**

- Sign-up with email + password, Argon2id hashing, email verification
  (24h single-use token), resend, login/logout.
- Teams: create / rename / delete (delete blocked with **409** while the team
  has tickets or epics).
- Epics: per-team CRUD; team immutable after creation; delete blocked with
  **409** while referenced by tickets.
- Tickets: create / view / edit / delete (cascades comments); strict enum and
  reference validation; correct `modified_at` semantics.
- Comments: add and list (oldest-first); immutable; do not bump ticket
  `modified_at`.
- Kanban board: five fixed columns, drag-and-drop state change persisted
  immediately with rollback on failure, filtering by type/epic + title search.

**Out of scope:** Scrum / sprints / backlogs, SSO / OAuth, roles / membership /
private teams, attachments, notifications / mentions / watchers, real-time
updates, custom workflows / types, reporting dashboards, production deployment
and HA. (See source §12.)

---

## Troubleshooting

**Port 8080 is already in use** — change the published host port without
touching the rest of the stack: set `WEB_PORT` in `.env` (e.g. `WEB_PORT=8090`)
and reopen the app at `http://localhost:8090`. Also update `FRONTEND_URL` to
match so verification links resolve. Then `docker compose up --build`.

**First start / first migration is slow or the API briefly returns 502** — on a
clean machine the first run builds three images and PostgreSQL initializes its
data directory. The `api` waits for the `db` healthcheck, then applies EF Core
migrations before serving traffic (ADR-0003); until that finishes, nginx may
return **502** for `/api/*`. Give it a few seconds and refresh. Watch progress
with `docker compose logs -f api`. `GET /api/health/ready` (via the proxy)
reports `200` only once migrations are applied.

**Reset to a completely fresh database** — `docker compose down -v` removes the
`pgdata` volume. The next `up` recreates the schema (and migration metadata)
with **no application data**.

**Changes not picked up** — rebuild images explicitly:
`docker compose up --build --force-recreate`.

**SMTP / verification email never arrives** — check `docker compose logs api`
for send errors, confirm `SMTP_HOST`/`SMTP_PORT`/credentials in `.env`, and use
the in-app **resend** action. For offline testing, point `SMTP_HOST` at a local
mail catcher (see "Configuring SMTP").
