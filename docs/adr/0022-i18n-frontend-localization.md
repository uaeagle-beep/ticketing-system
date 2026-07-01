# ADR 0022 — Frontend localization (uk default + en) via react-i18next; backend keeps stable error codes; sequenced last

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 3 approved scope (i18n uk/en); [`WAVE3_DESIGN.md`](../WAVE3_DESIGN.md) §4.6/§5.7/§10.5/§12.1
- **Related ADRs:** 0006 (stable error codes in the envelope), 0004/0014 (email sender + digest — email locale), 0010 (self-service profile — `PUT /api/me/profile`)

## Context

Wave 3 localizes the SPA into Ukrainian and English, default Ukrainian (the PO/users are Ukrainian). This is a **large, mechanical** change touching every component. The backend already returns **stable machine error codes** ([ADR-0006]) that the SPA maps to human messages in `frontend/src/lib/errors.ts`; dates/times are formatted in `frontend/src/lib/time.ts`; enum display labels live in `frontend/src/lib/labels.ts`. The design must decide where localization lives (frontend vs backend), how the choice persists, how emails are handled, and when in the wave to do it.

## Decision

- **Localization is a frontend concern; the backend keeps stable CODES.** No API-error message localization. The SPA maps `code → localized message` by turning the existing `lib/errors.ts` `FRIENDLY` map into i18n keys (`errors:<code>`). Codes remain the contract ([ADR-0006] unchanged).
- **`react-i18next` (+ `i18next` + `i18next-browser-languagedetector`).** Mature, standard, supports namespaces and lazy bundles. Languages **uk + en**, **default `uk`**. Detection order: persisted user choice → `localStorage` → `navigator.language` → `uk`.
- **Persistence: `localStorage` (authoritative for the UI) mirrored to an optional `users.locale` column.** localStorage is instant/offline; the profile column (nullable `varchar(5)`, `uk`|`en`) makes the choice follow a user across devices and lets the backend localize emails. Set via the existing self-profile surface: `PUT /api/me/profile` gains an optional `locale`; `locale` is returned in `/me`/login so the SPA sets the language on bootstrap. Invalid locale → 400 keyed `locale`. This is the **only** backend change i18n needs (one nullable column + one DTO field), and the column ships with the Phase-2 migration so i18n itself needs **no** migration.
- **Resource bundle structure** (`frontend/src/locales/{uk,en}/*.json`) namespaced by feature area (`common`, `auth`, `board`, `tickets`, `teams`, `epics`, `labels`, `notifications`, `analytics`, `integrations`, `account`, `errors`, `enums`). `enums` replaces `lib/labels.ts` state/type/priority labels with i18n keys; `errors` replaces the `FRIENDLY` map; `lib/time.ts` becomes locale-aware via `Intl`/`toLocaleDateString(locale, …)`.
- **Emails: per-recipient locale via `users.locale`, pragmatic scope.** The digest and Wave-3-relevant copy are localized server-side using `users.locale` (default `uk`); verification/reset templates are localized if cheap, else remain English with a follow-up. Rationale: get the UI fully localized first; email templates are fewer and lower-traffic.
- **Sequenced LAST in the wave.** String extraction happens **after** attachments, webhooks/api-keys, real-time, and analytics ship — so every screen (including those new ones) exists and each string is extracted exactly once. Doing i18n earlier would force re-extracting the freshly-added UIs.

## Consequences

- **Positive:** clean separation (codes stay the contract, presentation localizes); standard tooling; instant + cross-device persistence; a single small backend surface; no dedicated migration; extraction done once (no churn) by sequencing last.
- **Negative (accepted):** a large mechanical diff across the whole SPA (unavoidable for full localization) — contained by feature-namespaced bundles and by doing it last. Email localization is partial in the initial delivery (pragmatic default, PO-vetoable). Two languages must be kept in sync as future features add strings — a future `i18next-parser`/translation-lint in CI is recommended but out of scope.
- **Reversibility:** dropping `users.locale` (localStorage-only) is a one-column revert; then emails stay single-language.
