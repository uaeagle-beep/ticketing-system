# UX / UI Review — Wireframe & Minimum-Screen Conformance

> **Scope:** Static review (no Node/runtime) of the React+TS frontend in
> `frontend/src` against `docs/REQUIREMENTS_SOURCE.md` §10 (Minimum Screens) and
> §15 (Reference Wireframes 1–5), cross-checked with `docs/API_CONTRACT.md`.
> **Nature:** Authorized internal review of our own code (defensive). No code was changed.
> **Method:** Every finding cites file + line + the evidence fragment.
> **Reviewer role:** UX/UI designer. **Date:** 2026-06-30.

---

## 1. Wireframe → Status → Notes (summary table)

| # | Screen / Wireframe | Routing | Status | Notes |
|---|---|---|---|---|
| 1 | Kanban board (primary) | `/board` (`App.tsx:29`) | **Implemented** | Team selector, +New ticket, full filter bar, 5 UPPERCASE columns with count badges, cards with TYPE/title/epic/relative-time, DnD with optimistic rollback + error toast all present. Gaps: header brand reads "Ticket Tracker" not "TICKET TRACKER" via text (cosmetic, CSS uppercases it); no keyboard DnD. |
| 2 | Auth: Login | `/login` (`App.tsx:19`) | **Implemented** | Email, password, "Log in", "Account not verified? Resend email", "Create an account →" all present. |
| 2 | Auth: Sign up | `/signup` (`App.tsx:20`) | **Implemented** | Email, password (min-8 hint), confirm password, "Sign up", "Already registered? Log in →", "Email verification is required." note all present. |
| 2 | Auth: Verify email result | `/verify-email` (`App.tsx:24`) | **Implemented** | Verifying / success ("Continue to login") / error (with resend) / missing-token states all present. |
| 3 | Ticket details/edit + comments | `/tickets/new`, `/tickets/:id` (`App.tsx:30-31`) | **Partial** | All fields, meta line, Delete (confirm), Save, comments (oldest-first, count, add) present. **Deviation:** meta line shows raw UUID instead of a human ticket key like `TCK-1042`; "Created by" shows email, not display name (acceptable — no name field in API). |
| 4 | Team management | `/teams` (`App.tsx:32`) | **Implemented** | Table (Name/Tickets/Epics/Modified/Actions), inline create, inline rename, Delete disabled when `ticketCount>0 || epicCount>0` with explanatory title. Note text present. |
| 5 | Epic management | `/epics` (`App.tsx:33`) | **Implemented** | Team selector, +Create epic, table (Title/Tickets/Modified/Actions Edit/×), edit panel (Title + optional Description + Cancel/Save), Delete (×) disabled when referenced. |

**Overall:** All 8 minimum screens (§10) and all 5 wireframes (§15) exist and are
routable. Information hierarchy and primary flows match the wireframes closely.
The substantive gaps are accessibility (keyboard drag-and-drop, modal focus
management, color-only state cues) and a few cosmetic/semantic deviations.

---

## 2. Detailed conformance per screen

### Wireframe 1 — Kanban board (`features/board/`)
**Required elements — all present:**
- Header with brand + nav (Board/Teams/Epics) + collapsed user menu with email and Log out — `components/AppLayout.tsx:36-78`.
- Team selector dropdown — `BoardPage.tsx:149-160` (`aria-label="Select team"`, value persisted in URL `?team=`).
- `+ New ticket` — `BoardPage.tsx:163-171`.
- Filter bar: Search (title), Type, Epic, Clear, total count — `FilterBar.tsx:29-81`. AND logic via server query (`useBoard.ts:10-19`). Count is post-filter (`FilterBar.tsx:78-80`).
- Exactly 5 columns in workflow order with count badges — `BoardColumn.tsx:18-36`, ordering guaranteed by `emptyBoard`/`normalizeBoard` (`useBoard.ts:103-119`) and `TICKET_STATES` (`api/types.ts:8-15`). Headers forced UPPERCASE (`BoardColumn.tsx:25`).
- Card: TYPE badge, title, epic name, relative modified time — `TicketCard.tsx:43-53`, `Badges.tsx:5-7`.
- DnD between columns, immediate persist, optimistic with rollback + error toast — `BoardPage.tsx:95-116`, `useBoard.ts:59-99` (snapshot/restore in `onError`).
- Three distinct empty states — no teams (`BoardPage.tsx:131-143`), team-with-no-tickets (`BoardPage.tsx:200-218`), filtered-to-empty (`BoardPage.tsx:190-199`). Loading + error states present (`BoardPage.tsx:120-128, 183-186`).

**Gaps:** keyboard DnD missing (see A11y-1); column-move cards reachable only via pointer.

### Wireframe 2 — Authentication (`features/auth/`)
- Login (`LoginPage.tsx`): all elements present; unverified login surfaces inline resend (`:98-105`); 401/403 handled.
- Signup (`SignupPage.tsx`): min-8 client check (`:27-29`), confirm-password client-only and NOT sent (`:39`), success banner + "Continue to login →" (`:62-68`), "Email verification is required." note (`:127-129`).
- Verify (`VerifyEmailPage.tsx`): reads `?token=`, POSTs, StrictMode double-call guard (`:27,34-35`); success/error/missing all covered; resend action on error & missing.

### Wireframe 3 — Ticket details/edit + comments (`features/tickets/`)
- Back link, Delete (confirm dialog), Save — `TicketPage.tsx:213-225, 359-368`.
- Fields Team/Type/State/Epic/Title/Body with labels — `TicketPage.tsx:242-339`.
- Team change clears epic (FR-E4-5) — `handleTeamChange` (`:115-119`) + defensive clear effect (`:103-109`).
- Meta line id • created by • created • modified — `TicketPage.tsx:231-238`.
- Comments: count, oldest-first list, author+time+body, add textarea + Post — `CommentsPanel.tsx:43-90`; add does not invalidate the board (`:30`), matching V21.
- **Deviation (UX-3-KEY):** wireframe shows a human key `TCK-1042`; UI prints the raw UUID (`TicketPage.tsx:233`). See finding.

### Wireframe 4 — Team management (`features/teams/TeamsPage.tsx`)
- Table columns Name/Tickets/Epics/Modified/Actions — `:147-153`.
- Inline create (`:102-129`), inline rename (`:163-187`).
- **Delete disabled when referenced** — `:206-218` (`disabled={hasChildren}`, `hasChildren = team.ticketCount>0 || team.epicCount>0` at `:158`), with explanatory `title`. Matches wireframe.
- Note "All verified users can view and manage all teams." — `:100`.

### Wireframe 5 — Epic management (`features/epics/EpicsPage.tsx`)
- Team selector (`:145-159`), +Create epic (`:160-167`).
- Table Title/Tickets/Modified/Actions (Edit / ×) — `:245-298`.
- **Delete (×) disabled when referenced** — `:278-291` (`disabled={referenced}`, `referenced = epic.ticketCount>0` at `:256`), with `title` and `aria-label`. Matches wireframe.
- Edit panel Title + optional Description + Cancel/Save — `:181-228`.

---

## 3. State coverage (loading / empty / error / success)

| Screen | Loading | Empty | Error | Success |
|---|---|---|---|---|
| Board | ✔ `BoardPage.tsx:120,185` | ✔ 3 variants `:131,190,200` | ✔ `:124,183` (+retry) | ✔ board renders / move toast |
| Ticket | ✔ `:197` | n/a (form) | ✔ `:200,205` | ✔ create/save/delete toasts `:124,135,144` |
| Comments | ✔ `CommentsPanel.tsx:50` | ✔ `:54-55` | ✔ `:52-53` | ✔ list refresh |
| Teams | ✔ `TeamsPage.tsx:131` | ✔ `:135` | ✔ `:133` (+retry) | ✔ toasts |
| Epics | ✔ `EpicsPage.tsx:170,230` | ✔ `:174,234` | ✔ `:172,232` (+retry) | ✔ toasts |
| Auth | ✔ submit-pending labels; verify spinner | n/a | ✔ banners | ✔ banners/toasts |

State coverage is strong and matches NFR-USE-1.

---

## 4. Accessibility (WCAG 2.2 AA) — static findings

**Positives**
- `lang="en"` on `<html>` (`index.html:2`).
- All form inputs have associated `<label htmlFor>` (login/signup/resend/ticket/comment/epic). Filter-bar and team selectors that lack a visible label use `aria-label` (`FilterBar.tsx:33,40,57`; `BoardPage.tsx:151`; `EpicsPage.tsx:148`).
- Live regions: loaders `role="status" aria-live="polite"`, errors `role="alert"`, toasts `aria-live="assertive"` (`States.tsx:7,40`, `FullPageLoader.tsx:3`, `ToastContext.tsx:61`).
- Modal has `role="dialog" aria-modal="true" aria-label` and Escape-to-close (`ConfirmDialog.tsx:42-46, 28-35`).
- User menu uses `aria-haspopup`/`aria-expanded` and `role="menu"`/`menuitem` (`AppLayout.tsx:54-72`).
- Decorative avatar/spinner marked `aria-hidden` (`AppLayout.tsx:59`, `States.tsx:8`).
- Icon-only delete (×) has `aria-label` (`EpicsPage.tsx:288`).
- Cards are keyboard-operable for *open* (`tabIndex=0`, Enter/Space → navigate, `TicketCard.tsx:30-39`).

**Issues** (see findings A11Y-1 … A11Y-6 in §5) — the dominant one is that the
primary board interaction (moving a card between columns) is pointer-only.

---

## 5. Discrepancies — prioritized

### High

**A11Y-1 — Drag-and-drop is not keyboard accessible (WCAG 2.1.1 Keyboard, 2.5.7 Dragging Movements).**
`BoardPage.tsx:66-68` registers only `PointerSensor`; no `KeyboardSensor`/`KeyboardCoordinateGetter`. There is no non-pointer alternative to change a ticket's state from the board. (A keyboard alternative does exist on the ticket edit screen via the State dropdown, which partially mitigates 2.5.7, but the board itself fails 2.1.1.) Recommend adding dnd-kit `KeyboardSensor` and/or documenting the State-dropdown path as the equivalent.

**A11Y-2 — Modal does not trap or move focus, and is not focused on open (WCAG 2.4.3 Focus Order).**
`ConfirmDialog.tsx:39-64`: on open, focus stays on the triggering button; Tab can leave the dialog to the page behind it; nothing restores focus on close. Add initial focus to the dialog/confirm button, a focus trap, and focus restoration.

### Medium

**A11Y-3 — Card has `role="button"` but spreads drag `listeners`/`attributes`, creating conflicting/garbled semantics (WCAG 4.1.2 Name, Role, Value).**
`TicketCard.tsx:24-42`: the same element is a `role="button"` (with Enter/Space → navigate) *and* a dnd-kit draggable (Space is also dnd's activation key, and `aria-roledescription`/`aria-describedby` injected by dnd may collide with the button role). Space both scrolls/activates drag and triggers navigation (`:35`). Result is ambiguous to AT users. Recommend separating the drag handle from the open affordance, or using a real `<button>` and a dedicated handle.

**A11Y-4 — Type badge and column state are conveyed by color + text but several text contrasts are borderline/again color-only (WCAG 1.4.3 Contrast, 1.4.1 Use of Color).**
Type badges: `.type-fix` is `#00875a` on `#e3fcef` (`styles.css:239-242`) — small 11px bold text; success banner reuses `#00875a` on `#e3fcef` (`:204-207`). These are near the 4.5:1 threshold for small text and should be verified/darkened. Column header text uses `--text-muted #5e6c84` on `#ebecf0` (`styles.css:430,452`) — also borderline for 12px. Flagged as a risk to verify with a contrast tool (static review cannot run one). Badge meaning is carried by the label text too, so 1.4.1 is met; contrast (1.4.3) is the live risk.

**A11Y-5 — `role="menu"` semantics are incomplete (WCAG 4.1.2).**
`AppLayout.tsx:65-75`: a `role="menu"` containing a non-`menuitem` `<div>` (the email line) and a single `menuitem`, with no arrow-key navigation or focus management expected of the menu pattern. Either drop `role="menu"`/`menuitem` and treat it as a simple popover, or implement the full menu keyboard pattern.

**UX-3-KEY — Ticket meta shows raw UUID instead of a human key (Deviation from Wireframe 3).**
`TicketPage.tsx:233` renders `{detail.id}` (a UUID) where the wireframe specifies a readable key like `TCK-1042`. The API (`API_CONTRACT.md` §6) only exposes UUIDs, so this is a data-model limitation, not a bug, but it deviates from the wireframe's information hierarchy. Recommend a derived short display key or at least a "Copy ID" affordance; otherwise document as accepted deviation.

### Low

**A11Y-6 — Toast close button label and live-region politeness (minor).**
`ToastContext.tsx:61` uses `aria-live="assertive"` for *all* toasts including success/info; success/info should arguably be `polite` to avoid interrupting. Also the whole stack is one assertive region while each toast is `role="status"` (polite) — mixed politeness. Minor; consider splitting error (assertive/alert) vs success (polite/status).

**UX-DARK — `<meta name="color-scheme" content="light dark">` declared but no dark theme implemented.**
`index.html:6` advertises dark support, yet `styles.css` defines only light tokens (`:root`, no `prefers-color-scheme` block). On OS dark mode, form controls/scrollbars may render dark against the app's hard-coded light surfaces, hurting contrast. Either implement dark tokens or set `content="light"`.

**UX-1-BRAND — Brand text casing (cosmetic).**
Wireframe 1 labels the header `TICKET TRACKER`; the DOM text is "Ticket Tracker" (`AppLayout.tsx:37`), uppercased only via CSS `text-transform` (`styles.css:266`). No functional impact; noted for completeness. Confirm not relying on CSS for required casing in copy.

---

## 6. Conclusion

All required screens and wireframe elements are present, routable, and behave
per the requirements (DnD rollback, disabled-delete guards, three board empty
states, comment ordering, team-change-clears-epic). The implementation is in
good shape functionally. The highest-value remediation is **accessibility**:
add keyboard drag-and-drop (or document the State-dropdown equivalent) and give
the confirmation modal proper focus management. Contrast on small colored
badges/labels should be verified with a tool. Remaining items (ticket key,
dark-mode meta, menu semantics, toast politeness) are minor/cosmetic.
