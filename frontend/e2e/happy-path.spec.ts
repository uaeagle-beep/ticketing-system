// Full happy-path E2E against the live Docker stack + Mailpit.
//
// Flow (single sequential test — each step depends on the previous):
//   signup -> read verification link from Mailpit -> verify -> login ->
//   create team -> create epic -> create ticket -> open ticket -> add comment ->
//   back to board -> drag card to a new column -> reload -> assert it persisted.
//
// PREREQS: the stack must be running with the E2E override so verification mail
// is captured by Mailpit:
//   docker compose -f docker-compose.yml -f docker-compose.e2e.yml up --build -d
//
// Every selector/label below is matched against the real components:
//   SignupPage / VerifyEmailPage / LoginPage / AppLayout / TeamsPage /
//   EpicsPage / BoardPage / TicketPage / CommentsPanel / BoardColumn / TicketCard.

import { test, expect, type Page, type Locator } from '@playwright/test';
import {
  clearMailpit,
  waitForVerificationLink,
} from './helpers/mailpit';
import { promoteToAdmin } from './helpers/adminBootstrap';

// Human column labels — kept identical to src/lib/labels.ts STATE_LABELS so the
// region (aria-label) lookups match BoardColumn.tsx exactly. Inlined (rather than
// imported from ../src/lib/labels) so the spec doesn't depend on the Vite `@`
// path alias resolving under Playwright's transpiler.
type TicketState =
  | 'new'
  | 'ready_for_implementation'
  | 'in_progress'
  | 'ready_for_acceptance'
  | 'done';

const STATE_LABELS: Record<TicketState, string> = {
  new: 'New',
  ready_for_implementation: 'Ready for implementation',
  in_progress: 'In progress',
  ready_for_acceptance: 'Ready for acceptance',
  done: 'Done',
};

function stateLabel(state: TicketState): string {
  return STATE_LABELS[state];
}

// Unique per run so reruns against a non-reset DB don't collide on the
// case-insensitive unique team name (API_CONTRACT §4.2).
const RUN_ID = Date.now().toString(36);
const EMAIL = `e2e-${RUN_ID}@example.com`;
const PASSWORD = 'correct horse battery staple'; // >= 8 chars
const TEAM_NAME = `E2E Team ${RUN_ID}`;
const EPIC_TITLE = `E2E Epic ${RUN_ID}`;
const TICKET_TITLE = `E2E ticket ${RUN_ID}`;
const TICKET_BODY = 'Steps to reproduce: open the app and follow the happy path.';
const COMMENT_BODY = `E2E comment ${RUN_ID}`;

test.describe.configure({ mode: 'serial' });

test('signup -> verify -> login -> team -> epic -> ticket -> comment -> drag persists', async ({
  page,
  request,
}) => {
  // ---- 0. Start from a clean Mailpit inbox so we read OUR verification mail. ----
  await clearMailpit(request);

  // ---- 1. Sign up (no auto-login; verification required). ----
  await page.goto('/signup');
  await page.locator('#signup-email').fill(EMAIL);
  await page.locator('#signup-password').fill(PASSWORD);
  await page.locator('#signup-confirm').fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign up' }).click();

  // Success banner from SignupPage.tsx + the "Continue to login" link.
  await expect(page.locator('.banner-success')).toBeVisible();
  await expect(page.getByRole('link', { name: /Continue to login/ })).toBeVisible();

  // ---- 2. Read the verification link from Mailpit and open it. ----
  const verificationLink = await waitForVerificationLink(request, EMAIL);
  // Navigate using the path+query only, so we stay on Playwright's baseURL
  // (FRONTEND_URL in the link equals the app origin, but this is robust either way).
  const url = new URL(verificationLink);
  await page.goto(`${url.pathname}${url.search}`);

  // VerifyEmailPage success state.
  await expect(page.getByRole('heading', { name: 'Email verification' })).toBeVisible();
  await expect(page.locator('.banner-success')).toBeVisible();
  await page.getByRole('link', { name: 'Continue to login' }).click();
  await expect(page).toHaveURL(/\/login$/);

  // ---- 3. Log in -> redirected to the board. ----
  await page.locator('#login-email').fill(EMAIL);
  await page.locator('#login-password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page).toHaveURL(/\/board/);
  // AppLayout header confirms an authenticated session.
  await expect(page.getByRole('navigation').getByRole('link', { name: 'Board' })).toBeVisible();

  // ---- 3b. Promote this account to admin. ----
  // Under the User-Management authz model (ADR-0007) team/epic CRUD is admin-only,
  // and a self-registered account is a member with no team. A fresh e2e DB has no
  // admin to grant the role through the UI, so flip is_admin directly in the e2e
  // Postgres, then reload: the SPA refetches /me (isAdmin is read fresh per request)
  // and the admin UI appears — including the "Users" nav and "+ Create team".
  promoteToAdmin(EMAIL);
  await page.reload();
  await expect(page.getByRole('navigation').getByRole('link', { name: 'Users' })).toBeVisible();

  // ---- 4. Create a team (now an admin). ----
  // Go to Team management via the nav. This is robust whether or not teams already
  // exist: an admin sees ALL teams, so the board's "No teams yet" empty state (and
  // its "Go to Team management" button) may be absent. Match the "Teams" heading
  // exactly so it doesn't also resolve the "No teams yet" empty-state heading.
  await page.getByRole('navigation').getByRole('link', { name: 'Teams' }).click();
  await expect(page).toHaveURL(/\/teams/);
  await expect(page.getByRole('heading', { name: 'Teams', exact: true })).toBeVisible();

  await page.getByRole('button', { name: '+ Create team' }).first().click();
  await page.getByPlaceholder('Team name').fill(TEAM_NAME);
  await page.getByRole('button', { name: 'Create', exact: true }).click();

  // The new team appears as a row in the teams table (counts 0/0).
  const teamRow = page.getByRole('row', { name: new RegExp(escapeRegExp(TEAM_NAME)) });
  await expect(teamRow).toBeVisible();

  // ---- 5. Create an epic for that team. ----
  await page.getByRole('navigation').getByRole('link', { name: 'Epics' }).click();
  await expect(page).toHaveURL(/\/epics/);
  // The team selector defaults to the first team; select ours explicitly.
  await page.getByLabel('Select team').selectOption({ label: TEAM_NAME });

  await page.getByRole('button', { name: '+ Create epic' }).first().click();
  await page.locator('#epic-title').fill(EPIC_TITLE);
  await page.getByRole('button', { name: 'Create', exact: true }).click();

  // Epic shows up in the epics table for the selected team.
  await expect(page.getByRole('cell', { name: EPIC_TITLE, exact: true })).toBeVisible();

  // ---- 6. Create a ticket (via the board's "+ New ticket"). ----
  await page.getByRole('navigation').getByRole('link', { name: 'Board' }).click();
  await expect(page).toHaveURL(/\/board/);
  await page.getByLabel('Select team').selectOption({ label: TEAM_NAME });

  await page.getByRole('button', { name: '+ New ticket' }).first().click();
  await expect(page).toHaveURL(/\/tickets\/new/);
  await expect(page.getByRole('heading', { name: 'New ticket' })).toBeVisible();

  // Team is prefilled from ?team=; set the rest of the fields. Use exact label
  // matches so "Team"/"Epic" don't substring-match other controls.
  await page.getByLabel('Team', { exact: true }).selectOption({ label: TEAM_NAME });
  await page.getByLabel('Epic', { exact: true }).selectOption({ label: EPIC_TITLE });
  await page.locator('#ticket-title').fill(TICKET_TITLE);
  await page.locator('#ticket-body').fill(TICKET_BODY);
  await page.getByRole('button', { name: 'Create ticket' }).click();

  // On success the app navigates to /tickets/:id (edit/detail mode). Capture id.
  await expect(page).toHaveURL(/\/tickets\/[0-9a-fA-F-]{8,}/);
  const ticketId = ticketIdFromUrl(page.url());
  // The detail heading is the ticket title once loaded.
  await expect(page.getByRole('heading', { name: TICKET_TITLE })).toBeVisible();

  // ---- 7. Add a comment on the open ticket. ----
  await page.locator('#new-comment').fill(COMMENT_BODY);
  await page.getByRole('button', { name: 'Post comment' }).click();
  // Comment renders in the list (CommentsPanel); the textarea clears on success.
  await expect(page.getByText(COMMENT_BODY)).toBeVisible();
  await expect(page.locator('#new-comment')).toHaveValue('');

  // ---- 8. Back to the board. ----
  await page.getByRole('link', { name: /Back to board/ }).click();
  await expect(page).toHaveURL(/\/board/);
  await page.getByLabel('Select team').selectOption({ label: TEAM_NAME });

  // New tickets default to the "new" state, so the card starts in the New column.
  // (ticketId is captured above for traceability; cards are located by their
  // unique per-run accessible name.)
  void ticketId;
  await expect(boardCard(page)).toBeVisible();
  await expect(columnFor(page, 'new')).toContainText(TICKET_TITLE);

  // ---- 9. Drag the card New -> In progress and confirm it persists on reload. ----
  await dragCardToColumn(page, 'in_progress');

  // After the optimistic move + PATCH, the card should be in the target column.
  await expect(columnFor(page, 'in_progress')).toContainText(TICKET_TITLE);

  // Persistence check: reload and re-select the team; the move must survive.
  await page.reload();
  await page.getByLabel('Select team').selectOption({ label: TEAM_NAME });
  await expect(boardCard(page)).toBeVisible();
  await expect(columnFor(page, 'in_progress')).toContainText(TICKET_TITLE);
  // And it is no longer in the original column.
  await expect(columnFor(page, 'new')).not.toContainText(TICKET_TITLE);
});

// ---- helpers ----

function ticketIdFromUrl(currentUrl: string): string {
  const m = new URL(currentUrl).pathname.match(/\/tickets\/([^/]+)$/);
  if (!m || !m[1]) throw new Error(`Could not parse ticket id from URL: ${currentUrl}`);
  return m[1];
}

// The card body exposes aria-label "Open ticket: <title>" (TicketCard.tsx). The
// run-unique title makes this locator unambiguous across the board.
function boardCard(page: Page): Locator {
  return page.getByRole('button', { name: `Open ticket: ${TICKET_TITLE}` });
}

// A column <section> is labelled with the human state label (BoardColumn.tsx
// sets aria-label={stateLabel(state)}). e.g. "New", "In progress".
function columnFor(page: Page, state: TicketState): Locator {
  return page.getByRole('region', { name: stateLabel(state), exact: true });
}

// Drag a ticket card from its current column into the target column.
//
// The board uses @dnd-kit with a PointerSensor (activationConstraint distance:5),
// so a single mouse.move is ignored — we press, nudge past the threshold, hover
// the destination column in several steps (dnd-kit needs intermediate moves to
// register the droppable), then release. We aim at the destination column's
// header region so we don't accidentally land "between" cards.
async function dragCardToColumn(
  page: Page,
  targetState: TicketState,
): Promise<void> {
  // The dedicated drag handle carries dnd-kit's pointer listeners (TicketCard.tsx);
  // the card body is the "open" affordance and must NOT be used to drag.
  const handle = page.getByRole('button', {
    name: new RegExp(`^Move ticket: ${escapeRegExp(TICKET_TITLE)}`),
  });
  await expect(handle).toBeVisible();

  const target = columnFor(page, targetState);
  await expect(target).toBeVisible();

  const handleBox = await handle.boundingBox();
  const targetBox = await target.boundingBox();
  if (!handleBox || !targetBox) {
    throw new Error('Could not resolve bounding boxes for drag source/target.');
  }

  const startX = handleBox.x + handleBox.width / 2;
  const startY = handleBox.y + handleBox.height / 2;
  // Aim near the top of the destination column (its body / header area).
  const endX = targetBox.x + targetBox.width / 2;
  const endY = targetBox.y + 60;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  // Cross the 5px activation threshold first.
  await page.mouse.move(startX + 8, startY + 8, { steps: 5 });
  // Move toward the target in several steps so dnd-kit's collision detection and
  // onDragOver fire for the destination droppable.
  await page.mouse.move((startX + endX) / 2, (startY + endY) / 2, { steps: 10 });
  await page.mouse.move(endX, endY, { steps: 10 });
  // Settle on the target, then release.
  await page.mouse.move(endX, endY + 4, { steps: 3 });
  await page.mouse.up();
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
