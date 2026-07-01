// Two-actor notification fan-out E2E against the live Docker stack + Mailpit.
//
// The notification fan-out only notifies WATCHERS other than the actor (ADR-0013): a user is auto-watched
// on the ticket they create, and a DIFFERENT user commenting on it must produce an in-app notification for
// the creator (never for the commenter). This spec proves that end to end with two real, verified accounts:
//
//   User A: signup -> verify -> login -> promote admin -> create team -> create ticket (A auto-watches).
//   log out.
//   User B: signup -> verify -> login -> promote admin (admin sees all teams) -> open A's ticket -> comment.
//   log out.
//   User A: log back in -> the bell unread count increases -> the notification panel lists "B commented"
//           -> clicking it navigates to the ticket.
//
// Determinism: we POLL the bell's unread count (the in-app path is instant, but a poll/expect-with-timeout
// absorbs the fan-out + query-refetch latency). We assert the IN-APP indicator, NOT email — the email digest
// is a time-based background worker (ADR-0014) and would be flaky to await here.
//
// It is split out of happy-path.spec.ts because it needs a second onboarded actor; both specs run under the
// same Playwright project and serial config, and CI's `npm run e2e` picks up every *.spec.ts in e2e/.
//
// PREREQS (same as happy-path): the E2E stack must be up so verification mail lands in Mailpit:
//   docker compose -f docker-compose.yml -f docker-compose.e2e.yml up --build -d

import { test, expect, type Page } from '@playwright/test';
import { clearMailpit } from './helpers/mailpit';
import { promoteToAdmin } from './helpers/adminBootstrap';
import { signUpVerifyLogin, logIn, logOut, type E2eAccount } from './helpers/authFlow';

const RUN_ID = Date.now().toString(36);
const PASSWORD = 'correct horse battery staple';
const USER_A: E2eAccount = { email: `e2e-notif-a-${RUN_ID}@example.com`, password: PASSWORD };
const USER_B: E2eAccount = { email: `e2e-notif-b-${RUN_ID}@example.com`, password: PASSWORD };
const TEAM_NAME = `E2E Notif Team ${RUN_ID}`;
const TICKET_TITLE = `E2E notif ticket ${RUN_ID}`;
const TICKET_BODY = 'A watched ticket; a second actor will comment to trigger a notification.';
const COMMENT_BODY = `E2E notif comment ${RUN_ID}`;

test.describe.configure({ mode: 'serial' });

test('watcher gets an in-app notification when a second actor comments', async ({ page, request }) => {
  // ---- 0. Clean inbox so both users' verification mails are ours to read. ----
  await clearMailpit(request);

  // ---- 1. User A onboards and becomes admin (team/ticket CRUD is admin-only, ADR-0007). ----
  await signUpVerifyLogin(page, request, USER_A);
  promoteToAdmin(USER_A.email);
  await page.reload();
  await expect(page.getByRole('navigation').getByRole('link', { name: 'Users' })).toBeVisible();

  // ---- 2. User A creates a team. ----
  await page.getByRole('navigation').getByRole('link', { name: 'Teams' }).click();
  await expect(page.getByRole('heading', { name: 'Teams', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '+ Create team' }).first().click();
  await page.getByPlaceholder('Team name').fill(TEAM_NAME);
  await page.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(page.getByRole('row', { name: new RegExp(escapeRegExp(TEAM_NAME)) })).toBeVisible();

  // ---- 3. User A creates a ticket (creator is auto-watched, ADR-0013). Capture its id. ----
  await page.getByRole('navigation').getByRole('link', { name: 'Board' }).click();
  await expect(page).toHaveURL(/\/board/);
  await page.getByLabel('Select team').selectOption({ label: TEAM_NAME });
  await page.getByRole('button', { name: '+ New ticket' }).first().click();
  await expect(page).toHaveURL(/\/tickets\/new/);
  await page.getByLabel('Team', { exact: true }).selectOption({ label: TEAM_NAME });
  await page.locator('#ticket-title').fill(TICKET_TITLE);
  await page.locator('#ticket-body').fill(TICKET_BODY);
  await page.getByRole('button', { name: 'Create ticket' }).click();
  await expect(page).toHaveURL(/\/tickets\/[0-9a-fA-F-]{8,}/);
  const ticketId = ticketIdFromUrl(page.url());
  await expect(page.getByRole('heading', { name: TICKET_TITLE })).toBeVisible();

  // ---- 4. Log out A. ----
  await logOut(page);

  // ---- 5. User B onboards and becomes admin (so B can see A's team — admin sees all teams). ----
  await signUpVerifyLogin(page, request, USER_B);
  promoteToAdmin(USER_B.email);
  await page.reload();
  await expect(page.getByRole('navigation').getByRole('link', { name: 'Users' })).toBeVisible();

  // ---- 6. User B opens A's ticket directly and comments (the fan-out notifies watcher A, not actor B). ----
  await page.goto(`/tickets/${ticketId}`);
  await expect(page.getByRole('heading', { name: TICKET_TITLE })).toBeVisible();
  await page.locator('#new-comment').fill(COMMENT_BODY);
  await page.getByRole('button', { name: 'Post comment' }).click();
  await expect(page.getByText(COMMENT_BODY)).toBeVisible();

  // B is the actor, so B must NOT be notified — the bell stays at zero for B (no unread badge).
  await expect(page.locator('.notif-badge')).toHaveCount(0);

  // ---- 7. Log out B. ----
  await logOut(page);

  // ---- 8. User A logs back in; the in-app notification arrives. ----
  await logIn(page, USER_A);

  // Poll the bell for a non-zero unread count. The bell aria-label carries the count
  // ("Notifications ({count} unread)"), and the visible badge appears only when unread > 0.
  await expect(page.locator('.notif-badge')).toBeVisible();
  await expect(bell(page)).toHaveAttribute('aria-label', /\d+ unread/);

  // ---- 9. Open the notifications panel; the fan-out entry is listed ("B commented"). ----
  await bell(page).click();
  await expect(page).toHaveURL(/\/notifications/);
  const list = page.getByRole('list', { name: 'Notifications' });
  await expect(list).toBeVisible();
  // The comment summary is "{actor} commented" (EventSummaries.CommentAdded); actor B's display name is
  // its email (no name set). Match the resilient "commented" verb and an unread marker.
  const notif = list.locator('.notification-item', { hasText: 'commented' }).first();
  await expect(notif).toBeVisible();
  await expect(notif).toHaveClass(/unread/);

  // ---- 10. Clicking the notification navigates to the watched ticket. ----
  await notif.getByRole('button').click();
  await expect(page).toHaveURL(new RegExp(`/tickets/${ticketId}`));
  await expect(page.getByRole('heading', { name: TICKET_TITLE })).toBeVisible();
});

// ---- helpers ----

function bell(page: Page) {
  // The bell is the only .notif-bell button (AppLayout.tsx); its aria-label carries the unread count.
  return page.locator('.notif-bell');
}

function ticketIdFromUrl(currentUrl: string): string {
  const m = new URL(currentUrl).pathname.match(/\/tickets\/([^/]+)$/);
  if (!m || !m[1]) throw new Error(`Could not parse ticket id from URL: ${currentUrl}`);
  return m[1];
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
