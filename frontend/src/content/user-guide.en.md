# Ticket Tracker — User & Administrator Guide

Complete product documentation: how to use the system (kanban board, tickets, notifications,
analytics) and how to administer it (teams, users, integrations).

- **Production:** https://honcharenko.pp.ua
- **Interface languages:** Ukrainian 🇺🇦 (default) and English 🇬🇧 — switcher in the header.
- **Audience:** end users and administrators. No technical detail (architecture, deployment,
  API contracts) here — see `docs/ARCHITECTURE.md` and `docs/adr/` for that.
- 🇺🇦 **Українською:** [USER_GUIDE.md](USER_GUIDE.md)

> **Markers used below**
> - 👤 **User** — available to any authenticated user, within their own teams.
> - 🛡 **Admin** — administrators only (the backend re-checks this permission on every request).

---

## Contents

1. [System overview](#1-system-overview)
2. [Roles & access](#2-roles--access)
3. [Sign-up, sign-in & passwords](#3-sign-up-sign-in--passwords)
4. [Kanban board](#4-kanban-board)
5. [Tickets](#5-tickets)
6. [Comments](#6-comments)
7. [Attachments](#7-attachments)
8. [Labels](#8-labels)
9. [Epics](#9-epics)
10. [Teams](#10-teams)
11. [Notifications & watching](#11-notifications--watching)
12. [Activity history](#12-activity-history)
13. [Real-time updates](#13-real-time-updates)
14. [Analytics](#14-analytics)
15. [User management (admin)](#15-user-management-admin)
16. [Account settings](#16-account-settings)
17. [Public API & access keys](#17-public-api--access-keys)
18. [Webhooks](#18-webhooks)
19. [Interface language](#19-interface-language)
20. [Security & privacy](#20-security--privacy)

---

## 1. System overview

Ticket Tracker is a **kanban**-style task-tracking system. Work is organized like this:

- **Teams** own all content — tickets, epics, labels, webhooks. Every ticket belongs to exactly
  one team.
- **Tickets** move through 5 workflow columns: from *New* to *Done*.
- **Epics** group related tickets within a team.
- **Labels** are colored tags for cross-cutting classification of a team's tickets.
- **Notifications** keep involved people up to date — instantly in the app and, optionally, as an
  email digest.
- **Analytics** show a team's status and pace in numbers and charts.

Main navigation: **Board · Teams · Epics · Analytics · Notifications · Help** (and **Users** for
admins). On the right of the header: the notifications bell, the language switcher, and the profile
menu. The **Help** item opens this guide right inside the app (in the interface language).

---

## 2. Roles & access

There are two roles:

| Role | What they can do |
|------|------------------|
| 👤 **Member** | Works only within the teams they belong to: views the board, creates and edits tickets, comments, adds attachments, manages watches and their own profile, and sets WIP limits for their teams. |
| 🛡 **Administrator** | Has access to **all** teams without restriction, plus admin actions: create/rename/delete teams, manage users, assign ticket assignees, manage labels and webhooks. |

A few important rules:

- **Team scoping.** A member sees and acts only within their teams. An admin ignores those
  boundaries and sees everything.
- **Last admin.** The system always keeps at least one active administrator — the last one can't be
  demoted or blocked.
- **Assigning ticket assignees** is an admin action. A member can see who is assigned but can't
  change the assignee set (a hint next to the field explains this).

---

## 3. Sign-up, sign-in & passwords

### Sign-up 👤
On the **Sign up** page enter an email and a password (at least 8 characters, with confirmation).
The system then sends an **email-verification** link. Sign-in is unavailable until you verify.

### Email verification
Follow the link from the email — your account is activated and you're taken straight into the app.

### Sign-in 👤
The **Log in** page — email and password. Your session persists across visits until you log out.

### Forgot your password? 👤
1. On the login page click **Forgot password?**
2. Enter your email — if the account exists, a reset link is sent.
3. Follow the link to set a new password. All previous sessions are ended at that point.

> For security, the system doesn't reveal whether an account with a given email exists — the
> "email sent" message is identical either way.

### Sign-out
Profile menu (top right) → **Log out**.

---

## 4. Kanban board

The **Board** is the main work screen. Pick a team at the top; the board with its 5 columns appears
below.

### Columns (workflow states)

| # | State | Meaning |
|---|-------|---------|
| 1 | **New** | Ticket created, work not yet planned. |
| 2 | **Ready for implementation** | Agreed, can be picked up. |
| 3 | **In progress** | Being worked on. |
| 4 | **Ready for acceptance** | Done, awaiting review/acceptance. |
| 5 | **Done** | Completed. |

### Moving tickets
- **With the mouse:** drag a card to another column (drag-and-drop).
- **With the keyboard (accessibility):** focus a card's **"Move ticket"** handle, press
  **Space/Enter** to pick it up, use **← →** to choose a column, and **Space/Enter** again to drop.
  **Esc** cancels. Moves are announced to screen readers.

### WIP limits
A column can have a **Work-In-Progress limit**. If a column has reached its limit, you can't move
another ticket into it — finish existing ones first. Cards and columns show a "full" / "over limit"
marker. Limits are configured on the **Teams** page (see §10).

### Filters & search
The filter bar above the board:

- **Search by title** — quick search field.
- **Type** — Bug / Feature / Fix.
- **Priority** — Low / Medium / High / Urgent.
- **Epic** — by a specific epic.
- **Assignee** — by assignee, or **"Assigned to me"**.
- **Label** — by label.
- **Due date** — overdue / has due date / no due date.
- **Clear** — resets all filters. A counter shows how many tickets match.

### Creating a ticket
The **"+ New ticket"** button opens the create form (see §5).

---

## 5. Tickets

A ticket is a unit of work. Open it by clicking a card; the create/edit form contains:

| Field | Description |
|-------|-------------|
| **Team** | Which team the ticket belongs to (required). |
| **Epic** | Optional link to a team epic. |
| **Type** | Bug / Feature / Fix. |
| **State** | Current workflow column. |
| **Priority** | Low / Medium / High / Urgent. |
| **Due date** | Deadline; overdue tickets are flagged. |
| **Assignees** | **Multiple** assignees via a convenient multi-select dropdown. Edited by admins. |
| **Labels** | Multiple team labels via a dropdown. |
| **Title** | Short headline (required). |
| **Body** | Ticket description (required). |

### Key capabilities
- **Multiple assignees.** A ticket can be assigned to several people at once. The field is a
  multi-select dropdown. 🛡 Editing assignees is an admin action; a member sees how many/who is
  assigned.
- **Priority** shows on the card and is available in filters and analytics.
- **Due date.** Approaching/exceeding the due date is highlighted with an "Overdue" badge.
- **Labels** are chosen with the same dropdown (see §8). If the team has no labels yet, the system
  points you to create them on the Teams page.
- **Watch.** The **"👁 Watch / Watching"** button turns on notifications about changes to this
  ticket (see §11). The author automatically watches their own ticket.

### Ticket actions
- **Create / Save** — the form validates required fields (team, title, body).
- **Delete** — permanently deletes the ticket **and all of its comments** (with confirmation).
- Meta info: who created it and when, and when it was last modified.

> **WIP and saving.** If the target column is already at its WIP limit, saving a ticket into that
> state will fail — free up room in the column first.

---

## 6. Comments

A ticket page has a **Comments** thread:

- 👤 **Add comment** — an input at the bottom with a **"Post comment"** button.
- 👤 **Edit** your own comment — edited ones are marked "(edited)" with a date.
- 👤 **Delete** your own comment (with confirmation; can't be undone).

A new comment from another member generates a notification for the ticket's watchers (see §11).

---

## 7. Attachments

You can attach files to a ticket:

- 👤 **Upload file** — images, PDF, text, CSV, or Office documents, **up to 10 MB**.
- 👤 **Download** an attachment.
- 👤 **Delete** an attachment (with confirmation).

Files are stored safely; the type is verified by content (not just by extension) and is served to
the browser as a download (never executed).

---

## 8. Labels

**Labels** are colored tags for classifying a team's tickets.

- **Create/edit/delete** labels on the **Teams** page → the **"Labels"** action (🛡 the team-management
  admin zone). Set a name and a color from a palette or a custom one.
- **Use them** in the ticket form's **Labels** field via a multi-select dropdown. Each label shows
  as its colored chip.
- Deleting a label removes it from all tickets (with confirmation).
- The board filters and analytics both offer breakdowns by label.

---

## 9. Epics

**Epics** group related tickets within a team (for example, a large feature made of several tasks).

The **Epics** page:
- Pick a team and view its epics.
- 👤 **Create epic** — title and (optional) description.
- **Edit / Delete** an epic. An epic still referenced by tickets can't be deleted — reassign or
  remove those tickets first.
- Tickets link to an epic in their own form; the board can filter by epic.

---

## 10. Teams

**Teams** are the containers for all content. The **Teams** page lists them with ticket and epic
counts and a last-modified date.

### Actions
- 🛡 **Create team** — by name.
- 🛡 **Rename / Delete** a team. Only an empty team (no tickets or epics) can be deleted.
- 👤 **WIP limits** — caps on the number of tickets per column. A blank field = no limit (a whole
  number from 1 to 999). Members can configure these too.
- 🛡 **Labels** — manage the team's labels (see §8).
- 🛡 **Webhooks** — the team's integrations with external systems (see §18).

### Membership
- A member sees only the teams they've been added to (an admin adds them in the Users section).
- An admin has access to all teams regardless of membership.

---

## 11. Notifications & watching

### Two channels
1. **In-app — always on.** The bell in the header shows the unread count. The **Notifications** page
   is the full list; there's **"Mark all read"**. Clicking a notification takes you straight to the
   relevant ticket.
2. **Email digest — optional.** In **Account settings** you can enable digest emails about updates
   to tickets you watch (see §16). Emails are off by default; in-app notifications are always on.

### Who gets notified (watchers)
Notifications go to the people **involved** with a ticket — those who **watch** it:
- The author automatically watches the ticket they create.
- Anyone can subscribe/unsubscribe with the **"👁 Watch"** button on the ticket page.

### Anti-noise
- **The person who performs an action isn't notified about their own action.** If you changed a
  ticket or added a comment yourself, you won't get a notification — the other watchers will.
- Notifications are sent for ticket changes (state, fields) and for new comments.

---

## 12. Activity history

A ticket page has an **Activity** block — a chronological log of changes: creation, field and state
changes, comments, and so on. Entries load in batches via **"Load more"**. This gives full
transparency into who did what to a ticket.

---

## 13. Real-time updates

The board and ticket pages **update live**: when a colleague moves a card, edits a ticket, or adds
a comment, you see the change without reloading the page. If the live connection isn't available,
the system gracefully falls back to periodic refresh, so data still stays current.

---

## 14. Analytics

The **Analytics** page is a dashboard for the selected team over a chosen period.

### Period
Ready-made presets: **last 4 / 12 / 26 weeks**, **year to date**, or a **custom range** of dates.

### Metrics (tiles)
- **Open** — number of unfinished tickets.
- **Done** — completed ones.
- **Overdue** — past their due date.
- **Avg cycle time** — with the median and sample size.

### Charts
- **Tickets by state** — distribution across columns.
- **Tickets by priority**.
- **Tickets by type** (doughnut).
- **Open vs done** (doughnut).
- **Throughput** — how many are completed per week.
- **Tickets by label**.
- **Work in progress vs limit** (WIP), with an over-limit marker.

Charts have text descriptions for screen readers.

---

## 15. User management (admin)

🛡 The **Users** section (admins only). A table with name/email, role, teams, verification status,
and account state.

### Actions
- **Create user** — email, name (optional), password (or **generate a strong one automatically**),
  role (admin/member), and teams. A generated password is shown **only once**.
- **Edit** — name, role, team membership. Admins have access to all teams; membership is optional
  for them.
- **Reset password** — generates a new password (shown once) and ends all the user's sessions.
  Unavailable for a blocked account — unblock it first.
- **Block / Unblock** — a blocked user is signed out and can't log in until unblocked.

### Filters
By role, team, email-verification status, and account state; search by name/email.

### Account statuses
- **Active** — can work.
- **Unverified** — hasn't verified their email yet.
- **Blocked** — sign-in denied.

> The system always keeps at least one active administrator.

---

## 16. Account settings

Profile menu → **Account**. Sections:

### Profile
- **Email** (view only).
- **Display name** — shown instead of your email.

### Change password
- Current password → new → confirm. After the change, **other devices are signed out**.

### Notifications
- **"Email me notification digests"** — turn digest emails about updates to tickets you watch on or
  off. In-app notifications are always active regardless of this toggle.

### API keys
- Manage personal access keys for the public API (see §17).

---

## 17. Public API & access keys

For integrations there's a **public API** (`/api/v1`), accessed with personal keys.

- **Create a key** — in Account → **API keys** → "New key": a name and **scopes** — read, or
  read + write.
- **Format.** A key is prefixed `ptk_` and sent in the `Authorization: Bearer ptk_…` header. The
  **full key is shown only once** — copy it immediately.
- **Key restrictions (security).** Keys have narrow rights: they can **never delete data** and have
  **no access to admin functions**, and they work only within the explicitly granted teams.
- **Revocation.** A revoked key stops working immediately. The table shows the prefix, scopes, when
  it was last used, and its status.

---

## 18. Webhooks

🛡 **Webhooks** (on the **Teams** page → the "Webhooks" action) notify an external URL about ticket
events.

- **Create** — specify the **endpoint URL** and a list of **events** (or "All events `*`").
- **Signing.** Each request is signed with **HMAC-SHA256** so the receiver can verify authenticity.
  The secret is shown once; it can be **rotated**.
- **Manage** — enable/disable a webhook, **send a test ping**, view **delivery history** (event,
  status, attempt count, last result/HTTP code), and delete.

> For security, outgoing webhook requests are protected against reaching internal network addresses.

---

## 19. Interface language

The language switcher in the header: **Ukrainian** (default) and **English**. Your choice is saved
for your browser and takes your profile language into account. The whole interface, dates, and
labels are localized.

---

## 20. Security & privacy

Key product-level guarantees:

- **Email verification** is required before sign-in.
- **Passwords** are stored securely (hashed); at least 8 characters.
- **Sessions** end on password change/reset and on account blocking.
- **Access separation:** members see only their teams; every request is checked on the server.
- **API keys** are restricted: no data deletion, no admin access, granted teams only.
- **Webhooks** are HMAC-signed and protected against reaching the internal network.
- **Attachments** are verified by content and served only as downloads.
- **Connections** are protected with TLS (HTTPS).

---

*This document describes current product functionality (waves 1–3 + security hardening). For
technical detail see `docs/ARCHITECTURE.md`, decisions in `docs/adr/`, and the test report in
`docs/TEST_REPORT.md`.*
