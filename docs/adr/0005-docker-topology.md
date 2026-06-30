# ADR 0005 — Docker topology: three containers (postgres + backend API + nginx-served SPA with /api proxy)

- **Status:** Accepted
- **Date:** 2026-06-30
- **Deciders:** Architect
- **Source refs:** REQUIREMENTS_SOURCE §2 (three tiers, compose up --build), §9; ANALYSIS FR-E8-1/2, FR-E7-*, A29
- **Related ADRs:** 0001 (auth), 0003 (migrations)

## Context

The source mandates three clearly separated logical tiers and a single `docker compose up --build` from the repo root that starts everything on a clean Windows/macOS/Linux laptop with only Docker (§2). The frontend and backend may be separate containers OR the backend may serve the compiled SPA, as long as the tiers stay separated (§2).

### Options considered

1. **Backend serves the compiled SPA** (single app container + DB). Fewer containers, but mixes presentation delivery into the API process and complicates SPA fallback routing and asset caching headers.
2. **Three containers: postgres + backend + nginx serving the SPA and reverse-proxying `/api` to the backend (chosen).** Cleanest tier separation; nginx handles static assets, SPA history-fallback (`try_files ... /index.html`), and proxies `/api/*` to the backend so the browser sees a single same-origin (no CORS, simpler cookie/bearer handling). Each tier is an independent, independently-scalable container.

## Decision

Three services in the root `docker-compose.yml`:

| Service | Image / build | Host port | Internal | Role |
|---|---|---|---|---|
| `db` | `postgres:17-alpine` | (not published by default) | 5432 | PostgreSQL persistence tier |
| `api` | build `./backend` (multi-stage .NET 10 SDK → ASP.NET runtime) | (not published; reached via nginx) | 8080 | ASP.NET Core Web API |
| `web` | build `./frontend` (node build → nginx:alpine) | **8080:80** (host:container) | 80 | nginx: serves SPA + proxies `/api` → `api:8080` |

- **Single entry point:** the user opens `http://localhost:8080`. nginx serves the SPA; any request to `/api/...` is proxied to `http://api:8080/...`. The browser therefore makes only same-origin requests → no CORS config needed in the common path (a permissive dev CORS policy is still added on the API for direct API testing).
- **Healthchecks & ordering:**
  - `db` healthcheck: `pg_isready -U $POSTGRES_USER`; interval 5s, retries 10.
  - `api` `depends_on: db: condition: service_healthy`; the API additionally retries the DB connection at startup (ADR 0003) to cover the migration race. `api` healthcheck hits `GET /health/live`; readiness `GET /health/ready` verifies DB connectivity + migrations applied.
  - `web` `depends_on: api: condition: service_started` (nginx tolerates a briefly-unavailable upstream and returns 502 until the API is up).
- **Volumes:** named volume `pgdata:/var/lib/postgresql/data` so persisted data survives `docker compose down`/restart (NFR-REL-1). `down -v` clears it for a truly fresh DB.
- **Config:** all secrets/config via `.env` at repo root (compose `env_file`), with `.env.example` committed (defaults incl. `SMTP_HOST=relay1.dataart.com`). `.env` is gitignored.
- **Images:** backend is a multi-stage build (SDK to publish, then `mcr.microsoft.com/dotnet/aspnet:10.0` runtime, non-root user). Frontend is a multi-stage build (`node:22-alpine` to `npm run build`, then `nginx:alpine` serving `/usr/share/nginx/html`). `VITE_*` build-time vars are baked at build; runtime API base is just the relative `/api` so no rebuild is needed per environment.

## Consequences

- **Positive:** Strict three-tier separation; single same-origin entry point eliminates CORS/cookie cross-site complexity; nginx handles SPA routing and static caching; DB isolated with a healthcheck gate; one command (`docker compose up --build`) brings the stack up on any OS with Docker.
- **Negative:** Three images to build (slightly longer first build). The `web → api` proxy is a single point for the API path (fine for a single-node hackathon deployment). Publishing only port 8080 keeps the surface minimal; developers who want to hit Postgres directly can add a published port locally.
