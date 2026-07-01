# Feature Research & Product Roadmap — Kanban Ticketing System

> **Status:** Business-analysis research for product-owner decision & architect input.
> **Author role:** Business Analyst (delivery pipeline: BA → Architect → Developer → QA).
> **Product:** Kanban Ticketing System (.NET 10 API + React SPA + PostgreSQL), deployed at https://honcharenko.pp.ua.
> **Scope of this document:** propose and prioritize *new* features (roadmap). This is WHAT & WHY only — the HOW (technical design, schema, API shape) is deliberately left to the architect. Where a technical choice is unavoidable to describe a need, it is flagged **[на розгляд архітектора]**.
> **Date:** 2026-07-01.
> **Companion docs:** [`REQUIREMENTS_SOURCE.md`](./REQUIREMENTS_SOURCE.md) (canonical), [`REQUIREMENTS_ANALYSIS.md`](./REQUIREMENTS_ANALYSIS.md), [`ARCHITECTURE.md`](./ARCHITECTURE.md), [`API_CONTRACT.md`](./API_CONTRACT.md), [`USER_MANAGEMENT_DESIGN.md`](./USER_MANAGEMENT_DESIGN.md), [`WIP_LIMITS_UX.md`](./WIP_LIMITS_UX.md), [`TEST_REPORT.md`](./TEST_REPORT.md), [`SECURITY_REVIEW_USER_MGMT.md`](./SECURITY_REVIEW_USER_MGMT.md).

---

## 1. Executive summary

The product has moved well beyond its original hackathon mandate. On top of the mandatory scope (auth + email verification, teams, epics, tickets, 5-state Kanban, comments, drag-and-drop, filters/search) it already ships **User Management with a real authorization model** (admin/member roles, team membership, blocking, admin user CRUD, admin password reset, display Name, user filtering) and **per-team/per-state WIP limits**. Quality is strong: 234 backend + 168 frontend tests + E2E, all green; a security review of the authorization model returned a **GO** with no High/Critical findings.

That maturity changes the roadmap question. The base "work item + board" is solid, so the highest-value next investments are the **work-item richness** and **awareness/communication** capabilities that every mature issue tracker (Jira, Linear, Trello, GitHub Issues) treats as table stakes — and which are conspicuously *absent* here today: **assignee, priority, due date, labels, notifications, and activity history**. These were explicitly declared "out of scope" for the hackathon (REQUIREMENTS_SOURCE §12), but the product's trajectory (roles, membership, WIP limits — themselves originally out of scope) shows the owner is intentionally growing this into a usable team tool. Returning selected §12 items to scope is now the single biggest lever on user value.

Alongside feature growth there is a small, concrete set of **product/technical-debt gaps** worth closing because they are user-visible today: **no self-service password reset** (only an admin can reset — a real operational bottleneck and a login dead-end), **no self-service profile edit** (a user cannot set their own display Name), **no default team auto-provisioning** (self-signup silently lands a user with no workspace if "Demo Team" is missing), **no audit log** for privileged admin actions (flagged SEC-3), and **email deliverability hardening** (SPF/DKIM/DMARC) since verification email is the first-run critical path.

**Prioritization method:** RICE (Reach × Impact × Confidence ÷ Effort), applied uniformly (§4). Top recommendations for the next release (§6): **self-service password reset, ticket assignee, ticket priority, in-app notifications, activity history, labels/tags, self-service profile edit, and saved board views** — sequenced so that foundational data (assignee) precedes the features that depend on it (notifications).

---

## 2. Method & sources

### 2.1 What "already exists" (baseline — do NOT propose these)

Confirmed by reading the design docs **and** the actual code (`backend/src`, `frontend/src`) and test report:

| Area | Shipped capability |
|---|---|
| Auth | Email+password signup, Argon2id, email verification (24h single-use token, resend), login/logout, bearer sessions |
| Authorization | `admin` / `member` roles; per-team membership; server-side per-resource enforcement (IDOR-safe); blocking (hard denial + session purge); last-admin guard |
| User Management (admin) | List/filter users; create pre-verified user; set role; set team membership; block/unblock; **admin** reset-password (shown once); derived status (active/unverified/blocked) |
| Profile | Optional display **Name** (`name || email` fallback) — but **set only by an admin**, not by the user themselves |
| Teams | CRUD, case-insensitive uniqueness, delete-guard (409 with children); member list-filtering |
| Epics | CRUD per team, immutable team, delete-guard (409 when referenced) |
| Tickets | Fields = team, epic (optional, same-team), type (bug/feature/fix), state (5), title, body, created/modified timestamps, created-by. CRUD, enum/reference validation, correct `modified_at` semantics |
| Board | 5-column Kanban per team, drag-and-drop with optimistic move + rollback, sort by modified desc, filter by type/epic + title substring search (AND), count badges, keyboard DnD |
| WIP limits | Per-team per-state caps; board badges (under/full/over) with color+icon+text; `409 wip_limit_reached` on create/edit/drag into a full state |
| Comments | Add + list oldest-first, non-empty, immutable, no `modified_at` bump; shows `authorName` |
| NFR/platform | Docker Compose one-command start; nginx SPA + `/api` proxy; SSL on prod; migrations on startup; health endpoints; strong automated test coverage |

### 2.2 Confirmed gaps (present in mature trackers, absent here)

From code inspection: ticket has **no assignee, no priority, no due date, no labels/tags, no attachments**; there is **no notification system**, **no activity/audit history** (user-facing or admin), **no @mentions / watchers**, **no real-time board updates**, **no saved filters/views**, **no bulk actions**, **no reporting/analytics**, **no API keys/webhooks**, **no self-service password reset or profile edit**, **no dark mode / i18n**. Board filtering is currently server-or-client but there is no persisted per-user view.

### 2.3 Industry grounding

Market research on Jira / Linear / Trello / GitHub Issues consistently treats **assignee, priority, labels, due dates, notifications, and activity history** as foundational, non-negotiable capabilities of an issue tracker; saved views, bulk actions, and automation are the next tier; AI summaries and deep configurability are differentiators layered on top. Email-authentication (SPF/DKIM/DMARC) is now effectively mandatory for reliable transactional-email delivery. Sources are listed in §11.

### 2.4 Prioritization framework — RICE

For each feature: **Reach** (users/events affected per quarter, relative 1–10 for this internal-tool scale), **Impact** (0.25 minimal / 0.5 low / 1 medium / 2 high / 3 massive), **Confidence** (50/80/100 %), **Effort** (person-scale S=1 / M=2 / L=4 — *relative sizing from a value standpoint, NOT an engineering estimate*; the architect/dev own true effort). **RICE = (R × I × C) ÷ E.** Higher = do sooner. Effort letter is shown alongside for readability.

---

## 3. Themes

- **T1 — User productivity** (richer work item & board): assignee, priority, due date, labels, saved views, bulk actions, ticket search over body, subtasks/checklist.
- **T2 — Communication & awareness**: in-app notifications, email notifications, @mentions, watchers, activity history, comment edit/delete, comment reactions.
- **T3 — Administration & account self-service**: self-service password reset, self-service profile edit, default team auto-provisioning, admin audit log, user delete/soft-delete.
- **T4 — Analytics & reporting**: board/flow metrics (cycle time, throughput), per-user workload, CSV export.
- **T5 — Integrations & extensibility**: REST API keys, webhooks, real-time board updates.
- **T6 — Quality / non-functional**: board performance at 100+ tickets (virtualization), email deliverability (SPF/DKIM/DMARC), observability, i18n (uk/en), dark mode & accessibility, last-admin TOCTOU fix (SEC-1).

---

## 4. Prioritization table (RICE)

> Effort is a *value-relative* size (S/M/L), not an engineering estimate. Sorted by RICE score (desc). IDs are stable for traceability.

| ID | Feature | Theme | Reach | Impact | Conf. | Effort | RICE | Tier |
|---|---|---|---:|---:|---:|---|---:|---|
| F-01 | Self-service password reset (forgot password) | T3 | 8 | 2 | 100% | S (1) | **16.0** | Quick win / Top |
| F-02 | Ticket assignee | T1 | 9 | 2 | 100% | M (2) | **9.0** | Top |
| F-03 | Ticket priority | T1 | 9 | 1 | 100% | S (1) | **9.0** | Quick win / Top |
| F-04 | Self-service profile edit (own Name/password) | T3 | 8 | 1 | 100% | S (1) | **8.0** | Quick win / Top |
| F-05 | Labels / tags on tickets | T1 | 8 | 1 | 90% | M (2) | **3.6** | Top |
| F-06 | In-app notifications | T2 | 8 | 2 | 80% | L (4) | **3.2** | Top |
| F-07 | Activity history (ticket timeline) | T2 | 8 | 1 | 90% | M (2) | **3.6** | Top |
| F-08 | Due date + overdue indicator | T1 | 7 | 1 | 90% | S (1) | **6.3** | Quick win / Top |
| F-09 | Saved board views / filters | T1 | 6 | 1 | 80% | M (2) | **2.4** | Top |
| F-10 | Default team auto-provisioning on signup | T3 | 6 | 1 | 100% | S (1) | **6.0** | Quick win |
| F-11 | Admin audit log (privileged actions) | T3 | 4 | 1 | 90% | M (2) | **1.8** | Strategic/debt |
| F-12 | Comment edit / delete (own) | T2 | 6 | 0.5 | 90% | S (1) | **2.7** | Quick win |
| F-13 | Email notifications (assignment/mention/comment) | T2 | 7 | 1 | 70% | L (4) | **1.2** | Strategic |
| F-14 | @mentions in comments | T2 | 6 | 1 | 80% | M (2) | **2.4** | Strategic (after F-06) |
| F-15 | Watchers / subscribe to ticket | T2 | 5 | 0.5 | 80% | M (2) | **1.0** | Strategic |
| F-16 | Bulk actions on the board | T1 | 5 | 1 | 70% | M (2) | **1.75** | Strategic |
| F-17 | Search over ticket body (not just title) | T1 | 7 | 0.5 | 90% | S (1) | **3.15** | Quick win |
| F-18 | Reporting / flow analytics (cycle time, throughput) | T4 | 4 | 1 | 70% | L (4) | **0.7** | Strategic |
| F-19 | CSV export of tickets | T4 | 4 | 0.5 | 90% | S (1) | **1.8** | Quick win |
| F-20 | Real-time board updates (live sync) | T5 | 6 | 1 | 60% | L (4) | **0.9** | Strategic |
| F-21 | REST API keys + webhooks | T5 | 3 | 1 | 60% | L (4) | **0.45** | Strategic |
| F-22 | Board virtualization (100+ tickets perf) | T6 | 5 | 0.5 | 80% | M (2) | **1.0** | Debt (conditional) |
| F-23 | Email deliverability hardening (SPF/DKIM/DMARC) | T6 | 9 | 1 | 90% | S (1) | **8.1** | Quick win / debt |
| F-24 | Last-admin TOCTOU fix (SEC-1) | T6 | 3 | 2 | 100% | S (1) | **6.0** | Debt (do early) |
| F-25 | i18n (Ukrainian / English) | T6 | 4 | 0.5 | 80% | L (4) | **0.4** | Strategic |
| F-26 | Dark mode | T6 | 5 | 0.25 | 90% | M (2) | **0.56** | Later |
| F-27 | Subtasks / checklist on a ticket | T1 | 5 | 1 | 70% | L (4) | **0.875** | Strategic |
| F-28 | File attachments on tickets/comments | T1 | 6 | 1 | 70% | L (4) | **1.05** | Strategic |

**Reading the scores:** RICE rewards low-effort, high-reach items. The mathematically top-ranked are F-01, F-02/F-03, F-23, F-04, F-08, F-10, F-24 — most are Small. Larger strategic bets (notifications, analytics, real-time, integrations) score lower on RICE by design (high effort) but may still be chosen for *strategic* reasons; they are grouped in the "later" waves in §6.

---

## 5. Theme detail — every proposed feature

Format per feature: **Problem/Need (WHY) → User story → Business value → Complexity (S/M/L) → Dependencies/Risks**. Technical realization is intentionally omitted (architect's domain).

### T1 — User productivity

#### F-02 — Ticket assignee
- **Need:** Today a ticket records only *created-by*. There is no way to say *who is responsible* for doing the work. On a shared team board this is the number-one missing signal; "whose card is this?" is unanswerable, and WIP limits per column can't be complemented by per-person focus.
- **User story:** As a team member, I want to assign a ticket to a specific user (or myself), so that ownership is explicit and everyone knows who is working on what.
- **Business value:** Accountability and coordination — the core reason teams adopt a tracker over a spreadsheet. Prerequisite for "my work" filters, workload analytics, and assignment notifications.
- **Complexity:** M. New optional field on ticket referencing a user; must respect team membership (assignee should be a member of the ticket's team — **[на розгляд архітектора]** whether admins are assignable). Board card + filter + detail changes.
- **Dependencies/Risks:** Enables F-06/F-13 (notify on assignment), F-09 ("assigned to me" view), F-18 (workload). Risk: reassigning across a team change must keep the same-team-membership invariant consistent.

#### F-03 — Ticket priority
- **Need:** All tickets look equal; there is no way to express urgency/importance. Teams cannot triage.
- **User story:** As a team member, I want to set a priority (e.g. Low / Medium / High / Urgent) on a ticket, so that the team can decide what to pick up next.
- **Business value:** Better triage and focus; feeds sorting/filtering and future SLA/analytics. Very low cost, high everyday utility.
- **Complexity:** S. A small fixed enum field (values are a product decision — **[на розгляд архітектора]** exact set), plus card badge, filter, and sort option.
- **Dependencies/Risks:** Independent. Mild: interacts with default board sort (currently modified-desc) — decide whether priority becomes a secondary sort. Keep enum fixed (mirrors type/state pattern) to avoid custom-field scope creep.

#### F-05 — Labels / tags on tickets
- **Need:** Type is a fixed 3-value classification; there is no flexible, team-defined categorization (e.g. `frontend`, `tech-debt`, `customer-X`). Teams need lightweight cross-cutting tags.
- **User story:** As a team member, I want to attach one or more labels to a ticket and filter the board by label, so that I can slice work by cross-cutting concerns my team cares about.
- **Business value:** Flexible organization without custom workflows; a top-used Trello/Jira capability. Strong filtering payoff.
- **Complexity:** M. Many-to-many ticket↔label; label management scope (per-team vs global) is a product decision — **[на розгляд архітектора]**. Board filter + card chips.
- **Dependencies/Risks:** Governance risk (label sprawl) — consider per-team ownership and an admin/member permission for label CRUD. Combines with F-09 (saved views) and F-17 (search).

#### F-08 — Due date + overdue indicator
- **Need:** No time dimension on tickets; nothing signals lateness. Teams working to deadlines have no cue.
- **User story:** As a team member, I want to set a due date on a ticket and see overdue tickets highlighted, so that time-sensitive work is not missed.
- **Business value:** Deadline awareness; feeds notifications ("due soon") and analytics later.
- **Complexity:** S. Optional date field + card indicator + optional "overdue" board filter. Time-zone display is a UX detail (relative-time infra already exists).
- **Dependencies/Risks:** Low. "Due soon" reminders depend on F-06/F-13. Clarify whether due date is date-only or datetime, and time-zone handling — **[відкрите питання до власника продукту]**.

#### F-09 — Saved board views / filters
- **Need:** Filters reset every visit; a user re-applies the same type/epic/label/assignee filters constantly. Power users want reusable views ("My open bugs", "This epic, high priority").
- **User story:** As a frequent user, I want to save a named combination of filters (and reopen it in one click), so that I don't reconfigure the board every time.
- **Business value:** Daily time savings for heavy users; increases stickiness. A standard "views" feature in Linear/Jira.
- **Complexity:** M. Per-user saved views; likely server-persisted (localStorage is not the system of record per §9). Depends on which filter dimensions exist.
- **Dependencies/Risks:** Best done *after* assignee/priority/labels so views can filter on them; otherwise value is limited.

#### F-16 — Bulk actions on the board
- **Need:** Changing state/assignee/label on many tickets is one-at-a-time. Grooming a backlog is tedious.
- **User story:** As a team member, I want to select multiple tickets and change their state/assignee/label/priority at once, so that grooming is fast.
- **Business value:** Efficiency for larger boards; reduces friction that pushes teams back to spreadsheets.
- **Complexity:** M. Multi-select UX + batched operations; must respect WIP limits and per-resource authorization for each ticket.
- **Dependencies/Risks:** Depends on the fields it edits (F-02/F-03/F-05). Partial-failure semantics need a clear rule (**[на розгляд архітектора]**).

#### F-17 — Search over ticket body (not just title)
- **Need:** Search matches title substring only (A24). Users expect to find tickets by words in the description.
- **User story:** As a team member, I want board search to also match the ticket body, so that I can find tickets by content, not just title.
- **Business value:** Findability; low cost, frequent benefit at scale.
- **Complexity:** S. Extend existing search (already server-side capable). At larger scale full-text indexing is an architect decision — **[на розгляд архітектора]**.
- **Dependencies/Risks:** Low. Confirm expected behavior with active filters (AND) stays consistent.

#### F-27 — Subtasks / checklist on a ticket
- **Need:** No way to break a ticket into steps; teams track sub-work in the body as freehand text.
- **User story:** As a team member, I want a checklist (or subtasks) on a ticket, so that I can track progress within a single work item.
- **Business value:** Better granularity without inventing a full hierarchy. (Full subtask *dependencies* were explicitly out of scope §12 — a lightweight checklist is the pragmatic middle ground.)
- **Complexity:** L (subtasks as entities) or M (simple checklist). Recommend starting with a checklist.
- **Dependencies/Risks:** Scope-creep risk toward full hierarchy; keep minimal. §12 called out subtasks/dependencies as out of scope — returning even a checklist needs owner sign-off.

#### F-28 — File attachments on tickets/comments
- **Need:** No way to attach screenshots/logs/specs; §12 excluded attachments. For bug tracking, screenshots are highly valuable.
- **User story:** As a team member, I want to attach files (e.g. a screenshot) to a ticket or comment, so that context lives with the work item.
- **Business value:** Especially high for `bug` tickets. A common expectation.
- **Complexity:** L. Requires storage strategy, size/type limits, security scanning, and access control aligned with team scope — significant new surface (**[на розгляд архітектора]**, also **[на розгляд security-engineer]**).
- **Dependencies/Risks:** Storage cost/ops; malware/upload-abuse risk; must inherit team-scoped authorization. Highest-effort T1 item.

### T2 — Communication & awareness

#### F-06 — In-app notifications
- **Need:** Nothing tells a user something happened. If a ticket is assigned to them, commented on, or moved, they only find out by manually re-checking the board. This is the biggest awareness gap.
- **User story:** As a user, I want to see in-app notifications (a bell with unread count) for events relevant to me — assigned to me, commented on my ticket, mention — so that I can respond without polling the board.
- **Business value:** Drives engagement and responsiveness; turns a passive board into an active workflow. Foundational for retention.
- **Complexity:** L. Notification model + generation on events + unread/read + a UI surface. Delivery mechanism (poll vs push) is an architect decision — **[на розгляд архітектора]**.
- **Dependencies/Risks:** Depends on F-02 (assignment) and benefits from F-14 (mentions). Risk: notification noise — needs sensible defaults and later per-user preferences.

#### F-07 — Activity history (ticket timeline)
- **Need:** §12 excluded audit history. A ticket shows current values only; you cannot see *what changed, when, and by whom* (state moves, reassignments, field edits). This is essential for accountability and debugging "who moved this to Done?".
- **User story:** As a team member, I want to see a chronological activity log on a ticket (state changes, field edits, assignment changes), so that I understand its history.
- **Business value:** Transparency and trust; supports retrospectives and dispute resolution. A universally expected tracker feature.
- **Complexity:** M. Append-only event capture on ticket mutations + a timeline UI. Retention policy is a product decision.
- **Dependencies/Risks:** Interacts with `modified_at` semantics (already precise). Overlaps conceptually with the admin audit log (F-11) — consider a shared event backbone (**[на розгляд архітектора]**).

#### F-12 — Comment edit / delete (own)
- **Need:** Comments are immutable (§7 mandatory scope; §14 named edit/delete as a stretch). Typos and mistakes are permanent; users expect to fix their own comments.
- **User story:** As a comment author, I want to edit or delete my own comment, so that I can correct mistakes.
- **Business value:** Everyday usability; directly listed as an intended stretch feature (§14).
- **Complexity:** S. Add author-scoped edit/delete; decide whether to show "edited" marker and keep an edit trail (ties to F-07).
- **Dependencies/Risks:** Authorization: only the author (or admin) may edit/delete — **[на розгляд архітектора]**. Must not alter ticket `modified_at` (consistency with V21).

#### F-13 — Email notifications (assignment / mention / comment)
- **Need:** In-app notifications (F-06) only reach active users. Email reaches users who are away — critical for assignment and mentions.
- **User story:** As a user, I want to optionally receive email for high-signal events (assigned to me, mentioned), so that I'm reachable when not in the app.
- **Business value:** Reach beyond active sessions; standard for trackers.
- **Complexity:** L. Reuses existing SMTP but adds templating, per-user preferences, unsubscribe, and digest/anti-spam considerations. Deliverability (F-23) becomes important.
- **Dependencies/Risks:** Depends on F-06 (event model) and F-23 (deliverability). Risk: email fatigue → needs preferences and batching.

#### F-14 — @mentions in comments
- **Need:** No way to pull a specific person into a discussion; users resort to out-of-band pings.
- **User story:** As a commenter, I want to @mention a teammate, so that they are notified and drawn into the conversation.
- **Business value:** Collaboration; a high-signal notification trigger.
- **Complexity:** M. Mention parsing + user picker scoped to team + notification hook.
- **Dependencies/Risks:** Depends on F-06 (and ideally F-13). Scope the mention candidate list to team members (authorization).

#### F-15 — Watchers / subscribe to a ticket
- **Need:** Only the assignee/author naturally track a ticket; interested others cannot follow updates.
- **User story:** As a user, I want to "watch" a ticket, so that I receive its notifications even if I'm not the assignee.
- **Business value:** Keeps stakeholders informed without assigning them.
- **Complexity:** M. Watch relationship + notification fan-out.
- **Dependencies/Risks:** Depends on F-06. Fan-out volume interacts with notification-noise controls.

### T3 — Administration & account self-service

#### F-01 — Self-service password reset ("forgot password")
- **Need:** **Concrete gap today.** There is no self-service reset — only an *admin* can reset a password (`POST /api/admin/users/{id}/reset-password`). A user who forgets their password is stuck until an admin intervenes; §14 explicitly listed "password reset flow" as an intended stretch feature. This is a login dead-end and a support burden.
- **User story:** As a user who forgot my password, I want to request a reset link by email and set a new password, so that I can regain access without contacting an admin.
- **Business value:** Removes the single most common account-recovery blocker; cuts admin toil; expected on every login screen. Highest RICE score.
- **Complexity:** S. Reuses the existing email + single-use, time-limited token machinery already built for verification (same patterns: hashed token, expiry, single-use, anti-enumeration responses).
- **Dependencies/Risks:** Must honor the existing anti-enumeration posture (non-committal responses) and block-state rules (a blocked user must not reset — mirror admin-reset behavior). **[на розгляд security-engineer]** for token lifetime and rate-limiting. Strong reuse of existing code lowers risk.

#### F-04 — Self-service profile edit (own Name / change password)
- **Need:** **Concrete gap today.** The display Name exists but can be set **only by an admin** (TEST_REPORT explicitly lists "User self-service profile ... out of scope, not implemented"). A user cannot set their own name or change their own password while logged in.
- **User story:** As a logged-in user, I want to edit my own display Name and change my own password, so that I manage my profile without an admin.
- **Business value:** Basic self-service expected of any authenticated app; reduces admin toil; improves the human-readable board (names instead of emails).
- **Complexity:** S. A "My profile" screen; name-set logic already exists server-side (currently admin-gated) and password-change reuses hashing. Change-password should require current password — **[на розгляд security-engineer]**.
- **Dependencies/Risks:** Ensure a member cannot escalate role/teams via a self-endpoint (the design already isolates privilege fields to admin endpoints — keep that invariant).

#### F-10 — Default team auto-provisioning on signup
- **Need:** **Concrete gap today.** After email verification a self-registered user joins the team named by `DEFAULT_SIGNUP_TEAM_NAME` (default "Demo Team") — but the migration deliberately does **not** create that team. If it's absent, the user verifies successfully yet lands with **no team and no workspace** (only a warning log). First-run experience silently breaks.
- **User story:** As a newly verified user, I want to automatically have a usable workspace (a team) after signup, so that I can start immediately instead of seeing an empty app.
- **Business value:** Protects the critical first-run/activation path; avoids "I signed up and there's nothing here" churn and support tickets.
- **Complexity:** S. Options (architect's choice — **[на розгляд архітектора]**): ensure the default team exists (safe idempotent provisioning at startup/first signup), or add an onboarding step, or clearer empty-state guidance. Note the "fresh DB = schema only" constraint (V28) must be respected — auto-creating a team at *runtime* on first signup respects it; seeding it in a *migration* would not.
- **Dependencies/Risks:** Must not violate the no-seed-data rule (§9/V28). Interacts with member-visibility rules.

#### F-11 — Admin audit log (privileged actions)
- **Need:** Security review **SEC-3 (Low)**: privileged admin actions (create/block/demote/reset) leave no audit trail; insider misuse or a compromised admin session is not attributable after the fact.
- **User story:** As an admin/owner, I want an audit log of privileged user-management actions (who did what to whom, when), so that I can investigate and demonstrate accountability.
- **Business value:** Security, compliance-readiness, and trust for an internal tool with real RBAC.
- **Complexity:** M. Structured, append-only audit events (never logging secrets/passwords) + an admin view.
- **Dependencies/Risks:** Could share the event backbone with F-07 (ticket activity). Keep secrets out of logs (SEC-4/SEC-6 discipline).

### T4 — Analytics & reporting

#### F-18 — Reporting / flow analytics (cycle time, throughput)
- **Need:** §12 excluded reporting dashboards. With WIP limits already in place, teams have no way to see whether flow is actually improving (bottlenecks, cycle time, throughput).
- **User story:** As a team lead, I want basic flow metrics (tickets completed over time, average time-in-state/cycle time, current WIP), so that I can improve how the team works.
- **Business value:** Turns the board into a management instrument; complements WIP limits (the metrics that justify them).
- **Complexity:** L. Requires historical state-transition data — strongly benefits from F-07 (activity history) as the data source. Charting UI.
- **Dependencies/Risks:** Depends on F-07 for accurate historical data; without it, only crude point-in-time metrics are possible.

#### F-19 — CSV export of tickets
- **Need:** No way to get data out for offline reporting or sharing with non-users.
- **User story:** As a user, I want to export the current (filtered) board/ticket list to CSV, so that I can report or analyze outside the app.
- **Business value:** Low-cost interoperability; unblocks ad-hoc reporting before F-18 exists.
- **Complexity:** S. Export the current filtered set. Respect team-scoped authorization.
- **Dependencies/Risks:** Low. Decide which fields/columns to include.

### T5 — Integrations & extensibility

#### F-20 — Real-time board updates (live sync)
- **Need:** §12 excluded real-time multi-user updates. Two people on the same board don't see each other's moves without a refresh — confusing and can cause stale drags.
- **User story:** As a team member, I want the board to update live when teammates change tickets, so that I always see the current state.
- **Business value:** Multiplayer feel; reduces conflicts and confusion on shared boards.
- **Complexity:** L. Real-time transport + client reconciliation with optimistic updates (**[на розгляд архітектора]**).
- **Dependencies/Risks:** Interacts with last-write-wins (§9) and optimistic DnD. Higher infra/ops complexity.

#### F-21 — REST API keys + webhooks
- **Need:** No programmatic access for automation or integration with CI, chat, etc.
- **User story:** As an admin/power user, I want to issue API keys and register webhooks, so that I can integrate the tracker with other tools.
- **Business value:** Extensibility and stickiness; enables ecosystem/automation.
- **Complexity:** L. Key lifecycle, scoping, secret handling, webhook delivery/retries (**[на розгляд security-engineer]** + architect).
- **Dependencies/Risks:** New auth surface and secret management; significant security review needed. Lower reach for an internal tool.

### T6 — Quality / non-functional (technical debt & platform)

#### F-23 — Email deliverability hardening (SPF / DKIM / DMARC)
- **Need:** Verification email is the **first-run critical path** — if it lands in spam or is rejected, the user can never activate. Modern mailbox providers (Gmail/Yahoo/Microsoft) increasingly require authenticated mail; unauthenticated transactional mail is unreliable.
- **User story:** As a new user, I want the verification (and future notification) emails to reliably reach my inbox, so that I can activate my account.
- **Business value:** Directly protects activation and every future email feature (F-13). High reach, low effort.
- **Complexity:** S (mostly DNS/config + verifying the sending domain), though it's an ops/infra task — **[на розгляд архітектора/DevOps]**.
- **Dependencies/Risks:** Prerequisite quality gate for F-13. Requires control of the sending domain's DNS.

#### F-24 — Last-admin TOCTOU fix (SEC-1)
- **Need:** Security review **SEC-1 (Medium)**: the last-admin guard's count-then-mutate is not atomic on the demote path; two concurrent privileged requests could leave the system with **zero usable admins** (self-lockout / availability).
- **User story:** As the system, I want the last-admin guard to be race-safe, so that concurrent operations can never remove the final administrator.
- **Business value:** Prevents a catastrophic self-lockout of the admin zone. Low effort, high protective value; a concurrency test already exists for part of this.
- **Complexity:** S. Make guard-then-mutate atomic/serialized (architect's fix per SEC-1/SEC-2). Purely internal.
- **Dependencies/Risks:** None user-facing; low risk. Recommend doing it early regardless of feature roadmap.

#### F-22 — Board virtualization (100+ tickets performance)
- **Need:** NFR requires the board to remain usable at 100+ tickets (§8); virtualization was a named stretch (§14). Performance is currently **not automated** (TEST_REPORT gap). As boards grow, render cost may degrade DnD/scroll.
- **User story:** As a user of a large board, I want it to stay responsive with hundreds of tickets, so that filtering, scrolling, and dragging remain smooth.
- **Complexity:** M. Virtualized rendering + confirm DnD/keyboard-DnD compatibility.
- **Dependencies/Risks:** **Conditional** — validate with a performance measurement first; only invest if a real threshold is crossed. Interacts with DnD library.

#### F-25 — i18n (Ukrainian / English)
- **Need:** UI is English-only (A7). A Ukrainian-speaking user base may prefer localized UI.
- **User story:** As a user, I want to use the app in Ukrainian or English, so that it fits my language preference.
- **Business value:** Accessibility to a broader/local user base.
- **Complexity:** L. Externalize all strings + locale switch + date/number formatting; ongoing translation upkeep.
- **Dependencies/Risks:** Large surface touch; best done before the string count grows further. Confirm demand — **[відкрите питання до власника продукту]**.

#### F-26 — Dark mode
- **Need:** No theme choice; a frequently requested comfort feature.
- **User story:** As a user, I want a dark theme, so that the app is comfortable in low light.
- **Business value:** Satisfaction/comfort; low functional impact.
- **Complexity:** M. Theming tokens (the CSS already uses variables, which helps).
- **Dependencies/Risks:** Low. Ensure WCAG contrast in both themes.

---

## 6. Recommended roadmap (3 waves)

Sequencing respects **dependencies** (assignee before assignment-notifications; activity history before analytics; deliverability before email notifications) and front-loads **quick wins** and the one **Medium security fix**.

### Wave 1 — "Close the gaps + table stakes" (next release)
Goal: fix user-visible product/technical-debt gaps and add the foundational work-item fields every tracker needs. Mostly Small/Medium, high RICE.

| Order | ID | Feature | Size | Why now |
|---|---|---|---|---|
| 1 | F-24 | Last-admin TOCTOU fix (SEC-1) | S | Security (Medium); prevents admin-zone self-lockout; do first. |
| 2 | F-01 | Self-service password reset | S | Highest RICE; removes login dead-end; reuses token infra; §14 intended. |
| 3 | F-23 | Email deliverability (SPF/DKIM/DMARC) | S | Protects activation + all future email; prerequisite for F-13. |
| 4 | F-04 | Self-service profile edit (name/password) | S | Basic self-service; reduces admin toil; humanizes the board. |
| 5 | F-10 | Default team auto-provisioning | S | Fixes silent broken first-run; protects activation. |
| 6 | F-03 | Ticket priority | S | Cheap, high daily utility; triage. |
| 7 | F-02 | Ticket assignee | M | Foundational; unlocks Wave 2 (notifications, "my work", workload). |
| 8 | F-08 | Due date + overdue indicator | S | Time dimension; feeds later reminders. |

**Wave 1 top-8 recommendation** (the "5–8 for the next release" ask): F-24, F-01, F-23, F-04, F-10, F-03, F-02, F-08.

### Wave 2 — "Awareness & collaboration"
Goal: make the board active, not passive. Depends on Wave 1 (assignee).

| Order | ID | Feature | Size |
|---|---|---|---|
| 1 | F-07 | Activity history (ticket timeline) | M |
| 2 | F-06 | In-app notifications | L |
| 3 | F-12 | Comment edit/delete (own) | S |
| 4 | F-05 | Labels / tags | M |
| 5 | F-17 | Search over body | S |
| 6 | F-09 | Saved board views | M |
| 7 | F-14 | @mentions (after F-06) | M |
| 8 | F-11 | Admin audit log | M |

### Wave 3 — "Scale, reach & insight" (strategic / large)
Goal: broaden reach and add management value. Larger bets, chosen strategically.

| ID | Feature | Size |
|---|---|---|
| F-13 | Email notifications | L |
| F-18 | Reporting / flow analytics | L |
| F-15 | Watchers | M |
| F-16 | Bulk actions | M |
| F-19 | CSV export | S |
| F-20 | Real-time board updates | L |
| F-28 | File attachments | L |
| F-27 | Subtasks / checklist | L/M |
| F-21 | API keys + webhooks | L |
| F-22 | Board virtualization | M (conditional on perf data) |
| F-25 | i18n (uk/en) | L |
| F-26 | Dark mode | M |

### Quick wins (high value ÷ low effort — pull forward opportunistically)
F-01 (password reset), F-03 (priority), F-04 (profile edit), F-08 (due date), F-10 (default team), F-23 (deliverability), F-24 (last-admin fix), F-12 (comment edit/delete), F-17 (body search), F-19 (CSV export).

---

## 7. Assumptions

- **[ПРИПУЩЕННЯ R1]** The product owner intends to grow this from a hackathon deliverable into a usable multi-team tool (evidenced by already-shipped User Management + WIP limits, both originally §12 out-of-scope). Therefore returning selected §12 items to scope is legitimate, not a violation of the mandate. *Confidence: Medium — needs owner confirmation.*
- **[ПРИПУЩЕННЯ R2]** "Reach" values are relative for an internal-tool user base (tens–low-hundreds of users), not internet scale. RICE scores are for *ranking*, not absolute forecasting.
- **[ПРИПУЩЕННЯ R3]** Effort letters (S/M/L) express value-relative size only; true engineering estimates are the architect's/developer's to produce.
- **[ПРИПУЩЕННЯ R4]** Existing security/anti-enumeration and team-scoped authorization postures must be preserved by every new feature (e.g. self-service reset stays non-committal; self-profile cannot touch role/teams).
- **[ПРИПУЩЕННЯ R5]** The "fresh DB = schema only, no seed data" rule (§9/V28) constrains F-10 to *runtime* provisioning, not migration seeding.
- **[ПРИПУЩЕННЯ R6]** Priority and (any new) label sets should stay as *fixed/managed* vocabularies rather than fully custom fields, to avoid drifting into the "custom workflows/fields" territory §12 excluded — unless the owner explicitly wants configurability.

## 8. Open questions for the product owner

1. **Scope mandate:** Do you confirm that returning §12 "out of scope" items (assignee, priority, labels, notifications, activity history, attachments, real-time) to the roadmap is desired? Which, if any, remain permanently out of scope? *(Blocks how aggressively Waves 2–3 are pursued.)*
2. **Assignment model (F-02):** Can a ticket be assigned only to a *member* of its team? May *admins* be assignees? Can a ticket be unassigned? Multiple assignees or single? *(Shapes the field and notifications.)*
3. **Priority vocabulary (F-03):** What exact priority values do you want (e.g. Low/Medium/High/Urgent), and should priority influence default board sort?
4. **Notifications scope (F-06/F-13):** Which events must notify (assignment, comment, mention, state change, due-soon)? Is email required in the first cut or in-app only? Any per-user preference/opt-out expectations?
5. **Activity history retention (F-07):** How much history to keep and show, and to whom (all team members vs admins)? Should it be one backbone shared with the admin audit log (F-11)?
6. **Default team behavior (F-10):** For a brand-new self-signup, do you prefer auto-creating/ensuring a shared default team, an onboarding "create your team" step, or admin-must-assign? *(Currently broken silently if the team is missing.)*
7. **i18n demand (F-25):** Is Ukrainian localization actually needed by the user base, or is English-only acceptable for now? *(High-effort; only worth it with real demand.)*
8. **Attachments (F-28):** Is attachment support wanted (esp. for bug screenshots)? If yes, are there constraints on storage/size/type or hosting we must respect? *(Drives a large effort + security review.)*

## 9. Handoff to the architect

- **Decided (BA position):** Themes, feature set, and RICE-based ordering above. Wave-1 top-8 is the recommended next-release slate.
- **Explicitly left to the architect (HOW):** all data-model/schema, API shape, notification delivery mechanism (poll vs push/SSE/WebSocket), event/audit backbone design (shared vs separate for F-07/F-11), token lifetime & rate-limiting for F-01, label ownership model (team vs global), attachment storage strategy, real-time transport, full-text search approach, and true effort estimates.
- **Flagged constraints to honor:** anti-enumeration & block-state rules (F-01/F-04), team-scoped per-resource authorization (all team-touching features), last-write-wins interaction with real-time/optimistic UI (F-20), "fresh DB = schema only" (F-10), and keeping fixed vocabularies unless configurability is explicitly requested (F-03/F-05).
- **Security items to route to security-engineer:** F-01 (reset token lifetime/rate-limit), F-04 (require current password on change), F-21 (API-key/webhook secret handling), F-28 (upload abuse/malware), plus the already-identified SEC-1 fix (F-24).
- **Dependency notes:** F-02 → F-06/F-13/F-09/F-18; F-07 → F-18; F-06 → F-13/F-14/F-15; F-23 → F-13. Do F-24 (SEC-1) and F-23 early.

---

## 10. Traceability — proposal → source signal

| Proposal | Grounded in |
|---|---|
| F-01 password reset | §14 stretch "Password reset flow"; code shows only admin reset exists |
| F-12 comment edit/delete | §14 stretch "Edit or delete own comments"; comments currently immutable (§7) |
| F-07 activity history | §14 stretch "Ticket activity history"; §12 excluded audit history (return-to-scope) |
| F-22 virtualization | §14 stretch "Virtualized rendering for large boards"; §8 100+ NFR; TEST_REPORT perf gap |
| F-02/03/05/08 assignee/priority/labels/due | §12 "advanced PM features" excluded; ticket entity confirms none exist; industry-standard (§11) |
| F-06/13/14/15 notifications/mentions/watchers | §12 excluded "notifications, mentions, watchers"; industry-standard awareness layer |
| F-20 real-time | §12 excluded "real-time multi-user updates" |
| F-28 attachments | §12 excluded "file attachments" |
| F-18 reporting | §12 excluded "reporting dashboards"; complements shipped WIP limits |
| F-04 profile edit | TEST_REPORT §6 gap "User self-service profile — out of scope, not implemented" |
| F-10 default team | ARCHITECTURE §8 `DEFAULT_SIGNUP_TEAM_NAME` + USER_MANAGEMENT_DESIGN §7.2 (missing team ⇒ no membership) |
| F-11 audit log | SECURITY_REVIEW_USER_MGMT SEC-3 |
| F-24 last-admin fix | SECURITY_REVIEW_USER_MGMT SEC-1/SEC-2 |
| F-23 deliverability | First-run email path (§3); 2026 provider auth requirements (§11) |

## 11. Sources (industry research)

- Jira vs Linear (2026) — Lane: https://www.laneapp.co/blog/jira-vs-linear-which-tool-wins
- Linear vs Jira 2026 — monday.com: https://monday.com/blog/rnd/linear-or-jira/
- Linear vs Jira — Getguru: https://www.getguru.com/reference/linear-vs-jira
- Trello vs Jira feature breakdown 2026 — Planyway: https://planyway.com/blog/trello-vs-jira-comparison-guide
- Jira vs Trello 2025 features — Softgile: https://softgile.com/en/en-jira-vs-trello-2025-ultimate-features-and-pricing-comparison/
- Email Authentication Protocols 2025 (SPF/DKIM/DMARC/BIMI) — EmailOnAcid: https://www.emailonacid.com/blog/article/email-deliverability/email-authentication-protocols/
- Implementing SPF, DKIM, DMARC — Mailgun: https://www.mailgun.com/blog/dev-life/how-to-setup-email-authentication/

---

*End of research. This document is BA input for the product owner (prioritization decision) and the architect (design of the chosen slate). It states WHAT and WHY; the HOW is the architect's to design.*

---

## Product-owner decisions (2026-07-01)

1. **Scope mandate — CONFIRMED.** The §12 "out of scope" items are RETURNED to the
   roadmap: assignee, priority, labels, notifications, activity history,
   attachments and real-time updates are all in-plan (subject to wave sequencing).
   Nothing is held permanently out of scope at this time.
2. **Assignee (F-02): MULTIPLE assignees per ticket** (many-to-many ticket ↔ users;
   multi-select UI).
3. **Notifications (F-06/F-13): BOTH in-app AND email, on ALL changes.** This makes
   notifications a first-class subsystem — an event backbone + in-app inbox + email
   fan-out covering every ticket/board change. It is a Wave-2 strategic effort with
   F-02 (assignee) and F-23 (email deliverability) as prerequisites, not a quick win.

**Revised sequencing:**
- **Wave 1 (now):** account/onboarding gaps + cheap ticket fields — F-01 password
  reset, F-04 self-profile, F-10 default-team provisioning, F-03 priority,
  F-02 multi-assignee, F-08 due date, F-23 email deliverability (SPF/DKIM/DMARC).
- **Wave 2:** notifications subsystem (in-app + email, all changes), F-12
  edit/delete own comments, activity history, labels/tags.
- **Wave 3:** attachments, real-time board updates, analytics/reporting,
  API/webhooks, i18n (uk/en).
