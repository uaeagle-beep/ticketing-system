# Ticketing System — Business Analysis & Requirements Specification

> **Status:** Analysis for architecture/design input
> **Author role:** Business Analyst
> **Canonical source:** [`docs/REQUIREMENTS_SOURCE.md`](./REQUIREMENTS_SOURCE.md) (transcribed from `task.pdf`)
> **Scope:** Mandatory scope only — authentication (with email verification), teams, epics, tickets, comments, Kanban board. Stretch and out-of-scope items are explicitly flagged, not specified.
> **Convention:** Every section number `§N` below refers to the matching section in `REQUIREMENTS_SOURCE.md`. Backend-enforced rules are tagged **[BACKEND-ENFORCED]**. Assumptions are tagged **[ASSUMPTION]** and consolidated in §A.

---

## 0. How to read this document

- **Epics → User Stories → Acceptance Criteria (Gherkin).** Each story is uniquely IDed (`US-<area>-<n>`) so architecture, dev, and QA can trace it.
- **Acceptance Criteria** are written in Given/When/Then. They are intentionally implementation-agnostic (WHAT/WHY, not HOW).
- **Traceability matrix (§13)** maps every requirement → source section → story/criterion.
- **DoD mapping (§14)** links each Definition-of-Done checkbox to the stories that satisfy it.
- Statements about validation deliberately distinguish **client UX** (helpful, fast) from **[BACKEND-ENFORCED]** (authoritative; client validation alone is insufficient — see §6 of source).

---

## 1. Stakeholders, actors & glossary

### 1.1 Actors
| Actor | Description | Authenticated | Verified |
|---|---|---|---|
| **Visitor** | Anyone who can reach public screens (sign-up, login, verification result, resend). | No | No |
| **Unverified user** | Has an account but email not yet verified. Can log in only far enough to be told to verify / resend; cannot use business screens. | Partial | No |
| **Verified user** | Full access to all business features. There are no roles, admins, or per-team membership (§12). | Yes | Yes |
| **QA / Operator** | A verified user who exercises the system to create demo/test data through UI or API (§13). Not a distinct role in the model. |
| **SMTP service** | External mail relay (`relay1.dataart.com`) used to deliver verification emails (§3). |

> **[ASSUMPTION A1]** "Unverified user" cannot authenticate into the business application at all; the only post-login outcome for an unverified account is a "not verified" message plus the resend action. The system does not issue a usable session to unverified accounts. (Source §3: "A newly registered account cannot use the main application until the email address is verified" + resend available "from the login or verification-result screen".)

### 1.2 Glossary
| Term | Meaning |
|---|---|
| **Team** | A grouping container for tickets and epics. No ownership/membership (§4). |
| **Epic** | A larger work item belonging to exactly one team; optionally referenced by tickets (§5). |
| **Ticket** | The core work item, owns state on the Kanban board (§6). |
| **Comment** | Immutable note attached to a ticket (§7). |
| **State** | One of 5 canonical workflow values; equals a Kanban column (§6, §8). |
| **Type** | Classification label `bug` / `feature` / `fix` — no behavioral difference (§6). |
| **Verification token** | Single-use, 24h-expiry token delivered by email to verify an account (§3). |

### 1.3 Canonical enumerations
- **Ticket Type** (`bug` | `feature` | `fix`) — exactly these three values, API-canonical lowercase.
- **Ticket State** (workflow order): `new` → `ready_for_implementation` → `in_progress` → `ready_for_acceptance` → `done`.
  - **UI labels** (human-readable, with spaces, per wireframes §15): `NEW`, `READY FOR IMPLEMENTATION`, `IN PROGRESS`, `READY FOR ACCEPTANCE`, `DONE`.
  - **[ASSUMPTION A2]** UI label rendering: API value `ready_for_implementation` ↔ label "Ready for implementation" (display-cased per component; wireframe shows column headers UPPERCASE, type badges UPPERCASE). Cards may move between any two states; sequential transitions are NOT enforced (§8).

---

## 2. EPIC E1 — Authentication & Email Verification

**Source:** §3, §9 (auth transport rules), §10 (screens), §11 (security), §15 (Wireframe 2).
**Goal (WHY):** Only people who own a real email address can use the system; credentials are stored safely; verification is time-bounded and tamper-resistant.

### Functional requirements (E1)
- FR-E1-1 Sign up with **email + password**; email trimmed, compared case-insensitively, must be unique (§3).
- FR-E1-2 Password ≥ 8 chars; never stored in plaintext; hashed with an established algorithm — **Argon2id** (§3, §11).
- FR-E1-3 On sign-up, send a verification email via configurable SMTP; must support `relay1.dataart.com` (§3).
- FR-E1-4 Unverified account cannot use the main application (§3).
- FR-E1-5 Verification token expires after **24h**, is **single-use**; success leads to login screen; auto-login NOT required (§3).
- FR-E1-6 Unverified user can request a new verification email; issuing a new token **invalidates earlier unused tokens** (§3).
- FR-E1-7 Login / logout with local credentials; no SSO (§3).
- FR-E1-8 All business screens & API endpoints require authentication, except: sign-up, login, email verification, resend; static assets + optional health/readiness may be public (§3).
- FR-E1-9 **[BACKEND-ENFORCED]** Session/access/bearer tokens must NOT appear in URLs. A single-use verification token MAY be in the verification URL (§9).

> **[ASSUMPTION A3]** Login attempts use generic failure messaging ("Invalid email or password") to avoid user enumeration; this is good security practice consistent with §11 ("avoid exposing credentials"). Wireframe shows a separate "Account not verified? Resend email" affordance, implying the system may distinguish *unverified* from *wrong credentials* for that specific affordance — see A4.
>
> **[ASSUMPTION A4]** When valid credentials belong to an unverified account, the system communicates that the account is unverified and offers resend (per Wireframe 2 "Account not verified? Resend email"). This is an intentional, scoped exception to strict non-enumeration, limited to the unverified-state hint, and only after correct credentials are supplied. (Resolvable by architect; documented so it is a conscious decision.)
>
> **[ASSUMPTION A5]** Password complexity beyond "≥ 8 characters" is NOT required by source; we do not impose uppercase/symbol rules. Max length is unspecified; **[BACKEND-ENFORCED]** a sane upper bound (e.g. ≤ 1024 chars) should be applied to prevent Argon2id DoS via huge inputs.
>
> **[ASSUMPTION A6]** Email format is validated syntactically (RFC-ish) on backend; normalization = trim + lowercase for uniqueness comparison, while preserving original case for display is optional. We store normalized lowercase as the unique key.
>
> **[ASSUMPTION A7]** Interface language is **English** (per all wireframe labels). No localization in scope.

---

### US-AUTH-1 — Sign up with email and password
**As a** visitor, **I want** to create an account with my email and a password, **so that** I can later access the ticket tracker.

```gherkin
Feature: Account sign-up

  Background:
    Given I am an unauthenticated visitor on the sign-up screen

  Scenario: Successful sign-up with valid, unique email
    Given no account exists for "alex@dataart.com"
    When I submit email "  Alex@DataArt.com " and a password of at least 8 characters with a matching confirmation
    Then a new unverified account is created keyed by the normalized email "alex@dataart.com"
    And the password is stored only as an Argon2id hash, never in plain text
    And a verification email is sent through the configured SMTP service
    And I am shown a message that email verification is required
    And I am NOT automatically logged in

  Scenario: Email uniqueness is case-insensitive and trim-insensitive
    Given an account already exists for "alex@dataart.com"
    When I submit email " ALEX@dataart.com " with a valid password
    Then the backend rejects the request as a duplicate
    And no second account is created
    And I see a clear "email already registered" validation message

  Scenario Outline: Password too short is rejected by the backend
    When I submit a valid unique email with password "<pw>"
    Then the backend rejects the request with a validation error
    And no account is created
    Examples:
      | pw       |
      | 1234567  |
      |          |

  Scenario: Confirmation password mismatch (client UX)
    When I enter a password and a non-matching confirmation
    Then the form shows a mismatch error and does not submit

  Scenario: Empty/blank email is rejected
    When I submit email "   " with a valid password
    Then the backend rejects the request as invalid email
```
**Notes:** Confirm-password is a client-side UX guard (Wireframe 2); the backend authoritative checks are uniqueness, email validity, and length. **[BACKEND-ENFORCED]:** uniqueness (case-insensitive, trimmed), min length 8, email syntactic validity.

---

### US-AUTH-2 — Receive and consume a verification token (24h, single-use)
**As an** unverified user, **I want** to verify my email via the link I received, **so that** I can use the application.

```gherkin
Feature: Email verification token lifecycle

  Scenario: Verify with a valid, unexpired, unused token
    Given I received a verification token less than 24 hours ago that has not been used
    When I open the verification URL containing that token
    Then my account becomes verified
    And I am shown a success result "Email verified — Your account is ready to use."
    And I am offered "Continue to login"
    And I am NOT automatically logged in

  Scenario: Token is single-use
    Given my verification token was already used successfully once
    When I open the same verification URL again
    Then verification does not succeed a second time
    And I am shown an invalid/expired-link error with a resend action

  Scenario: Token expired after 24 hours
    Given my verification token was issued more than 24 hours ago
    When I open the verification URL
    Then verification fails
    And I am shown an expired-link error with a resend action

  Scenario: Malformed or unknown token
    When I open a verification URL with a token that does not exist
    Then verification fails with an invalid-link error and a resend action

  Scenario: Already-verified account opening a stale link
    Given my account is already verified
    When I open an old (now invalid) verification link
    Then I am shown that the link is no longer valid (or that the account is already verified)
    And I can proceed to login
```
**[BACKEND-ENFORCED]:** expiry (24h from issuance), single-use (mark consumed atomically), token validity. Verification token MAY be in the URL (§9).

---

### US-AUTH-3 — Resend verification email (invalidates prior tokens)
**As an** unverified user, **I want** to request a fresh verification email, **so that** I can verify even if my prior link expired or was lost.

```gherkin
Feature: Resend verification email

  Scenario: Resend issues a new token and invalidates earlier unused tokens
    Given my account is unverified and I have one previously issued, unused token T1
    When I request a new verification email from the login or verification-result screen
    Then a new token T2 is issued and emailed via SMTP
    And the earlier unused token T1 is invalidated and can no longer verify the account

  Scenario: Resend available from verification-result (expired/invalid) screen
    Given I landed on the verification-result screen with an expired/invalid link
    When I use the resend action
    Then a new verification email is sent

  Scenario: Resend for an already-verified account
    Given my account is already verified
    When a resend is requested for that email
    Then no usable new verification token grants further access
    And the response does not leak whether the account exists or is verified

  Scenario: Resend for a non-existent email (no enumeration)
    Given no account exists for the submitted email
    When a resend is requested
    Then the response is generic and does not reveal that the account does not exist
```
**[BACKEND-ENFORCED]:** new-token issuance invalidates earlier unused tokens (§3). **[ASSUMPTION A8]** Resend responses are non-committal to avoid account enumeration; UI shows a neutral "If an account needs verification, an email has been sent." A light rate-limit on resend is recommended (see NFR-SEC-5) though not explicitly required.

---

### US-AUTH-4 — Log in with local credentials
**As a** verified user, **I want** to log in, **so that** I can access the board and manage data.

```gherkin
Feature: Login

  Scenario: Successful login for verified account
    Given I have a verified account with correct credentials
    When I submit the correct email and password
    Then I am authenticated and granted a session/bearer token (not placed in any URL)
    And I am taken into the application (e.g., the Kanban board)

  Scenario: Wrong password
    When I submit a known email with an incorrect password
    Then login fails with a generic "invalid email or password" message
    And no session is granted

  Scenario: Unverified account attempting login
    Given I have an account that is not yet verified
    When I submit the correct email and password
    Then I am NOT granted access to the business application
    And I am informed the account is not verified
    And I am offered the resend-verification action

  Scenario: Email matching is case-insensitive and trimmed at login
    Given my account email is "alex@dataart.com"
    When I log in with " ALEX@dataart.com " and the correct password
    Then login succeeds
```
**[BACKEND-ENFORCED]:** credential verification against Argon2id hash; unverified accounts denied business access; token never in URL.

---

### US-AUTH-5 — Log out
**As a** verified user, **I want** to log out, **so that** my session cannot be reused on a shared machine.

```gherkin
Feature: Logout

  Scenario: Log out from the user menu
    Given I am logged in
    When I choose "Log out" from the user menu in the header
    Then my session/token is invalidated for further authenticated requests
    And I am returned to a public screen (e.g., login)

  Scenario: Using a stale token after logout
    Given I have logged out
    When a request is made with the previously valid token
    Then the request is rejected as unauthenticated
```
**Wireframe 1:** collapsed user menu shows email + Log out.

---

### US-AUTH-6 — Protect all business endpoints/screens
**As the** system, **I want** to require authentication on all business endpoints, **so that** data is not exposed to anonymous callers.

```gherkin
Feature: Authentication gate

  Scenario Outline: Unauthenticated access to business resources is denied
    Given I have no valid session/token
    When I call a business endpoint "<endpoint>"
    Then the backend responds 401 Unauthorized
    Examples:
      | endpoint        |
      | teams           |
      | epics           |
      | tickets         |
      | comments        |
      | kanban board    |

  Scenario Outline: Public endpoints remain accessible without auth
    Given I have no valid session/token
    When I access "<public>"
    Then the request is allowed
    Examples:
      | public                    |
      | sign-up                   |
      | login                     |
      | email verification        |
      | resend verification email |
      | static frontend assets    |
      | health/readiness (optional)|
```
**[BACKEND-ENFORCED]:** every non-exempt endpoint requires a valid session/token (§3, §9).

---

## 3. EPIC E2 — Teams CRUD

**Source:** §4, §9 (409), §10, §15 (Wireframe 4).
**Goal (WHY):** Provide the grouping container for tickets/epics, with safe naming and safe deletion.

### Functional requirements (E2)
- FR-E2-1 Verified users can view, create, rename, delete teams (§4).
- FR-E2-2 Team has ≥ identifier, name, created timestamp, modified timestamp (§4).
- FR-E2-3 Name non-empty after trim; **unique case-insensitively** (§4).
- FR-E2-4 A team cannot be deleted while it contains tickets **or** epics → **HTTP 409**, with clear UI message; no cascade (§4, §9).
- FR-E2-5 No ownership/membership; all verified users manage all teams (§4).
- FR-E2-6 Team management table shows Name, Tickets count, Epics count, Modified, Actions (Edit/Delete); Delete disabled while team has tickets or epics (Wireframe 4).

> **[ASSUMPTION A9]** "Modified" timestamp on a team advances when the team's own fields change (rename). Creating/deleting tickets or epics inside a team is NOT defined by source as changing the team's modified timestamp; we treat team `modified_at` as reflecting changes to the team entity itself only. (Symmetric with ticket rule that unrelated child activity does not touch the parent timestamp.)
>
> **[ASSUMPTION A10]** Rename to the team's current name (case/whitespace-normalized identical) is a no-op that does NOT advance `modified_at` (mirrors ticket "save unchanged ⇒ no advance" principle in §6). Documented as an assumption since §4 does not state it explicitly.

---

### US-TEAM-1 — Create a team
```gherkin
Feature: Create team

  Background:
    Given I am a verified, authenticated user on the Team management screen

  Scenario: Create a uniquely named team
    Given no team named "Platform" (case-insensitive) exists
    When I create a team named "  Platform  "
    Then a team named "Platform" (trimmed) is persisted with created and modified timestamps set in UTC
    And it appears in the teams list with 0 tickets and 0 epics

  Scenario: Reject empty/blank name
    When I submit a team name of "   "
    Then the backend rejects it as a non-empty-name validation error
    And no team is created

  Scenario: Reject duplicate name case-insensitively
    Given a team named "Platform" exists
    When I create a team named "platform" or " PLATFORM "
    Then the backend rejects it as a duplicate-name conflict
    And no team is created
```
**[BACKEND-ENFORCED]:** non-empty trimmed name; case-insensitive uniqueness.

---

### US-TEAM-2 — Rename a team
```gherkin
Feature: Rename team

  Scenario: Rename to a new unique name
    Given a team "Platform" exists and "Payments" does not
    When I rename "Platform" to "Payments"
    Then the team name becomes "Payments"
    And the team modified timestamp advances

  Scenario: Rename collides with another team's name (case-insensitive)
    Given teams "Platform" and "Payments" exist
    When I rename "Platform" to "payments"
    Then the backend rejects it as a duplicate-name conflict
    And the name is unchanged

  Scenario: Rename to the same value is a no-op
    Given a team "Platform" exists with a known modified timestamp
    When I rename it to "  platform  " (normalizes to the same name)
    Then no change is persisted and the modified timestamp does not advance
```
**[BACKEND-ENFORCED]:** uniqueness on rename; non-empty trim.

---

### US-TEAM-3 — Delete a team (guarded; 409 when not empty)
```gherkin
Feature: Delete team

  Scenario: Delete an empty team
    Given a team "Sandbox" has no tickets and no epics
    When I confirm deletion of "Sandbox"
    Then the team is removed from the database
    And it no longer appears in the teams list

  Scenario: Cannot delete a team that contains tickets
    Given a team "Platform" has at least one ticket
    When I attempt to delete "Platform"
    Then the backend responds HTTP 409 Conflict
    And nothing is deleted (no cascade)
    And the UI shows a clear validation message explaining tickets must be removed first

  Scenario: Cannot delete a team that contains epics
    Given a team "Platform" has at least one epic and no tickets
    When I attempt to delete "Platform"
    Then the backend responds HTTP 409 Conflict
    And the team and its epics remain

  Scenario: Delete action disabled in UI when team is non-empty
    Given a team has tickets or epics
    When I view the teams table
    Then its Delete action is disabled with an explanatory hint
```
**[BACKEND-ENFORCED]:** 409 on non-empty delete (tickets OR epics); UI disable is convenience only — backend is authoritative (defends against stale UI / direct API calls).

---

### US-TEAM-4 — View teams with counts
```gherkin
Feature: View teams

  Scenario: List teams with ticket and epic counts
    Given several teams exist with varying numbers of tickets and epics
    When I open the Team management screen
    Then I see each team's name, ticket count, epic count, and modified timestamp
    And the Delete action is enabled only for teams with zero tickets and zero epics

  Scenario: Empty state
    Given no teams exist
    When I open the Team management screen
    Then I see an empty-state message and a way to create the first team
```

---

## 4. EPIC E3 — Epics CRUD

**Source:** §5, §9 (409), §10, §15 (Wireframe 5).
**Goal (WHY):** Group tickets under a larger work item within a single team, with immutable team ownership.

### Functional requirements (E3)
- FR-E3-1 Each epic belongs to exactly one team; team chosen at creation and **cannot change** (no moving between teams) (§5).
- FR-E3-2 Separate screen for epic CRUD: create/list/edit/delete (§5).
- FR-E3-3 Epic has ≥ identifier, team reference, title, optional description, created ts, modified ts (§5).
- FR-E3-4 Epic title non-empty after trim (§5).
- FR-E3-5 A ticket may optionally reference one epic chosen from a dropdown of the ticket's team's epics (§5, §6).
- FR-E3-6 **[BACKEND-ENFORCED]** A ticket may reference only an epic of the **same team** (§5, §6).
- FR-E3-7 An epic cannot be deleted while tickets reference it → **HTTP 409**, clear UI message (§5, §9).
- FR-E3-8 Epic management screen: Team selector; table Title, Tickets count, Modified, Actions (Edit / ×); Delete disabled while tickets reference the epic (Wireframe 5).

> **[ASSUMPTION A11]** Epic title uniqueness is NOT required by source (only non-empty). We allow duplicate epic titles (even within a team) unless the architect decides otherwise.
>
> **[ASSUMPTION A12]** Description is optional and may be empty/null; no max length specified (treat consistently with ticket body — apply a sane DB limit, see NFR).
>
> **[ASSUMPTION A13]** Editing an epic may change title and description only; team is read-only after creation (FR-E3-1). The edit panel (Wireframe 5) shows Title + optional Description, consistent with this.

---

### US-EPIC-1 — Create an epic for a team
```gherkin
Feature: Create epic

  Background:
    Given I am a verified user on the Epic management screen
    And a team "Platform" exists

  Scenario: Create epic with title and team
    When I create an epic titled "  Billing Revamp  " under team "Platform" with an optional description
    Then an epic "Billing Revamp" (trimmed) is persisted, belonging to "Platform"
    And created and modified timestamps are set in UTC

  Scenario: Reject empty title
    When I create an epic with title "   " under team "Platform"
    Then the backend rejects it as a non-empty-title validation error

  Scenario: Team is required and must exist
    When I create an epic referencing a non-existent or null team
    Then the backend rejects the request (missing/invalid team reference)
```
**[BACKEND-ENFORCED]:** non-empty trimmed title; valid existing team reference.

---

### US-EPIC-2 — Edit an epic (team immutable)
```gherkin
Feature: Edit epic

  Scenario: Edit title and description
    Given an epic "Billing Revamp" belongs to team "Platform"
    When I change its title to "Billing v2" and update the description
    Then the changes persist and the modified timestamp advances

  Scenario: Team cannot be changed
    Given an epic belongs to team "Platform"
    When an attempt is made to reassign the epic to team "Payments"
    Then the backend rejects/ignores the team change and the epic stays in "Platform"

  Scenario: Edit to identical values is a no-op
    Given an epic with a known modified timestamp
    When I save the edit form without changing any value
    Then no change is persisted and the modified timestamp does not advance
```
**[BACKEND-ENFORCED]:** team immutability after creation; non-empty title on edit. **[ASSUMPTION A14]** "Save unchanged ⇒ no timestamp advance" extends to epics by analogy with the explicit ticket rule (§6); flagged because §5 does not state it.

---

### US-EPIC-3 — Delete an epic (guarded; 409 when referenced)
```gherkin
Feature: Delete epic

  Scenario: Delete an epic with no referencing tickets
    Given an epic "Old Initiative" has zero referencing tickets
    When I confirm deletion
    Then the epic is removed from the database

  Scenario: Cannot delete an epic referenced by tickets
    Given an epic "Billing Revamp" is referenced by at least one ticket
    When I attempt to delete it
    Then the backend responds HTTP 409 Conflict
    And the epic remains
    And the UI shows a clear message that referencing tickets must be reassigned/removed first

  Scenario: UI disables delete while referenced
    Given an epic is referenced by tickets
    When I view the epics table
    Then its Delete (×) action is disabled with an explanatory hint
```
**[BACKEND-ENFORCED]:** 409 when referenced; UI disable is convenience only.

---

### US-EPIC-4 — List epics by team with counts
```gherkin
Feature: List epics

  Scenario: View epics for a selected team
    Given team "Platform" has several epics
    When I select "Platform" in the Epic screen team selector
    Then I see each epic's title, referencing-ticket count, and modified timestamp
    And only epics of "Platform" are listed

  Scenario: Empty state for a team with no epics
    Given team "Payments" has no epics
    When I select "Payments"
    Then I see an empty-state message and a way to create the first epic
```

---

## 5. EPIC E4 — Tickets

**Source:** §6 (field table + operations), §9, §10, §15 (Wireframe 3).
**Goal (WHY):** Manage the core work items with strict field/enum/reference validation and correct timestamp semantics that drive board ordering.

### Field model (from §6 table)
| Field | Required | Type / values | Server rules |
|---|---|---|---|
| ID | Yes | system-generated (UUID or numeric) | stable, unique |
| Team | Yes | team reference | **[BACKEND-ENFORCED]** must reference existing team |
| Type | Yes | `bug`\|`feature`\|`fix` | **[BACKEND-ENFORCED]** exactly these |
| State | Yes | `new`\|`ready_for_implementation`\|`in_progress`\|`ready_for_acceptance`\|`done` | **[BACKEND-ENFORCED]** exactly these |
| Epic | No | epic reference or null | **[BACKEND-ENFORCED]** null OR epic of the SAME team |
| Title | Yes | text | **[BACKEND-ENFORCED]** non-empty after trim |
| Body | Yes | long text | **[BACKEND-ENFORCED]** non-empty |
| Created at | Yes | timestamp | server-set, UTC, at creation |
| Modified at | Yes | timestamp | server-set, UTC, on actual field/state change; **NOT** on comment add; **NOT** on save-without-change |
| Created by | Yes | user reference | server-set from authenticated user |

> **[ASSUMPTION A15]** Default state on creation is `new` (the first workflow column / Wireframe board). Source does not mandate a default; this is the natural workflow start. Confirmable by architect.
>
> **[ASSUMPTION A16]** `created_by` is immutable after creation; `created_at` and `created_by` are read-only in the UI (Wireframe 3 meta line shows them as display-only).
>
> **[ASSUMPTION A17]** No max length is mandated for title/body; backend should apply pragmatic DB limits (e.g., title ≤ 512, body ≤ a large TEXT bound) and reject overflow rather than truncate silently.
>
> **[ASSUMPTION A18]** Ticket reference label like `TCK-1042` (Wireframe 3) is a human-facing display key. Whether it is the raw ID or a derived key is an architecture decision; functionally the ID just needs to be stable/unique. We do not require the `TCK-####` format.

### Functional requirements (E4)
- FR-E4-1 Create a ticket (§6).
- FR-E4-2 Open/view all fields incl. created by, created at, modified at (§6).
- FR-E4-3 Edit type, team, epic, title, body, state (§6).
- FR-E4-4 Modified timestamp = latest actual field/state change; saving unchanged values must NOT advance it (§6).
- FR-E4-5 When team changes, UI must clear/replace selected epic; backend must reject a ticket whose epic belongs to a different team (§6).
- FR-E4-6 Delete a ticket after explicit confirmation; deleting also deletes its comments (§6).
- FR-E4-7 Drag-and-drop state change persisted immediately (§6, §8 — detailed under E6).
- FR-E4-8 **[BACKEND-ENFORCED]** Validate all submitted enum values and references; client validation alone insufficient (§6).

---

### US-TICKET-1 — Create a ticket
```gherkin
Feature: Create ticket

  Background:
    Given I am a verified user
    And a team "Platform" exists

  Scenario: Create a valid ticket without an epic
    When I create a ticket with team "Platform", type "bug", title "Login fails", body "Steps...", and no epic
    Then the ticket is persisted with state "new" by default
    And created_at and modified_at are set in UTC to the creation time
    And created_by is set to my authenticated user
    And the ticket appears in the NEW column of the Platform board

  Scenario: Create a ticket with a same-team epic
    Given team "Platform" has an epic "Billing Revamp"
    When I create a ticket on "Platform" referencing epic "Billing Revamp"
    Then the ticket is persisted referencing that epic

  Scenario Outline: Reject invalid type
    When I create a ticket with type "<type>"
    Then the backend rejects it as an invalid enum value
    Examples:
      | type     |
      | Bug      |
      | task     |
      | epic     |
      |          |

  Scenario Outline: Reject invalid state (if state is settable on create)
    When I create a ticket with state "<state>"
    Then the backend rejects it as an invalid enum value
    Examples:
      | state         |
      | open          |
      | closed        |
      | READY         |

  Scenario: Reject empty title or body
    When I create a ticket with title "   " or empty body
    Then the backend rejects it as a non-empty validation error

  Scenario: Reject non-existent team
    When I create a ticket referencing a team that does not exist
    Then the backend rejects it with a missing-reference error

  Scenario: Reject epic from a different team
    Given epic "Other" belongs to team "Payments"
    When I create a ticket on team "Platform" referencing epic "Other"
    Then the backend rejects it because the epic is not in the ticket's team
```
**[BACKEND-ENFORCED]:** type enum, state enum, non-empty title/body, existing team, epic-same-team-or-null, server-set created_by/created_at/modified_at.

---

### US-TICKET-2 — View a ticket with full detail
```gherkin
Feature: View ticket details

  Scenario: Open a ticket and see all fields
    Given a ticket exists
    When I open its details view
    Then I see team, type, state, epic (if any), title, body
    And I see created by, created at (UTC), and modified at (UTC)
    And I see its comments oldest-first
```
**Wireframe 3:** meta line `TCK-#### • Created by <user> • Created <ts> UTC • Modified <ts> UTC`.

---

### US-TICKET-3 — Edit ticket fields with correct modified semantics
```gherkin
Feature: Edit ticket

  Scenario: Editing a field advances modified_at
    Given a ticket with a known modified timestamp
    When I change its title (or body, type, state, team, or epic) to a new value and save
    Then the change persists
    And modified_at advances to the time of the change

  Scenario: Saving with no changes does NOT advance modified_at
    Given a ticket with a known modified timestamp
    When I save the edit form without changing any field value
    Then nothing is persisted as a change
    And modified_at does not advance
    And the ticket's board ordering does not change

  Scenario: Changing only state advances modified_at
    Given a ticket in state "new"
    When I change its state to "in_progress" and save
    Then state becomes "in_progress" and modified_at advances

  Scenario Outline: Reject invalid enum/reference on edit
    When I edit a ticket setting <field> to <bad value>
    Then the backend rejects it
    Examples:
      | field | bad value                       |
      | type  | "story"                         |
      | state | "archived"                      |
      | team  | a non-existent team             |
      | epic  | an epic from a different team   |
```
**[BACKEND-ENFORCED]:** modified_at advances only on actual change; enum/reference validation on edit; same-team epic rule.

> **[ASSUMPTION A19]** "Actual change" is determined by comparing normalized incoming values to stored values (e.g., trimmed strings). If, after normalization, all values equal the stored ones, it is a no-op. This makes "saving unchanged values" deterministic and avoids whitespace-only edits bumping the timestamp.

---

### US-TICKET-4 — Change team clears/replaces epic (cross-team integrity)
```gherkin
Feature: Reassign ticket team and keep epic integrity

  Scenario: Changing team in the UI clears the previously selected epic
    Given a ticket on team "Platform" references epic "Billing Revamp" (a Platform epic)
    When I change the ticket's team to "Payments" in the edit form
    Then the UI clears the selected epic
    And the epic dropdown now lists only "Payments" epics

  Scenario: Backend rejects a ticket whose epic belongs to a different team
    Given a request sets team "Payments" but keeps epic "Billing Revamp" (a Platform epic)
    When the request is submitted (e.g., bypassing the UI)
    Then the backend rejects it because the epic is not in the target team

  Scenario: Changing team and choosing a valid same-team epic
    Given I change a ticket's team to "Payments"
    When I select a "Payments" epic and save
    Then the ticket persists on "Payments" referencing the chosen epic
```
**[BACKEND-ENFORCED]:** reject cross-team epic on team change. **Client UX:** clear/replace epic selection on team change (§6).

---

### US-TICKET-5 — Delete a ticket (confirm; cascades comments)
```gherkin
Feature: Delete ticket

  Scenario: Delete with explicit confirmation
    Given a ticket exists with comments
    When I delete it and confirm the action
    Then the ticket is removed
    And all of its comments are removed as well
    And it disappears from the board

  Scenario: Cancelling the confirmation keeps the ticket
    Given a ticket exists
    When I trigger delete but cancel the confirmation
    Then nothing is deleted
```
**Note:** Ticket deletion cascades to comments (§6) — this is the ONLY mandated cascade. Team/epic deletes are blocked (no cascade).

---

## 6. EPIC E5 — Comments

**Source:** §7, §10, §15 (Wireframe 3 comments panel).
**Goal (WHY):** Allow collaborative notes on a ticket without disturbing ticket modified-time/board ordering.

### Functional requirements (E5)
- FR-E5-1 Verified users can add comments to a ticket (§7).
- FR-E5-2 Comment has: identifier, ticket reference, author, body, created timestamp (§7).
- FR-E5-3 Body non-empty (§7).
- FR-E5-4 Displayed chronologically, oldest first (§7).
- FR-E5-5 Adding a comment does NOT update ticket modified_at, so board ordering is unchanged (§7).
- FR-E5-6 Comments immutable after creation for mandatory scope (edit/delete are stretch) (§7).

> **[ASSUMPTION A20]** `author` = the authenticated user who posted the comment, server-set (analogous to ticket `created_by`). Wireframe shows author + time + body.
>
> **[ASSUMPTION A21]** Comment `created_at` is server-set UTC, ISO-8601 in API (consistent with §9 timestamp rule).

---

### US-COMMENT-1 — Add a comment
```gherkin
Feature: Add comment

  Scenario: Post a non-empty comment
    Given I am viewing a ticket
    When I submit a non-empty comment body
    Then a comment is persisted with my user as author and a UTC created timestamp
    And it appears at the bottom of the comment list (newest after older ones)
    And the comment count increments

  Scenario: Reject empty comment
    When I submit a blank/whitespace-only comment body
    Then the backend rejects it as a non-empty validation error
    And no comment is created

  Scenario: Adding a comment does not change the ticket's modified time or board order
    Given a ticket has a known modified timestamp and board position
    When I add a comment to it
    Then the ticket's modified timestamp is unchanged
    And its position within its Kanban column is unchanged
```
**[BACKEND-ENFORCED]:** non-empty body; server-set author + created_at; ticket modified_at untouched.

---

### US-COMMENT-2 — View comments oldest-first
```gherkin
Feature: View comments

  Scenario: Comments listed chronologically oldest-first
    Given a ticket has several comments created at different times
    When I open the ticket details
    Then the comments are displayed oldest first
    And each shows its author, created time, and body

  Scenario: Empty comment state
    Given a ticket has no comments
    When I open its details
    Then I see an empty-state for comments and a way to add one
```

---

### US-COMMENT-3 — Comments are immutable (mandatory scope)
```gherkin
Feature: Comment immutability

  Scenario: No edit or delete in mandatory scope
    Given a comment exists
    When I view it
    Then there is no edit or delete affordance in the mandatory scope
    And the backend exposes no mandatory endpoint to mutate or remove an existing comment
```
**Note:** Edit/delete-own-comment is an explicit stretch feature (§14) — out of mandatory scope.

---

## 7. EPIC E6 — Kanban Board

**Source:** §8, §6 (drag persistence + enum validation), §9, §10, §15 (Wireframe 1).
**Goal (WHY):** Provide the primary working surface: visualize and move tickets through the fixed workflow for one team, with robust persistence, ordering, filtering, and scale.

### Functional requirements (E6)
- FR-E6-1 Primary screen = Kanban board for ONE selected team; team selector (§8, Wireframe 1).
- FR-E6-2 Exactly 5 columns, one per state, in workflow order (§8).
- FR-E6-3 Card shows ≥ title and type; epic recommended; relative modified time + type badge (§8, Wireframe 1).
- FR-E6-4 Drag card between columns ⇒ state change persisted via backend API (§8, §6).
- FR-E6-5 On drag-drop failure, card returns to previous column AND UI shows an error (§8).
- FR-E6-6 Cards may move directly between any two states; sequential transitions NOT required (§8).
- FR-E6-7 Within a column, ordered by most-recently-modified first; persistent manual order NOT required (§8).
- FR-E6-8 Clear way to create a ticket and to open an existing ticket (§8).
- FR-E6-9 Filtering by type and epic + case-insensitive substring search over title; filters combined with **AND**; client- or server-side (§8).
- FR-E6-10 Usable with ≥ 100 tickets on one team board (§8).
- FR-E6-11 Each column shows a count badge; filter bar shows total ticket count; Clear filters button (Wireframe 1).

> **[ASSUMPTION A22]** "Most recently modified first" sorts by ticket `modified_at` descending within each column. Because comments don't touch `modified_at` (§7), commenting never reorders cards — consistent and intended.
>
> **[ASSUMPTION A23]** The count badge per column and the "total ticket count" reflect the CURRENTLY FILTERED set (i.e., counts respect active filters/search). This matches the Wireframe filter-bar "total ticket count" sitting alongside filters. Flagged because source is silent on whether counts are pre- or post-filter; post-filter is the more useful and common interpretation.
>
> **[ASSUMPTION A24]** Search matches a case-insensitive substring of the title only (not body/epic). AND-combination means a card must satisfy every active filter (type AND epic AND search) to be shown.
>
> **[ASSUMPTION A25]** A team must be selected to show a board; with no team selected the board shows an empty/prompt state. With no teams at all, the board prompts the user to create a team first.
>
> **[ASSUMPTION A26]** "Relative modified time" (e.g., "2h ago") is a display concern derived from the UTC `modified_at`; exact UTC timestamps remain available on the ticket details view (Wireframe 3).

---

### US-BOARD-1 — View the board for a selected team
```gherkin
Feature: Kanban board view

  Background:
    Given I am a verified user
    And team "Platform" exists with tickets in various states

  Scenario: Five columns in workflow order
    When I open the board for "Platform"
    Then I see exactly five columns in order: NEW, READY FOR IMPLEMENTATION, IN PROGRESS, READY FOR ACCEPTANCE, DONE
    And each column shows a count badge of the tickets it currently displays

  Scenario: Cards show required info
    When I view a card
    Then it shows at least the ticket title and a type badge (BUG/FEATURE/FIX)
    And it shows the epic name (recommended) and a relative modified time

  Scenario: Switching teams reloads the board
    Given teams "Platform" and "Payments" both have tickets
    When I switch the team selector to "Payments"
    Then the board shows only "Payments" tickets in their state columns

  Scenario: Empty/prompt states
    Given no team is selected
    Then the board shows a prompt to select a team
    And given a selected team has no tickets, each column shows an empty state
```

---

### US-BOARD-2 — Drag-and-drop to change state (persist + rollback)
```gherkin
Feature: Drag-and-drop state change

  Scenario: Successful drag persists immediately
    Given a ticket is in the NEW column
    When I drag it to IN PROGRESS and drop it
    Then the backend persists state "in_progress" immediately
    And the card remains in the IN PROGRESS column
    And after a page refresh the card is still in IN PROGRESS

  Scenario: Failed persistence rolls back the card and shows an error
    Given a ticket is in the NEW column
    And the backend update will fail (e.g., network or server error)
    When I drag it to DONE and drop it
    Then the card returns to its previous column (NEW)
    And the UI displays an error message

  Scenario: Non-sequential moves are allowed
    Given a ticket is in NEW
    When I drag it directly to DONE
    Then the move is accepted (no sequential-transition enforcement)

  Scenario: Drag-induced state change advances modified_at and reorders by modified desc
    Given several tickets are in IN PROGRESS ordered by modified desc
    When I drag a NEW ticket into IN PROGRESS
    Then its modified_at advances
    And it appears at the top of IN PROGRESS (most recently modified first)
```
**[BACKEND-ENFORCED]:** state enum validation on the persisted change; reject invalid target states. **Client behavior:** immediate optimistic move with rollback-on-error.

---

### US-BOARD-3 — Ordering within columns
```gherkin
Feature: Column ordering

  Scenario: Most recently modified first
    Given a column contains tickets with different modified timestamps
    When I view the column
    Then cards are ordered from most recently modified to least

  Scenario: Adding a comment does not reorder
    Given a ticket sits below others in its column
    When I add a comment to it
    Then its position does not change (modified_at unchanged)

  Scenario: No persistent manual ordering
    Given I reorder within a column is not a supported operation
    Then no custom intra-column order is persisted across refreshes
```

---

### US-BOARD-4 — Filter and search (AND logic)
```gherkin
Feature: Filtering and search

  Background:
    Given the board for "Platform" is open with a variety of tickets

  Scenario: Filter by type
    When I select type "bug" in the Type filter
    Then only bug tickets are shown across all columns
    And the column count badges and total count reflect the filtered set

  Scenario: Filter by epic
    When I select epic "Billing Revamp" in the Epic filter
    Then only tickets referencing that epic are shown

  Scenario: Case-insensitive substring search over title
    When I search "LOGIN"
    Then tickets whose title contains "login" (any case) are shown
    And tickets without that substring in the title are hidden

  Scenario: Filters combine with AND logic
    Given I select type "bug" AND epic "Billing Revamp" AND search "login"
    Then only tickets that are bugs AND reference that epic AND have "login" in the title are shown

  Scenario: Clear filters
    Given active filters and a search term
    When I click Clear
    Then all filters/search reset and the full team ticket set is shown
```
**Note:** May be client- or server-side (§8). **[ASSUMPTION A24/A23]** apply (title-only search; counts respect filters).

---

### US-BOARD-5 — Scale to 100+ tickets
```gherkin
Feature: Board scale and usability

  Scenario: Board remains usable with at least 100 tickets on one team
    Given team "Platform" has 100 or more tickets distributed across states
    When I open the board
    Then the board loads and remains responsive for viewing, filtering, searching, and drag-and-drop
    And no functional feature degrades to unusability
```
**Note:** Virtualized rendering is a stretch optimization (§14), not required; the requirement is "remain usable."

---

### US-BOARD-6 — Create/open ticket from the board
```gherkin
Feature: Board navigation to ticket CRUD

  Scenario: Create a ticket from the board
    Given the board for "Platform" is open
    When I click "+ New ticket"
    Then I can create a ticket (team prefilled to the selected team is acceptable)
    And on creation it appears in the appropriate column

  Scenario: Open an existing ticket from the board
    When I click a card
    Then the ticket details/edit view opens showing all fields and comments
```

---

## 8. EPIC E7 — Cross-cutting: API, Persistence & Data Integrity

**Source:** §9, §11 (security), §1–2 (architecture).
**Goal (WHY):** Guarantee that the data layer is the system of record, referential integrity holds, and the API behaves predictably.

### Functional/technical requirements (E7)
- FR-E7-1 All create/update/delete go through the backend API and persist in the RDBMS (§9).
- FR-E7-2 No reliance on browser local storage as system of record (§9).
- FR-E7-3 DB constraints and/or server-side validation maintain referential integrity (§9).
- FR-E7-4 Meaningful HTTP codes & messages: validation, auth, not-found, conflict. Non-empty team delete and referenced-epic delete ⇒ **409** (§9).
- FR-E7-5 IDs may be UUID or numeric; API timestamps ISO-8601 UTC (§9).
- FR-E7-6 Cookie sessions or bearer tokens OK; never in URLs; verification token MAY be in URL (§9).
- FR-E7-7 No concurrent-edit conflict detection; last write wins (§9).
- FR-E7-8 Schema creation automated via migrations/repeatable init (§9).
- FR-E7-9 Fresh DB has NO application data (no users/teams/epics/tickets/comments); migration metadata allowed; no seed data on default startup (§9, §13).

```gherkin
Feature: API and persistence contract

  Scenario: Persisted data survives refresh and restart
    Given I created teams, epics, tickets, and comments through the API/UI
    When the browser is refreshed or the application is restarted
    Then all previously persisted data is still present (RDBMS is the system of record)

  Scenario Outline: Meaningful status codes
    When a request results in "<situation>"
    Then the backend returns "<status>"
    Examples:
      | situation                                   | status              |
      | unauthenticated access to business endpoint | 401 Unauthorized    |
      | validation failure (bad enum, empty field)  | 400 Bad Request     |
      | missing referenced record                   | 404 Not Found       |
      | delete team containing tickets or epics     | 409 Conflict        |
      | delete epic referenced by tickets           | 409 Conflict        |
      | duplicate team name (case-insensitive)      | 409 Conflict        |

  Scenario: Tokens are never placed in URLs
    Given I am authenticated
    Then no session id, access token, or bearer token appears in any URL
    And only a single-use email-verification token may appear in a verification URL

  Scenario: Timestamps are ISO-8601 UTC
    When the API returns any timestamp
    Then it is an ISO-8601 representation in UTC

  Scenario: Fresh database has no application data
    Given the database has just been initialized via migrations
    When I inspect it before any UI/API use
    Then it contains schema and migration metadata only
    And no users, teams, epics, tickets, or comments exist
    And no seed/sample data was loaded by the default startup path

  Scenario: Last write wins (no concurrent-edit detection)
    Given two updates to the same ticket occur close together
    When both are accepted
    Then the last successful write is the persisted result (no conflict error is required)
```

> **[ASSUMPTION A27]** Status-code mapping in the table above for 400/401/404 is the conventional REST interpretation; source explicitly mandates only 409 for the two delete-conflict cases (§9) and "meaningful codes" generally. Duplicate-name is mapped to 409 (a conflict) by analogy; architect may choose 422/400 for validation-style conflicts — documented as a decision point.
>
> **[ASSUMPTION A28]** Referential integrity is enforced at the DB level (FKs) AND validated server-side, so direct-API misuse cannot create orphans or cross-team epic links even if a client misbehaves.

---

## 9. EPIC E8 — Cross-cutting: Deployment, Config & Tooling (DoD enablers)

**Source:** §1–2, §9, §11 (maintainability/security), §13.
**Goal (WHY):** The QA team can run and evaluate the whole system from a clean checkout, on any major OS, with no committed secrets.

- FR-E8-1 Three logical tiers (presentation / API / persistence) clearly separated; SPA frontend; HTTP API backend; RDBMS (e.g., PostgreSQL) in a dedicated container (§1–2).
- FR-E8-2 `docker compose up --build` from repo root starts the complete solution; no host-installed FE/BE/DB runtime beyond Docker Compose; runs on clean Windows/macOS/Linux (§2).
- FR-E8-3 SMTP service configurable; must support `relay1.dataart.com`; SMTP secrets not in source control (§3, §11).
- FR-E8-4 No hard-coded user password or committed secret (§13).
- FR-E8-5 README with prerequisites, configuration, startup commands (§11).
- FR-E8-6 Automated tests: ≥ 1 backend business flow AND ≥ 1 frontend or API flow (§11).

```gherkin
Feature: Clean-checkout startup and configuration

  Scenario: Start from a clean checkout
    Given a clean repository checkout on a laptop with only Docker Compose installed
    When I run "docker compose up --build" from the repository root
    Then the frontend, backend, and database all start
    And the application is usable without installing any FE/BE/DB runtime on the host

  Scenario: SMTP is configurable and supports relay1.dataart.com
    Given SMTP settings are provided via configuration (not committed secrets)
    When the system sends a verification email
    Then it routes through the configured SMTP service, and relay1.dataart.com is supported

  Scenario: No committed secrets or hard-coded passwords
    When the repository is inspected
    Then it contains no committed SMTP credentials and no hard-coded user password

  Scenario: Automated test coverage exists
    Then there is at least one automated test for a backend business flow
    And at least one automated test for a frontend or API flow
```

> **[ASSUMPTION A29]** PostgreSQL is the chosen RDBMS (source says "such as PostgreSQL" — a strong steer). Final choice is architecture's, but analysis assumes a server-based relational DB container.
>
> **[ASSUMPTION A30]** Configuration (DB URL, SMTP host/port/credentials, token secret, base URL for verification links) is supplied via environment variables / compose env, not committed. Verification link base URL must be configurable so emailed links resolve correctly in the QA environment.

---

## 10. Edge cases & backend-enforced validation catalog

This is the consolidated, prioritized list of rules the **backend MUST enforce** (client validation is insufficient — §6). Each maps to a story.

| # | Rule (backend authoritative) | Source | Story |
|---|---|---|---|
| V1 | Email uniqueness case-insensitive + trimmed | §3 | US-AUTH-1 |
| V2 | Password length ≥ 8; never plaintext; Argon2id hash | §3, §11 | US-AUTH-1 |
| V3 | Verification token: 24h expiry, single-use | §3 | US-AUTH-2 |
| V4 | New token issuance invalidates earlier unused tokens | §3 | US-AUTH-3 |
| V5 | Unverified accounts denied business access | §3 | US-AUTH-4/6 |
| V6 | Auth required on all non-exempt endpoints (401) | §3 | US-AUTH-6 |
| V7 | Tokens (session/access/bearer) never in URLs | §9 | US-AUTH-* / E7 |
| V8 | Team name non-empty trimmed; unique case-insensitive | §4 | US-TEAM-1/2 |
| V9 | Team delete blocked if tickets OR epics exist → 409 (no cascade) | §4, §9 | US-TEAM-3 |
| V10 | Epic belongs to exactly one team; team immutable after create | §5 | US-EPIC-1/2 |
| V11 | Epic title non-empty trimmed | §5 | US-EPIC-1/2 |
| V12 | Epic delete blocked while referenced by tickets → 409 | §5, §9 | US-EPIC-3 |
| V13 | Ticket type ∈ {bug,feature,fix} (exact) | §6 | US-TICKET-1/3 |
| V14 | Ticket state ∈ {new, ready_for_implementation, in_progress, ready_for_acceptance, done} | §6 | US-TICKET-1/3, US-BOARD-2 |
| V15 | Ticket team required + must exist | §6 | US-TICKET-1/3 |
| V16 | Ticket epic null OR epic of SAME team (enforced even on team change) | §5, §6 | US-TICKET-1/3/4 |
| V17 | Ticket title non-empty trimmed; body non-empty | §6 | US-TICKET-1/3 |
| V18 | created_at/modified_at server-set UTC; created_by from auth user | §6 | US-TICKET-1 |
| V19 | modified_at advances ONLY on actual field/state change | §6 | US-TICKET-3 |
| V20 | Saving unchanged values must NOT advance modified_at | §6 | US-TICKET-3 |
| V21 | Adding a comment does NOT change ticket modified_at | §7 | US-COMMENT-1, US-BOARD-3 |
| V22 | Ticket delete cascades to its comments (only mandated cascade) | §6 | US-TICKET-5 |
| V23 | Comment body non-empty; author + created_at server-set | §7 | US-COMMENT-1 |
| V24 | Comments immutable in mandatory scope (no edit/delete endpoint) | §7 | US-COMMENT-3 |
| V25 | Drag-drop state change persisted immediately; invalid target state rejected | §6, §8 | US-BOARD-2 |
| V26 | Referential integrity via DB constraints and/or server validation | §9 | E7 |
| V27 | Meaningful HTTP codes incl. 409 for the two delete-conflict cases | §9 | E7 |
| V28 | Fresh DB: no application data; no seed on default startup | §9, §13 | E8 |

### Notable edge cases (explicitly called out for QA/design)
- **EC1** Whitespace-only inputs (email, team name, epic title, ticket title/body, comment body) must be rejected after trimming.
- **EC2** Case/whitespace variants of an existing team name must collide (uniqueness).
- **EC3** Reusing/refreshing an already-consumed verification link must fail gracefully with resend option.
- **EC4** Expired token exactly at/after 24h boundary fails (define boundary inclusively — **[ASSUMPTION A31]** expiry is "issued_at + 24h <= now ⇒ expired", i.e., strictly older than 24h is invalid).
- **EC5** Changing a ticket's team while keeping an incompatible epic (UI clears it; backend rejects if forced).
- **EC6** Saving a ticket edit that only changes whitespace/casing in a way that normalizes to the same value ⇒ no modified_at bump (per A19).
- **EC7** Deleting a team/epic via direct API call (bypassing disabled UI button) must still return 409 when not empty/referenced.
- **EC8** Adding a comment must never move a card on the board (ordering by modified desc; comment doesn't touch modified_at).
- **EC9** Board with 0 teams vs. team with 0 tickets vs. filtered-to-empty — three distinct empty states.
- **EC10** Drag-drop network failure mid-move must visually roll back AND surface an error (not silently revert).
- **EC11** 100+ tickets: filtering/search/sort must remain correct and usable; counts reflect filtered set (A23).
- **EC12** Concurrent edits to the same ticket: last write wins; no conflict error required (§9).
- **EC13** Cross-team epic in dropdown must never appear (dropdown scoped to ticket's team); backend rejects if submitted anyway.
- **EC14** Duplicate epic titles allowed (A11) — ensure UI/QA don't treat as an error.
- **EC15** Logout invalidates token; subsequent use ⇒ 401.

---

## 11. Functional requirements summary (index)

| FR group | Area | Covers |
|---|---|---|
| FR-E1-* | Authentication & verification | sign-up, hashing, SMTP, verify lifecycle, resend, login/logout, auth gate, token-in-URL rule |
| FR-E2-* | Teams | CRUD, uniqueness, delete guard/409, counts |
| FR-E3-* | Epics | CRUD, single immutable team, same-team ticket link, delete guard/409 |
| FR-E4-* | Tickets | full field model, CRUD, modified semantics, team-change/epic integrity, cascade delete, enum/reference validation |
| FR-E5-* | Comments | add, view oldest-first, non-empty, immutability, no modified_at impact |
| FR-E6-* | Kanban board | 5 columns, drag persist + rollback, ordering, filter/search AND, 100+ scale, create/open |
| FR-E7-* | API & persistence | RDBMS system of record, integrity, status codes, ISO-8601 UTC, token transport, last-write-wins, migrations, clean DB |
| FR-E8-* | Deployment/config/test | compose up, SMTP config, no secrets, README, automated tests |

---

## 12. Non-functional requirements (NFR)

**Source:** §11 (primary), reinforced by §1–2, §3, §9.

### Security
- NFR-SEC-1 Protect all authenticated endpoints (deny anonymous → 401). *(§11, §3)*
- NFR-SEC-2 Passwords hashed with Argon2id; never stored/logged in plaintext. *(§3, §11)*
- NFR-SEC-3 Validate all input server-side; reject invalid enums/references/empties. *(§6, §11)*
- NFR-SEC-4 No credentials or SMTP secrets in source control; configuration via env. *(§11)*
- NFR-SEC-5 Tokens never in URLs (except single-use verification token). **[ASSUMPTION A32]** apply light rate-limiting/throttling on resend & login to reduce abuse/enumeration (not mandated, recommended). *(§9)*
- NFR-SEC-6 No user enumeration through sign-up/login/resend differential responses (beyond the scoped unverified hint, A4). *(derived from §11)*

### Reliability
- NFR-REL-1 Browser refresh or app restart must not lose persisted data (RDBMS is system of record). *(§11, §9)*
- NFR-REL-2 Drag-drop failures roll back UI to consistent state and surface errors. *(§8)*
- NFR-REL-3 Data integrity preserved under last-write-wins (no silent corruption; no orphan references). *(§9)*

### Usability
- NFR-USE-1 Display loading, empty, success, and error states where applicable. *(§11)*
- NFR-USE-2 Human-readable state labels with spaces in UI; canonical lowercase in API. *(§6)*
- NFR-USE-3 Clear validation messages for: duplicate team name, blocked team/epic delete (409), invalid inputs, unverified login, expired/invalid verification link. *(§4, §5, §3)*
- NFR-USE-4 Board usable with ≥ 100 tickets. *(§8)*
- NFR-USE-5 Interface language English (A7). *(wireframes)*

### Compatibility
- NFR-COMP-1 Support current desktop Chrome, Edge, or Firefox. *(§11)*
- NFR-COMP-2 Cross-platform: runs on clean Windows/macOS/Linux via Docker Compose. *(§2)*

### Maintainability
- NFR-MNT-1 README with prerequisites, configuration, startup commands. *(§11)*
- NFR-MNT-2 Clear three-tier separation (presentation/API/persistence). *(§1–2)*
- NFR-MNT-3 Schema via automated migrations / repeatable init. *(§9)*

### Testing
- NFR-TST-1 ≥ 1 automated backend business-flow test. *(§11)*
- NFR-TST-2 ≥ 1 automated frontend or API-flow test. *(§11)*

### Performance/Scale
- NFR-PERF-1 Board responsive at ≥ 100 tickets/team; virtualization is stretch only. *(§8, §14)*

---

## 13. Traceability matrix — requirement → source → acceptance criterion

> Legend: source = section in `REQUIREMENTS_SOURCE.md`. AC = the story/scenario that verifies it.

| Req | Requirement (WHAT) | Source | Story | Acceptance criterion (scenario) |
|---|---|---|---|---|
| R-01 | Sign up with email+password; trimmed, case-insensitive, unique email | §3 | US-AUTH-1 | "Successful sign-up", "Email uniqueness is case-insensitive and trim-insensitive" |
| R-02 | Password ≥ 8 chars; never plaintext; Argon2id | §3, §11 | US-AUTH-1 | "Password too short rejected"; hash assertion in success scenario |
| R-03 | Send verification email via configurable SMTP (relay1.dataart.com) | §3 | US-AUTH-1, US-AUTH-3, E8 | "verification email is sent"; "SMTP supports relay1.dataart.com" |
| R-04 | Unverified account cannot use main app | §3 | US-AUTH-4, US-AUTH-6 | "Unverified account attempting login"; auth-gate scenarios |
| R-05 | Verification token 24h expiry, single-use | §3 | US-AUTH-2 | "Token is single-use", "Token expired after 24 hours" |
| R-06 | Success → login screen; no auto-login | §3 | US-AUTH-2 | "Verify with a valid token" (NOT auto logged in) |
| R-07 | Resend verification; new token invalidates earlier unused | §3 | US-AUTH-3 | "Resend issues a new token and invalidates earlier unused tokens" |
| R-08 | Auth required except sign-up/login/verify/resend; static+health public | §3 | US-AUTH-6 | "Unauthenticated access denied"; "Public endpoints accessible" |
| R-09 | Login/logout local credentials; no SSO | §3 | US-AUTH-4, US-AUTH-5 | login/logout scenarios |
| R-10 | Tickets grouped by team | §4, §6 | US-TICKET-1, US-BOARD-1 | "Create a valid ticket" (team), board per team |
| R-11 | View/create/rename/delete teams | §4 | US-TEAM-1/2/3/4 | respective scenarios |
| R-12 | Team fields: id, name, created, modified | §4 | US-TEAM-1 | "Create a uniquely named team" (timestamps) |
| R-13 | Team name non-empty trimmed; unique case-insensitive | §4 | US-TEAM-1/2 | "Reject empty/blank name", "Reject duplicate name" |
| R-14 | No delete team with tickets/epics → 409, no cascade, clear msg | §4, §9 | US-TEAM-3 | "Cannot delete a team that contains tickets/epics" |
| R-15 | No ownership/membership; all verified users manage all teams | §4 | US-TEAM-* (actor model §1.1) | all team scenarios run as any verified user |
| R-16 | Epic belongs to exactly one team | §5 | US-EPIC-1 | "Create epic with title and team" |
| R-17 | Team chosen at create; cannot change later | §5 | US-EPIC-2 | "Team cannot be changed" |
| R-18 | Separate epic CRUD screen | §5, §10 | US-EPIC-1/2/3/4 | screen-based scenarios |
| R-19 | Epic fields: id, team, title, optional desc, created, modified | §5 | US-EPIC-1 | "Create epic" (fields/timestamps) |
| R-20 | Epic title non-empty trimmed | §5 | US-EPIC-1/2 | "Reject empty title" |
| R-21 | Ticket may optionally reference one epic from its team's list | §5, §6 | US-TICKET-1, US-TICKET-4 | "Create a ticket with a same-team epic"; dropdown scoping |
| R-22 | Ticket epic must be same team — backend enforced | §5, §6 | US-TICKET-1/4 | "Reject epic from a different team"; "Backend rejects cross-team epic" |
| R-23 | No delete epic while referenced → 409, clear msg | §5, §9 | US-EPIC-3 | "Cannot delete an epic referenced by tickets" |
| R-24 | Ticket field: ID stable/unique | §6 | US-TICKET-1/2 | creation persists stable id |
| R-25 | Ticket field: Team required, existing | §6 | US-TICKET-1 | "Reject non-existent team" |
| R-26 | Ticket Type bug\|feature\|fix exact | §6 | US-TICKET-1/3 | "Reject invalid type" |
| R-27 | Ticket State 5 canonical values; UI labels with spaces | §6 | US-TICKET-1/3, US-BOARD-1 | "Reject invalid state"; "Five columns" |
| R-28 | Ticket Epic null or same-team ref | §6 | US-TICKET-1/4 | cross-team rejection scenarios |
| R-29 | Title required non-empty trimmed | §6 | US-TICKET-1/3 | "Reject empty title or body" |
| R-30 | Body required non-empty | §6 | US-TICKET-1/3 | "Reject empty title or body" |
| R-31 | created_at server UTC at creation | §6 | US-TICKET-1 | "created_at ... set in UTC" |
| R-32 | modified_at server UTC on field/state change; not on comment | §6, §7 | US-TICKET-3, US-COMMENT-1 | "Editing advances"; "Adding a comment does not change modified" |
| R-33 | created_by from authenticated user | §6 | US-TICKET-1 | "created_by is set to my authenticated user" |
| R-34 | Create/open/edit/delete tickets; view all fields | §6 | US-TICKET-1/2/3/5 | respective scenarios |
| R-35 | Saving unchanged values must not advance modified_at | §6 | US-TICKET-3 | "Saving with no changes does NOT advance modified_at" |
| R-36 | Team change clears/replaces epic (UI); backend rejects mismatch | §6 | US-TICKET-4 | "Changing team clears epic"; backend reject |
| R-37 | Delete ticket after confirm; cascade comments | §6 | US-TICKET-5 | "Delete with explicit confirmation" |
| R-38 | Drag-drop state persisted immediately | §6, §8 | US-BOARD-2 | "Successful drag persists immediately" |
| R-39 | Backend validates all enum values and references | §6 | US-TICKET-1/3, US-BOARD-2 | invalid-enum/reference scenarios |
| R-40 | Comments: add, fields, non-empty, oldest-first | §7 | US-COMMENT-1/2 | add + ordering scenarios |
| R-41 | Comment doesn't change ticket modified/board order | §7 | US-COMMENT-1, US-BOARD-3 | "does not change ticket modified time or board order" |
| R-42 | Comments immutable (mandatory) | §7 | US-COMMENT-3 | "No edit or delete in mandatory scope" |
| R-43 | Board = one team; team selector | §8 | US-BOARD-1 | "Switching teams reloads the board" |
| R-44 | Exactly 5 columns in workflow order | §8 | US-BOARD-1 | "Five columns in workflow order" |
| R-45 | Card shows ≥ title + type (epic recommended) | §8 | US-BOARD-1 | "Cards show required info" |
| R-46 | Drag changes state + persists via API | §8 | US-BOARD-2 | "Successful drag persists immediately" |
| R-47 | Drag failure → return to previous column + error | §8 | US-BOARD-2 | "Failed persistence rolls back" |
| R-48 | Any-to-any state moves allowed | §8 | US-BOARD-2 | "Non-sequential moves are allowed" |
| R-49 | Within column ordered by modified desc; no persistent manual order | §8 | US-BOARD-3 | "Most recently modified first"; "No persistent manual ordering" |
| R-50 | Clear way to create + open ticket from board | §8 | US-BOARD-6 | "Create a ticket from the board"; "Open an existing ticket" |
| R-51 | Filter by type + epic + title substring search; AND logic | §8 | US-BOARD-4 | filter/search/AND scenarios |
| R-52 | Usable with ≥ 100 tickets | §8 | US-BOARD-5 | "Board remains usable with at least 100 tickets" |
| R-53 | All CUD via API + persisted in RDBMS | §9 | E7 | "Persisted data survives refresh and restart" |
| R-54 | No local storage as system of record | §9 | E7 / NFR-REL-1 | refresh/restart persistence |
| R-55 | DB constraints / server validation for integrity | §9 | E7 | integrity in validation catalog |
| R-56 | Meaningful HTTP codes; 409 for delete conflicts | §9 | E7, US-TEAM-3, US-EPIC-3 | "Meaningful status codes" table |
| R-57 | IDs UUID/numeric; timestamps ISO-8601 UTC | §9 | E7 | "Timestamps are ISO-8601 UTC" |
| R-58 | Sessions/bearer OK; tokens not in URLs; verify token may be in URL | §9 | E7, US-AUTH-* | "Tokens are never placed in URLs" |
| R-59 | No concurrent-edit detection; last write wins | §9 | E7 | "Last write wins" |
| R-60 | Schema via migrations/repeatable init | §9 | E8 | migrations enable "Fresh database" scenario |
| R-61 | Fresh DB no application data; no seed on default startup | §9, §13 | E8 | "Fresh database has no application data" |
| R-62 | Minimum screens present | §10 | screen-bearing stories | per-screen scenarios (sign-up, verify, resend, login, board+selector, ticket view, teams, epics) |
| R-63 | Security NFRs | §11 | NFR-SEC-* | validation catalog + auth gate |
| R-64 | Reliability NFRs | §11 | NFR-REL-* | persistence + rollback |
| R-65 | Usability NFRs (loading/empty/success/error) | §11 | NFR-USE-* | empty/error-state scenarios |
| R-66 | Compatibility (Chrome/Edge/Firefox) | §11 | NFR-COMP-1 | manual/automated browser check |
| R-67 | Maintainability (README) | §11 | NFR-MNT-1, E8 | README presence |
| R-68 | Testing (1 backend + 1 FE/API flow) | §11 | NFR-TST-*, E8 | "Automated test coverage exists" |
| R-69 | docker compose up --build from root; clean OSes | §2 | E8 | "Start from a clean checkout" |
| R-70 | Three-tier separation; RDBMS container (e.g., PostgreSQL) | §1–2 | E8, NFR-MNT-2 | architecture acceptance |
| R-71 | No hard-coded password / committed secret | §13 | E8 | "No committed secrets" |
| R-72 | Minimum screens: verify-result with expired/invalid + resend | §10, §15 | US-AUTH-2/3 | "Token expired"/"Malformed token"/resend |

> **Coverage check:** Every numbered requirement R-01..R-72 maps to at least one story + acceptance scenario. Every story maps back to at least one source section. No mandatory-scope requirement is left without an acceptance criterion.

---

## 14. Definition of Done — mapping to stories

**Source:** §13. Each DoD checkbox is satisfied by the listed stories/criteria.

| DoD item (§13) | Satisfied by | Verifying scenarios |
|---|---|---|
| Sign up → receive verification email → verify → log in | US-AUTH-1, US-AUTH-2, US-AUTH-4, E8 (SMTP) | sign-up success; verify valid token; login success |
| Teams and epics managed via UI and persisted | US-TEAM-1/2/3/4, US-EPIC-1/2/3/4, E7 | CRUD scenarios + "Persisted data survives refresh" |
| Verified user can create/view/edit/delete tickets | US-TICKET-1/2/3/5 | create/view/edit/delete scenarios |
| Add comments; see author and timestamp | US-COMMENT-1/2 | "Post a non-empty comment"; "Comments listed oldest-first" (author+time shown) |
| Kanban shows tickets in correct state columns for selected team | US-BOARD-1 | "Five columns"; "Switching teams reloads the board" |
| Drag to another column updates server; correct after refresh | US-BOARD-2 | "Successful drag persists immediately" (incl. after-refresh assertion) |
| Start from clean checkout with docker compose up --build at root | E8 | "Start from a clean checkout" |
| No hard-coded user password or committed secret | E8, NFR-SEC-4 | "No committed secrets or hard-coded passwords" |
| Fresh DB: schema + migration metadata only; no preloaded data | E7, E8 | "Fresh database has no application data" |
| QA can create all test/demo data via UI or API (no manual DB edits) | E7 (API contract) + all CRUD stories | CRUD via API/UI; no seed path |

> **DoD completeness note:** All 10 DoD items map to mandatory-scope stories. The "QA can create all required test/demo data" item is satisfied because every entity (team, epic, ticket, comment, user via sign-up) has a create path through UI/API, and no seed/manual-DB step is required (§9, §13).

---

## 15. Screen ↔ story map (§10, §15 wireframes)

| Screen (§10) | Wireframe | Primary stories |
|---|---|---|
| Sign-up | WF2 (Create account) | US-AUTH-1 |
| Email verification result | WF2 (success + expired/invalid) | US-AUTH-2, US-AUTH-3 |
| Verification-email resend action | WF2 (login + result) | US-AUTH-3 |
| Login | WF2 (Log in) | US-AUTH-4, US-AUTH-5 (logout via header) |
| Kanban board + team selector | WF1 | US-BOARD-1..6 |
| Ticket create/edit/details + comments | WF3 | US-TICKET-1/2/3/4/5, US-COMMENT-1/2/3 |
| Team management | WF4 | US-TEAM-1/2/3/4 |
| Epic management | WF5 | US-EPIC-1/2/3/4 |

---

## A. Consolidated assumptions register

| ID | Assumption | Basis / why | Confidence | Owner to confirm |
|---|---|---|---|---|
| A1 | Unverified accounts get no usable business session at all | §3 (cannot use main app) | High | BA→Arch |
| A2 | UI state labels derive from canonical values; columns/badges UPPERCASE per WF | §6, WF1 | High | Design |
| A3 | Generic login failure messaging (no enumeration) | §11 security | Medium | Arch |
| A4 | Scoped "unverified" hint after correct creds, to enable resend | WF2 | Medium | Arch |
| A5 | No complexity rules beyond ≥8; sane max length to protect Argon2id | §3, §11 | High | Arch |
| A6 | Store normalized lowercase email as unique key | §3 | High | Arch |
| A7 | UI language English | wireframes | High | Design |
| A8 | Resend responses non-committal (anti-enumeration); rate-limit recommended | §3, §11 | Medium | Arch |
| A9 | Team modified_at advances on team-entity changes only | §4 (symmetry w/ §6) | Medium | Arch |
| A10 | No-op rename does not advance modified_at | §6 analogy | Medium | Arch |
| A11 | Epic titles need not be unique | §5 (only non-empty) | High | BA |
| A12 | Epic description optional/nullable; sane DB limit | §5 | High | Arch |
| A13 | Epic edit changes title/desc only; team read-only | §5, WF5 | High | Arch |
| A14 | Epic save-unchanged ⇒ no modified_at advance | §6 analogy | Medium | Arch |
| A15 | New ticket defaults to state `new` | workflow start | Medium | Arch |
| A16 | created_by/created_at immutable, display-only | WF3 | High | Arch |
| A17 | No mandated max title/body length; apply pragmatic DB limits | §6 | Medium | Arch |
| A18 | `TCK-####` is a display key; not a required format | WF3 | Medium | Design |
| A19 | "Actual change" determined via normalized value comparison | §6 | High | Arch |
| A20 | Comment author = authenticated user, server-set | §7, WF3 | High | Arch |
| A21 | Comment created_at server UTC, ISO-8601 | §7, §9 | High | Arch |
| A22 | Column sort = modified_at desc | §8 | High | Arch |
| A23 | Column count badges + total count reflect FILTERED set | §8, WF1 | Medium | Design |
| A24 | Search matches title substring only; AND across filters | §8 | High | BA |
| A25 | Board requires selected team; distinct empty states | §8, WF1 | High | Design |
| A26 | Relative modified time derived from UTC modified_at | WF1, WF3 | High | Design |
| A27 | 400/401/404 conventional; only 409 mandated explicitly; dup-name→409 (decision point) | §9 | Medium | Arch |
| A28 | Referential integrity enforced at DB + server | §9 | High | Arch |
| A29 | RDBMS = PostgreSQL (source steer) | §2 | Medium | Arch |
| A30 | Config (DB/SMTP/secret/verify base URL) via env, not committed | §11, §3 | High | Arch |
| A31 | Token expiry boundary: strictly older than 24h ⇒ invalid | §3 | Medium | Arch |
| A32 | Light rate-limiting on resend/login (recommended, not mandated) | §11 | Low | Arch |

---

## B. Open risks / decision points for the architect

1. **Status-code policy** for validation-style conflicts (duplicate team name): 409 vs 422/400 — pick one and apply consistently (A27).
2. **Login enumeration vs. UX**: how explicitly to reveal "unverified" state (A3/A4) — security vs. usability trade-off.
3. **modified_at no-op semantics**: exact normalization rules for "unchanged" detection across all entity types (A10/A14/A19) must be uniform to be testable.
4. **Verification link base URL** must be environment-configurable so emailed links resolve in QA (A30); otherwise DoD "verify the account" can fail in QA env.
5. **Filtered counts** (A23) — confirm with design that badges/total reflect post-filter counts.
6. **Default ticket state** (A15) — confirm `new` as creation default.
7. **Token storage** — store only a hash of the verification token (recommended) so DB compromise doesn't leak live tokens; source is silent but this aligns with §11.
8. **Field length limits** (A5/A12/A17) — define concrete DB bounds to prevent abuse while honoring "no mandatory max length."

---

*End of analysis. This document is intended as the authoritative BA input for architecture and QA test design. Where the source is silent, assumptions (§A) and decision points (§B) are flagged explicitly rather than left implicit.*
