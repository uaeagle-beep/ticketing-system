# UX/UI Design: WIP (Work In Progress) Limits

> **Role:** UX/UI Designer. This document is a **specification**, not production code. `developer` implements
> against it; `tester` derives usability/a11y checks from it. Code-like snippets (markup/CSS/JSON) are
> illustrative spec, not drop-in code.
> **Companions:** [`REQUIREMENTS_SOURCE.md`](./REQUIREMENTS_SOURCE.md) §8 (Kanban), §15 (wireframes),
> [`ARCHITECTURE.md`](./ARCHITECTURE.md) §6.5/§7, [`API_CONTRACT.md`](./API_CONTRACT.md) §4/§6.
> **Stage:** Design, iterating with `architect` (contract gaps below) and `business-analyst` (fixed product
> decisions given). Interface language: **English**.

---

## 0. TL;DR (for the handoff)

- **Where limits are configured:** on the **Team management screen** (Wireframe 4), in a per-team
  **"WIP limits" expander/panel** opened from a new row action, with one numeric field per state (5 fields).
  Rationale below (§2.1).
- **Control:** native `<input type="number" min="1" step="1" inputmode="numeric">` (not a stepper), empty =
  "No limit". One **Save limits** button per panel (explicit save), not autosave (§2.3).
- **Column badge format:** `N / max` when a limit is set, `N` when unlimited. Three visual states:
  under / **at limit (full)** / **over limit**. Color **plus** icon **plus** text — never color alone (§3).
- **Block message (drag / create / edit into a full state)** — exact text:
  `This status already has the maximum number of tickets — finish existing ones first.`
  Shown as a **toast** for drag (card rolls back), and **inline** for the ticket form. (§4)
- **Validation message (invalid limit value):**
  `Enter a whole number of 1 or more, or leave blank for no limit.` (§2.2)

---

## 1. Context, users, JTBD (traceability)

### 1.1 What this feature is
A WIP limit caps how many tickets may sit in a given board column (state) for a team. It is the core Kanban
mechanism for surfacing bottlenecks: when a column is "full", the team is told to **finish work before
starting more**. Source §8 fixes the five states and the board model; this feature adds an optional ceiling
per state, **per team**.

### 1.2 Users & jobs-to-be-done
All verified users can view and manage all teams (Source §4) — there are no roles. So the same person both
configures limits and works the board.

- **JTBD-1 (configure):** *When I set up how my team works, I want to cap how many tickets can be
  "in progress" (or any state) at once, so we focus on finishing instead of starting.*
- **JTBD-2 (stay within limits while working):** *When I look at the board, I want to see at a glance how
  close each column is to its cap, so I don't even try to overfill it.*
- **JTBD-3 (understand a rejection):** *When the system stops me from moving/creating a ticket into a full
  column, I want a clear reason and to not lose my place, so I know what to do next.*

### 1.3 Fixed product decisions (design within these — given)
1. Limits are **per-team**, one per state: `new`, `ready_for_implementation`, `in_progress`,
   `ready_for_acceptance`, `done`. Empty / unset = **unlimited**.
2. Valid value = **integer ≥ 1**, or empty (unlimited). Invalid (negative, 0, decimal, non-numeric,
   too large) → inline validation, rejected.
3. Drag **or** create/edit into a state that has reached its limit → **backend rejects**; UI shows the block
   message; a dragged card **rolls back** to its previous column.
4. Interface is **English**.

### 1.4 Traceability
| Need | Source | This doc |
|---|---|---|
| Five fixed states / board model | §6, §8; ARCH §4.2 | §2, §3 |
| Drag persists immediately; on failure card returns + UI shows error | §8; ARCH §6.5; CONTRACT §6.5 | §4.2 |
| Loading/empty/success/error states everywhere | §11 NFR-USE-1 | §5 |
| Meaningful messages for validation/conflict | §9, §11 NFR-USE-3 | §2.2, §4 |
| Per-team scope | Product decision (given) | §2.1 |

---

## 2. Configuring limits (per-team, per-state)

### 2.1 Placement decision — Team management screen

**Chosen:** put limit configuration on **Team management** (`/teams`, Wireframe 4 / `TeamsPage.tsx`),
exposed per team via a new **"WIP limits"** row action that opens an inline panel for that team.

**Why here (rationale):**
- The board is **per selected team** (`BoardPage` selects one team; `useBoardQuery(teamId, …)`), and limits
  are **per-team** — so the natural owner of "team-level settings" is the team, which lives on `/teams`.
- `TeamsPage` already owns team CRUD with an established pattern: a per-row action set (`Edit` / `Delete`)
  and an **inline editing region** that expands within the table flow (rename uses an inline `<form>` in the
  Name cell; create uses `.inline-form`). A "WIP limits" panel reuses that exact mental model and styling.
- Keeping it off the board avoids cluttering the primary working screen (Wireframe 1 is already dense:
  selector, New ticket, filter bar, 5 columns). The board only **reads/visualizes** limits (§3).

**Alternatives considered & rejected:**
- *Inline editing the limit in each column header on the board.* Rejected: editing settings on the working
  surface is error-prone (easy to fat-finger while dragging), the header is small (target-size pressure,
  WCAG 2.5.8), and it duplicates a settings affordance the team screen should own. The board still **shows**
  the limit (that's the point of §3) — it just doesn't **edit** it.
- *A separate `/settings` screen.* Rejected: out of scope of the minimum screens (Source §10); over-built
  for one team-scoped setting; adds navigation cost.

> **Note to `business-analyst`:** if a future requirement adds more team-level settings, this panel
> generalizes into a "Team settings" panel. Not needed now.

### 2.2 The limits panel — layout, control, validation

Opened by a new per-row action **`WIP limits`** (alongside `Edit` / `Delete`). It expands a panel **below the
team's row** (full-width, spanning all columns via a `colspan` row), styled like the existing `.panel` /
`.inline-form` surfaces. Only one team's panel is open at a time (like inline rename: `editingId`).

```
+--------------------------------------------------------------------------+
| Name        | Tickets | Epics | Modified      | Actions                  |
+--------------------------------------------------------------------------+
| Platform    |   12    |   3   | Jun 22, 10:15 | [Edit] [WIP limits] [Del] |
+--------------------------------------------------------------------------+
| > WIP limits for Platform                                                 |  <- expanded panel row (colspan=5)
|   Cap how many tickets each column can hold. Leave a field blank for      |
|   no limit.                                                               |
|                                                                          |
|   NEW                         [        ] tickets   (blank = no limit)     |
|   READY FOR IMPLEMENTATION    [   5    ] tickets                          |
|   IN PROGRESS                 [   3    ] tickets                          |
|   READY FOR ACCEPTANCE        [        ] tickets   (blank = no limit)     |
|   DONE                        [        ] tickets   (blank = no limit)     |
|                                                                          |
|            [ Save limits ]   [ Cancel ]                                   |
+--------------------------------------------------------------------------+
```

**Control choice — numeric input, not a stepper.**
Use `<input type="number" min="1" step="1" inputmode="numeric" />` reusing the existing `.input` class.
Rationale: limits can plausibly be any small integer and occasionally larger; a stepper (+/- buttons) makes
reaching e.g. 12 tedious, adds two tiny targets per row (WCAG 2.5.8 pressure), and the empty="no limit"
semantics are clumsy with a stepper. A plain numeric field types fast, supports blank-for-unlimited
naturally, and matches every other field in the app (all use `.input`/`.select`). `inputmode="numeric"`
gives a numeric soft keyboard on touch.

- **Label** per field: the **state display label** from `lib/labels.ts` (`stateLabel(state)`), rendered
  UPPERCASE to match column headers (Wireframe 1 / `.board-column-header`). Each `<label htmlFor>` is bound
  to its input id (e.g. `wip-in_progress`).
- **Unlimited affordance:** placeholder text `No limit` inside the empty field, plus the field hint
  `Blank = no limit` (reuse `.field-hint`). An empty field is the canonical "unlimited".
- **Units:** trailing static text `tickets` after each input for scannability (not part of the value).

**Inline validation (per field, reuse `.field-error`):**
- Validate **on blur** and **on submit** (not on every keystroke — avoids nagging mid-typing; matches the
  app's submit-time validation in `TicketPage`/`TeamsPage`).
- A value is **valid** if it is empty **or** a whole number ≥ 1 within a sane max. Recommended max **999**
  (a board with 1000+ in one column is implausible and `type=number` should still be guarded; confirm exact
  ceiling with `architect`/`business-analyst`).
- Invalid triggers: `0`, negatives, decimals (`2.5`), non-numeric (`abc`, blank-after-spaces that the
  browser may pass as `e`/`+`), or above max.
- **Exact validation message (one string covers all invalid cases for clarity):**
  > `Enter a whole number of 1 or more, or leave blank for no limit.`
  - For the over-max case specifically, show:
    > `Enter a number no greater than 999.`
- Wire-up: invalid input gets `aria-invalid="true"` and `aria-describedby` pointing at the `.field-error`
  node; the **Save limits** button is disabled while any field is invalid; first invalid field receives
  focus on a blocked submit.

> **Why client validation is not enough (and is still required):** per Source §6 / ARCH §3.3 the backend
> re-validates every value. The inline message is for fast feedback; the backend is authoritative and may
> still return `400 validation_error` (handled in §5, "save error").

### 2.3 Saving — explicit button, not autosave

**Chosen: explicit `Save limits` button** that saves all five fields in one request; `Cancel` discards and
collapses the panel.

**Why explicit save:**
- Five interdependent values entered together read as **one settings form**; batching avoids five separate
  network round-trips and five separate toasts.
- Autosave-on-blur is risky here: a half-typed value (`2` on the way to `20`) could persist and momentarily
  make a column "over limit". Explicit save commits a deliberate, validated set.
- Matches the app's existing form-submit pattern (`TicketPage` Save, `TeamsPage` rename/create).

**Button states:** `Save limits` → `Saving…` (disabled, reuse the `…` busy convention) → success toast
`WIP limits saved.` and panel collapses. Disabled while any field invalid or while `isPending`.

### 2.4 Component spec — `WipLimitsPanel`

```
Component: WipLimitsPanel
Location (proposed): frontend/src/features/teams/WipLimitsPanel.tsx
Used by: TeamsPage (one expanded panel per team row, mirroring inline-rename state)
Props:
  team: Team               // includes wipLimits (see §6 contract need)
  busy: boolean            // save in flight
  onSave: (limits: Record<TicketState, number | null>) => void
  onCancel: () => void
Internal state: per-state string field values + per-state error map (Partial<Record<TicketState,string>>)
Reuse: .panel surface, .field / .input / .field-error / .field-hint, .btn .btn-primary / .btn-secondary,
       stateLabel()/orderedStates from lib/labels.ts, useToast()
A11y: each input has bound <label>, aria-invalid + aria-describedby on error; panel has
      role="group" aria-label="WIP limits for <team name>"; on open, focus moves to the first field;
      Esc cancels (parity with ConfirmDialog dismissal feel).
```

State map for fields (controlled): store as **strings** (so `""` = unlimited and partial input is preserved),
convert to `number | null` only at save. `null`/omit = unlimited in the request body.

---

## 3. Showing limits on the board (column badge + full/over states)

### 3.1 Badge format

In `BoardColumn` header, the existing `<CountBadge count={column.count} />` is **extended** to show the limit:

- **Unlimited (no limit set):** `N` — unchanged from today (e.g. `8`).
- **Limit set:** `N / max` — current count over the cap (e.g. `3 / 3`, `5 / 4`).

> **Important data correctness note (for `architect`, see §6):** the badge numerator must be the column's
> **true total ticket count for the team**, not the post-filter count. Today `column.count` is **post-filter**
> (A23) — if a user filters by type, `count` drops, which would make a full column look not-full and mislead
> the limit signal. The limit comparison and the `N` in `N / max` must use an **unfiltered per-state total**.
> Until the contract provides it (§6), the badge must not render `N / max` from a filtered count.

### 3.2 Three visual states (color + icon + text — never color alone, WCAG 1.4.1)

| State | Condition | Visual | a11y text |
|---|---|---|---|
| **Under limit** | `count < max` (or no limit) | Default count badge (existing `.badge-count`, neutral grey). No icon. | none extra |
| **At limit (full)** | `count === max` | Warning treatment: amber background, `max` shown, a small **"full" lock/▣ icon** before the count. Border `2px` so it reads at a glance. | column `aria-label` appended: `, full (3 of 3)` |
| **Over limit** | `count > max` | Danger treatment: red background, **▲ warning icon**, count emphasized. (Occurs only via direct API / stale data — backend blocks new entries, but a lowered limit can leave a column temporarily over.) | column `aria-label` appended: `, over limit (5 of 4)` |

Color tokens (reuse existing CSS variables, no new palette needed):
- At-limit (full): background `var(--warning-bg)` (#fffae6), text/icon `var(--warning-text)` (#974f0c).
- Over-limit: background `var(--error-bg)` (#ffebe6), text/icon `var(--error-text)` (#bf2600).
- Both pass non-text contrast ≥ 3:1 for the badge boundary/icon against the `#ebecf0` column background
  (WCAG 1.4.11), and text ≥ 4.5:1 (these are the same tokens the app already uses for banners).

**Why visible before the attempt:** the whole point (JTBD-2) is that the user sees the column is full
**before** dragging into it. The "full" badge + an optional subtle column-header tint is the pre-emptive cue;
the block message (§4) is the fallback if they try anyway.

### 3.3 Column header mock (full state)

```
+-------------------------------+        +-------------------------------+
|  IN PROGRESS         [ 3 / 3 ]|        |  IN PROGRESS    [▣ 3 / 3]     |   <- "full": amber pill + lock icon
+-------------------------------+        +-------------------------------+
        (under, e.g. 2/3)                       (at limit / full)

+-------------------------------+
|  IN PROGRESS    [▲ 5 / 4]     |   <- "over": red pill + warning icon (rare; lowered limit / direct API)
+-------------------------------+
```

ASCII can't show color; color is defined in §3.2. The icon + the `/max` text carry the meaning without color.

### 3.4 Optional drop-affordance cue (recommended, not required)

When a drag is in progress and the user hovers a **full** column, in addition to the existing `.drop-active`
outline, tint the drop target with the warning treatment and keep `aria-disabled`-style messaging via the
existing dnd announcements (e.g. append `(this column is full)` to the dnd `onDragOver` announcement). This is
a **hint**, not a hard block — the authoritative block is the backend rejection on drop (§4.2). Mark as
**recommended**; the mandatory behavior is the post-drop rollback + message.

### 3.5 Component spec — extended `CountBadge` / new `WipBadge`

```
Component: WipBadge (extends/ô replaces CountBadge usage in BoardColumn header)
Location: frontend/src/components/Badges.tsx
Props: count: number; limit: number | null
Render:
  limit == null            -> <span class="badge-count">{count}</span>            (unchanged)
  count <  limit           -> <span class="badge-count">{count} / {limit}</span>
  count == limit           -> <span class="badge-count is-full"  >{fullIcon} {count} / {limit}</span>
  count >  limit           -> <span class="badge-count is-over"  >{warnIcon} {count} / {limit}</span>
A11y: badge text already conveys numbers; the icon is decorative (aria-hidden). The full/over status is
      ALSO surfaced on the column's aria-label (§3.2) so screen-reader users learn it without seeing color.
CSS (spec): add .badge-count.is-full {background:var(--warning-bg);color:var(--warning-text);
            border:1px solid var(--warning-text);} and .badge-count.is-over {background:var(--error-bg);
            color:var(--error-text);border:1px solid var(--error-text);}
```

---

## 4. Feedback when an operation is blocked

There are two entry points where the limit is hit. The backend is authoritative in both; the UI reacts to a
non-2xx response.

### 4.1 Exact message text (English)

Primary block message (used in both surfaces below):
> **`This status already has the maximum number of tickets — finish existing ones first.`**

This is the product-mandated phrasing. If the backend returns a per-state detail, the UI may optionally name
the column, e.g.: `“In progress” already has the maximum number of tickets — finish existing ones first.`
(treat the named variant as **recommended**; the base string above is the required default).

### 4.2 Drag-and-drop into a full column → toast + rollback

Reuse the existing optimistic-move + rollback machinery (`useMoveTicketMutation` in `useBoard.ts`, ARCH §6.5):

1. User drops a card into a full column. The card moves **optimistically** (existing behavior).
2. `PATCH /api/tickets/{id}/state` returns the limit rejection (see §6 for the exact code/status to agree
   with `architect`).
3. `onError` **rolls the card back** to its previous column (already implemented — restores the snapshot).
4. `BoardPage`'s `onError` shows a **toast** via `useToast().showError(...)`. The toast text is the §4.1
   message (mapped from the limit error code in `lib/errors.ts`, see §6).

**Why a toast (not inline) for drag:** the action is transient and pointer/keyboard-driven on the board;
there is no form to attach an inline message to, and the card visibly snapping back + a toast is the
established pattern for drag failures (FR-E6-5 / EC10). The toast component already uses
`aria-live="assertive"` (`role="status"` per toast), so the message is announced (WCAG 4.1.3).

**Keyboard-drag parity:** the same flow applies to keyboard moves (dnd KeyboardSensor). After rollback, move
focus back to the moved card's drag handle so a keyboard user isn't dropped at the page top (a11y).

### 4.3 Create / edit a ticket into a full state → inline (form) + keep input

On the ticket form (`TicketPage`), the destination state is chosen in the **State** `<select>`. When
create/update is rejected for the limit:

1. Do **not** navigate away; keep all entered field values (no data loss).
2. Show an **inline error banner** at the top of the form (reuse `.banner.banner-error`) with the §4.1
   message, and set `aria-invalid` + a `.field-error` under the **State** select naming it as the cause:
   > `This status already has the maximum number of tickets — finish existing ones first.`
3. Also surface the toast is **optional/redundant** here — prefer inline only, to keep the error anchored to
   the field that caused it. Move focus to the State select.
4. Banner is `role="alert"` (assertive) so it's announced; it clears when the user changes the State value.

**Why inline (not just a toast) for the form:** the user is mid-form with a specific offending field; an
inline message tied to **State** tells them exactly what to change, and a toast would auto-dismiss before
they finish reading/correcting. This matches the app's NFR-USE-3 "clear validation messages" expectation.

### 4.4 Pre-emptive guard (recommended)

To reduce hitting the wall: in the ticket form, when the selected **State** is already full for the team,
show a non-blocking `.field-hint` warning under the State select **before** submit:
> `This status is at its limit. Saving here may be rejected — pick another status or finish existing tickets first.`
This requires the form to know per-state fullness for the team (same unfiltered counts as §3.1). Mark
**recommended**; the mandatory behavior is the on-reject inline message (§4.3). Do **not** hard-disable the
option (the limit can change server-side; backend stays authoritative).

---

## 5. All states (form + board)

### 5.1 Limits configuration form (in `WipLimitsPanel`)

| State | Trigger | UI |
|---|---|---|
| **Default / populated** | Panel opened for a team with some limits set | Fields prefilled from `team.wipLimits`; empties show `No limit` placeholder. |
| **No-limit (per field)** | Field left blank | Placeholder `No limit`; hint `Blank = no limit`; saved as `null`/omitted. |
| **Valid value** | e.g. `3` | Field neutral; Save enabled (if all valid). |
| **Invalid value** | `0`, `-1`, `2.5`, `abc`, `> max` | `.field-error` with the §2.2 message; `aria-invalid`; Save disabled; focus first invalid on submit. |
| **Loading (open)** | Team data still loading | Panel shows the existing `.spinner` / `LoadingState` until `team` is available (teams already loaded on `/teams`, so usually instant). |
| **Saving** | Save clicked | Button `Saving…`, fields disabled, no double submit. |
| **Save success** | 200/2xx | Toast `WIP limits saved.`; panel collapses; board badges update on next board read (invalidate board queries for the team). |
| **Save error (validation)** | `400 validation_error` | Map field errors back onto the offending inputs (`.field-error`) using `ApiError.fieldErrors`; keep panel open, values preserved. |
| **Save error (other)** | network/5xx/404 | Inline `.banner.banner-error` with `errorMessage(err)`; keep values; allow retry (Save again). |
| **Conflict with current board** | New limit < current count in that column | Allowed to save (product says only *new* entries are blocked). After save, that column renders **over-limit** (§3.2) until the team drains it. Show an info note on success (recommended): `Some columns now exceed their new limit. New tickets can't be added there until the count drops.` |

### 5.2 Board column badge

| State | Condition | UI |
|---|---|---|
| **Loading** | Board query loading | Existing `LoadingState` ("Loading board…"); no badge until data. |
| **Unlimited** | `limit == null` | `N` (existing). |
| **Under** | `count < limit` | `N / max`, neutral. |
| **At limit (full)** | `count == limit` | Amber badge + lock icon + `N / max`; column `aria-label` ` , full`. |
| **Over** | `count > limit` | Red badge + warning icon + `N / max`; column `aria-label` `, over limit`. |
| **Empty column** | `count == 0`, limit set | `0 / max`, neutral; existing "No tickets" empty body unchanged. |
| **Drag over full column** | dragging + hover full col | Existing `.drop-active` + (recommended) warning tint + announcement suffix `(this column is full)`. |

---

## 6. Contract gaps to resolve with `architect` (blocking for implementation)

The current `API_CONTRACT.md` and `ARCHITECTURE.md` have **no** WIP-limit fields or a limit-rejection code.
This UX assumes the following; **these need to be added to the contract by `architect` before `developer`
builds it.** Flagged, not assumed silently.

1. **Read limits with the team.** Add `wipLimits` to the Team object (CONTRACT §4), e.g.:
   ```json
   "wipLimits": { "new": null, "ready_for_implementation": 5, "in_progress": 3,
                  "ready_for_acceptance": null, "done": null }
   ```
   (`null` = unlimited; integer ≥ 1 otherwise.) This lets `/teams` prefill the panel and the board read caps.

2. **Write limits.** A way to persist per-team limits, e.g. extend `PUT /api/teams/{id}` to accept an
   optional `wipLimits` map (validated server-side: integer ≥ 1 or null), or a dedicated
   `PUT /api/teams/{id}/wip-limits`. Backend re-validates (Source §6) and returns `400 validation_error`
   with per-field errors keyed by state for the inline mapping in §5.1.

3. **Unfiltered per-state totals for the badge (§3.1).** The board `count` is post-filter (A23). The badge's
   `N` and the full/over comparison must use the **unfiltered** per-state total for the team. Options for
   `architect`: include `wipLimit` and an unfiltered `total`/`limitCount` per column in the board response,
   or expose per-state totals separately. **Without this, `N / max` can be wrong under active filters.**

4. **Limit-rejection error on drag and on create/edit.** Define a stable error code + status so the SPA can
   recognize it and show the §4.1 message (rather than a generic fallback). Recommended:
   `409 wip_limit_reached` (a conflict with persisted state — consistent with the §2 taxonomy where 409 =
   conflict with current data; mirrors `team_has_children`). Returned by `PATCH /api/tickets/{id}/state`,
   `POST /api/tickets`, and `PUT /api/tickets/{id}` when the target state is full.
   - Add `wip_limit_reached` to `ApiErrorCode` (frontend `types.ts`) and a `FRIENDLY` mapping in
     `lib/errors.ts`:
     `wip_limit_reached: 'This status already has the maximum number of tickets — finish existing ones first.'`
   - This makes the existing drag rollback path (`useBoard.ts` `onError` → `toast.showError(errorMessage(err))`)
     show the correct text with **no change to the rollback logic**.

> If `architect` cannot provide the unfiltered count (gap 3) in time, fallback UX: render `N / max` only when
> **no filters are active**, and render plain `N` while filters are active (the limit signal is still correct
> on an unfiltered board, which is the common case). Document this fallback as a known limitation.

---

## 7. Accessibility (WCAG 2.2 AA) — how it's met

- **1.4.1 Use of Color:** full/over status conveyed by **icon + `/max` text + (for SR) column aria-label**,
  not color alone. (§3.2)
- **1.4.3 Contrast (text):** badge text uses existing `--warning-text` / `--error-text` on
  `--warning-bg` / `--error-bg` (already ≥ 4.5:1, same as banners). (§3.2)
- **1.4.11 Non-text Contrast:** the full/over badge has a `1px`/`2px` border in the warning/error text color
  giving ≥ 3:1 against the grey column. Focus rings reuse the app's existing
  `box-shadow: 0 0 0 2px rgba(0,82,204,.2)` + border, ≥ 3:1. (§2.2, §3.2)
- **2.1.1 Keyboard:** every limit input is a native field; Save/Cancel are native buttons; panel reachable in
  tab order; existing keyboard drag flow drives the §4.2 block path. No keyboard trap.
- **2.4.11 / 2.4.13 Focus (not obscured / appearance):** the expanded panel renders in document flow (a
  table row), so focused fields are not covered; focus styles reuse the existing visible focus.
- **2.5.8 Target Size:** numeric inputs use `.input` (≥ 32px height) and buttons use `.btn` — all ≥ 24×24px.
  This is **why** a +/- stepper was rejected (it would add sub-24px targets). (§2.2)
- **3.3.1 / 3.3.3 Error identification & suggestion:** the validation string says exactly what's wrong **and**
  what to do (`whole number of 1 or more, or leave blank`). (§2.2)
- **3.3.7 Redundant Entry:** panel prefills existing limits so the user never re-enters known values.
- **4.1.3 Status Messages:** block message via toast (`aria-live="assertive"`) for drag; via `role="alert"`
  banner for the form; success via the existing success toast. Announced without focus change.

---

## 8. Handoff

### 8.1 For `developer` (build order)
1. **Blocked on `architect`** for the four contract items in §6 (fields, write endpoint, unfiltered counts,
   `wip_limit_reached` code). Do not invent the shape — wait for the contract update.
2. `WipBadge` in `components/Badges.tsx` + CSS `.badge-count.is-full` / `.is-over` (§3.5). Swap
   `CountBadge` usage in `BoardColumn` for `WipBadge` and append full/over to the column `aria-label`.
3. `WipLimitsPanel` in `features/teams/` + wire a `WIP limits` row action into `TeamsPage` (mirror the
   inline-rename `editingId` pattern). Save mutation invalidates `['teams']` and `['board', teamId]`.
4. Add `wip_limit_reached` to `ApiErrorCode` + `FRIENDLY` map (§6.4). The drag rollback path then shows the
   correct toast with no other change.
5. Ticket form (`TicketPage`): on `wip_limit_reached`, show inline banner + State `.field-error`, keep
   values, focus State (§4.3); optional pre-emptive hint (§4.4).

**Required vs recommended:**
- **Required:** per-state numeric config with blank=unlimited; inline validation with the §2.2 text; explicit
  Save; `N / max` badge with full/over via color **and** icon **and** text/aria; drag rejection → rollback +
  §4.1 toast; create/edit rejection → inline §4.1 message with no data loss; all states in §5; a11y in §7.
- **Recommended:** drag-over-full tint + announcement suffix (§3.4); column-named message variant (§4.1);
  pre-emptive State hint (§4.4); over-limit info note on save (§5.1).

### 8.2 For `tester` (verifiable criteria)
- Setting `in_progress = 3` for a team and saving persists across reload; the column shows `N / 3`.
- Entering `0`, `-1`, `2.5`, `abc`, or `> max` in any limit field shows
  `Enter a whole number of 1 or more, or leave blank for no limit.` and Save is disabled.
- Clearing a field shows `No limit` placeholder; saving stores unlimited; badge shows plain `N`.
- A column at its cap shows the **full** badge (amber + lock icon + `N/max`) and the column `aria-label`
  contains `full`; over cap shows red + warning icon and `over limit`.
- Dragging a card into a full column: the card **returns** to its original column **and** a toast reads
  `This status already has the maximum number of tickets — finish existing ones first.`
- Creating/editing a ticket into a full state: the form does **not** navigate away, all entered values are
  preserved, and the same message appears inline near the State field.
- The full/over signal is distinguishable **without color** (icon + text + screen-reader aria-label).
- Entire config + Save + Cancel operable by keyboard only; visible focus throughout; toast/banner announced.
- (After contract gap 3) `N / max` numerator reflects the **unfiltered** column total even when a type/epic
  filter is active.

### 8.3 Open questions (with owners)
- **`architect`:** confirm the four contract items in §6 (esp. unfiltered per-state count and the
  `wip_limit_reached` code/status). Blocking.
- **`business-analyst`:** confirm the **max** limit ceiling (proposed 999) and whether the column-named
  message variant is desired. Non-blocking (defaults chosen).
- **`security-engineer`:** none expected — limits are not sensitive data; no PII, no auth-flow impact.
  (Flagging proactively that nothing here changes the auth/consent surface.)
```
