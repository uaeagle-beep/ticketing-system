# Ticket Tracker — Frontend (SPA)

React + TypeScript + Vite single-page app for the Ticket Tracker. It talks to
the backend strictly through the REST contract in
[`../docs/API_CONTRACT.md`](../docs/API_CONTRACT.md).

## Stack

- **React 18 + TypeScript**, built with **Vite**.
- **@tanstack/react-query** for all server state (reads, mutations, optimistic
  drag-and-drop with rollback).
- **@dnd-kit** for the Kanban drag-and-drop.
- **react-router-dom** for routing.

No UI framework — styling is a single hand-written `src/styles.css`.

## How it fits the topology (ADR-0005)

- All API calls use the **relative** base `/api`. In production the `web` nginx
  container serves the compiled SPA and reverse-proxies `/api/*` to
  `http://api:8080`, so the browser is single-origin (no CORS). The whole stack
  runs from the repo root with `docker compose up --build` and is reached at
  `http://localhost:8080`.
- For local development without Docker, `vite dev` proxies `/api` to
  `http://localhost:8080` (override with the `VITE_DEV_API_TARGET` env var).

## Auth (ADR-0001)

- Opaque bearer token sent as `Authorization: Bearer <token>`.
- Token is held in memory and mirrored to `localStorage` only for refresh
  continuity (never the system of record; never placed in a URL).
- A `401` on a protected endpoint clears the token and routes to `/login`.
- `GET /api/auth/me` bootstraps the session on load. Login never issues a token
  to an unverified account, so an authenticated user is always verified.

## Scripts

```bash
npm install        # install dependencies
npm run dev        # Vite dev server on http://localhost:5173 (proxies /api)
npm run build      # type-check (tsc -b) + production build to dist/
npm run preview    # preview the production build locally
npm run lint       # eslint
npm test           # run the unit/component test suite once (Vitest)
npm run test:watch # run Vitest in watch mode
npm run coverage   # run tests with a v8 coverage report
```

> Node/npm are **not** required to run the full system — `docker compose up
> --build` builds the SPA inside the multi-stage `Dockerfile`.

## Tests (Vitest + React Testing Library)

Frontend tests run in **jsdom** with **no backend**: the network layer is
mocked with **MSW** ([`src/test/handlers.ts`](src/test/handlers.ts)) so the real
`fetch`-based client (`src/api/client.ts`) is exercised against contract-shaped
responses. Shared scaffolding lives in `src/test/`:

- `setup.ts` — registers `@testing-library/jest-dom`, starts/stops the MSW
  server (`beforeAll` / `afterEach` reset / `afterAll`), runs RTL `cleanup`, and
  resets the auth-token store between tests.
- `server.ts` / `handlers.ts` — MSW server + `/api/*` handlers mirroring
  `docs/API_CONTRACT.md`. Tests override individual routes with `server.use(...)`
  to drive error paths (401/403/409/validation).
- `renderWithProviders.tsx` — renders a component inside the real provider stack
  (`QueryClientProvider` → `MemoryRouter` → `ToastProvider` → `AuthProvider`)
  with retries disabled.

Tests are co-located next to their source as `*.test.ts(x)`.

## Project structure

```
src/
  api/         HTTP client, typed endpoint fns, contract types, token store
  auth/        AuthContext, RequireAuth / PublicOnly route guards
  components/   shared UI: layout/header, states, toasts, modal, badges
  features/
    auth/      login, signup, verify-email (+ resend) screens
    board/     Kanban board, columns, cards, filters, optimistic state mutation
    tickets/   ticket create/edit/details + comments panel
    teams/     team management
    epics/     epic management
  lib/         enum<->label mapping, relative/UTC time, query keys, error mapping
```

## Screens (source §10 / wireframes 1–5)

- **Sign up** `/signup`, **Login** `/login`, **Verify email** `/verify-email`
  (success + expired/invalid + resend), resend reachable from both.
- **Kanban board** `/board` — team selector, `+ New ticket`, filters
  (type / epic / title search, AND logic) + Clear + total count, five
  workflow-ordered columns with count badges, drag-and-drop with immediate
  persist and rollback-on-error, three distinct empty states.
- **Ticket** `/tickets/new` and `/tickets/:id` — all fields, meta line, delete
  with confirmation, comments (oldest-first) + add comment.
- **Teams** `/teams`, **Epics** `/epics` — management tables with guarded
  deletes (disabled while non-empty / referenced; backend stays authoritative).
