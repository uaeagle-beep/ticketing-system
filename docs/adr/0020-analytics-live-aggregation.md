# ADR 0020 — Analytics as one composite read-only endpoint aggregating live over existing tables, bundled Chart.js

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 3 approved scope (analytics / reporting); [`WAVE3_DESIGN.md`](../WAVE3_DESIGN.md) §5.4/§7.6/§10.3
- **Related ADRs:** 0007 (team-scoped authz), 0012 (event backbone → `activity_entries` provide state-transition timing), 0009 (priority/due-date fields), 0002 (SQLite tests / provider-agnostic queries), 0005 (single-server; strict CSP `script-src 'self'`)

## Context

Wave 3 adds a reporting dashboard. Metrics needed (per team, optional date range): tickets by state / priority / type / label, open-vs-done, throughput (done per week), cycle time (created→done), overdue count, WIP-vs-limit. It must stay team-scoped ([ADR-0007]), performant at the "100+ tickets" NFR, testable on SQLite ([ADR-0002]), and its charts must bundle into the SPA (the nginx CSP is `script-src 'self'` — **no external CDN**).

## Decision

- **One composite endpoint:** `GET /api/analytics/dashboard?teamId=&from=&to=` → a single `DashboardDto` with all nine metric groups. Not several small endpoints — the dashboard renders every card from one round-trip, so one query batch is cheaper and one cache key is simpler.
- **No new tables.** All aggregates run **live** over existing rows: `tickets` (state/priority/type/overdue counts, open-vs-done), `ticket_labels` (by-label), `wip_limits` (WIP-vs-limit), and **`activity_entries`** for time-based metrics. Throughput and cycle time derive "when did this ticket reach `done`" from the Wave-2 `ticket_moved` activity entries (`data_json` `{from,to}`), falling back to `modified_at` for pre-Wave-2 tickets. Wave 2 already records every state move, so no `ticket_state_transitions` rollup is needed at this scale.
- **Team-scoped + admin-any.** `teamId` required; resolve team → 404, `RequireTeamAccess` → 403 (admin sees any). **Every aggregate is computed inside `WHERE team_id = @teamId`** — a metric can never leak another team's data (the same guard as the board). Date range is bounded `DateOnly`; `from > to` → 400. All aggregation via EF `GroupBy`/`Count` (provider-agnostic; runs under SQLite in tests) — no raw/free-form SQL.
- **Pre-aggregated payload keeps charts cheap.** The endpoint returns counts/buckets (≤ a few dozen numbers), so the SPA plots a small fixed number of points regardless of ticket volume → the "100+ tickets" NFR is met server-side; the client never iterates raw tickets to chart them.
- **Charts: Chart.js via `react-chartjs-2`, bundled through npm/Vite.** Lightweight, tree-shakeable, mature, **no runtime CDN** (respects the strict CSP). Recharts was considered and rejected (heavier SVG DOM at high point counts). Charts are isolated in `features/analytics/`, so the library is swappable.
- **Session-auth only** (not an API-key `/api/v1` surface in Wave 3) — analytics is a UI concern.

## Consequences

- **Positive:** zero schema change (3 of 5 Wave-3 features need no migration; this is one of them); one endpoint, one cache key; team-scope guaranteed by construction; provider-agnostic queries run in the existing SQLite test harness; charts bundle cleanly under the existing CSP.
- **Negative (accepted):** live aggregation could slow down at very large data volumes (single big team). Mitigation/trigger (R-A1): if p95 dashboard latency exceeds budget at real volume, add a rollup/materialized table or a `ticket_state_transitions` table — a separate, additive feature. At hackathon/single-server scale this is not warranted.
- **Dependency on Wave-2 activity data:** throughput/cycle-time accuracy depends on `activity_entries` `ticket_moved` rows; pre-Wave-2 tickets use the `modified_at` fallback (documented, [ASSUMPTION W3-AN-TIMING-SOURCE]).
