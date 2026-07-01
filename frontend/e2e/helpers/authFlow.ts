// Shared signup -> verify -> login flow for E2E specs against the live Docker stack + Mailpit.
//
// Both the happy-path and the notifications spec need to bring a real account all the way to an
// authenticated session (the two-actor notification fan-out needs a SECOND verified user). This helper
// factors the exact UI steps the happy-path proved out, so a spec can onboard N users deterministically.
//
// It drives the real components (SignupPage / VerifyEmailPage / LoginPage) and reads the verification
// link from Mailpit. It does NOT clear Mailpit (the caller owns inbox lifecycle) and it filters the
// inbox by recipient, so onboarding several users against one shared inbox is safe.

import { expect, type Page, type APIRequestContext } from '@playwright/test';
import { waitForVerificationLink } from './mailpit';

export interface E2eAccount {
  email: string;
  password: string;
}

/** Sign up a fresh account (no auto-login; verification required). Leaves the page on the success banner. */
export async function signUp(page: Page, account: E2eAccount): Promise<void> {
  await page.goto('/signup');
  await page.locator('#signup-email').fill(account.email);
  await page.locator('#signup-password').fill(account.password);
  await page.locator('#signup-confirm').fill(account.password);
  await page.getByRole('button', { name: 'Sign up' }).click();
  await expect(page.locator('.banner-success')).toBeVisible();
}

/** Read the verification link from Mailpit and open it (staying on the app origin), landing on /login. */
export async function verifyViaMailpit(
  page: Page,
  request: APIRequestContext,
  email: string,
): Promise<void> {
  const link = await waitForVerificationLink(request, email);
  const url = new URL(link);
  await page.goto(`${url.pathname}${url.search}`);
  await expect(page.getByRole('heading', { name: 'Email verification' })).toBeVisible();
  await expect(page.locator('.banner-success')).toBeVisible();
  await page.getByRole('link', { name: 'Continue to login' }).click();
  await expect(page).toHaveURL(/\/login$/);
}

/** Log in an already-verified account; asserts the redirect to the board. */
export async function logIn(page: Page, account: E2eAccount): Promise<void> {
  // If a previous session is active, start from /login explicitly.
  await page.goto('/login');
  await page.locator('#login-email').fill(account.email);
  await page.locator('#login-password').fill(account.password);
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page).toHaveURL(/\/board/);
  await expect(page.getByRole('navigation').getByRole('link', { name: 'Board' })).toBeVisible();
}

/** signup -> verify -> login in one call. Leaves the page authenticated on the board. */
export async function signUpVerifyLogin(
  page: Page,
  request: APIRequestContext,
  account: E2eAccount,
): Promise<void> {
  await signUp(page, account);
  await verifyViaMailpit(page, request, account.email);
  await logIn(page, account);
}

/** Log out via the user menu so the same browser context can onboard/login a different account. */
export async function logOut(page: Page): Promise<void> {
  // The log-out control is a menuitem inside the user-menu popover (AppLayout.tsx); open it first.
  await page.locator('.user-menu-trigger').click();
  await page.getByRole('menuitem', { name: 'Log out' }).click();
  await expect(page).toHaveURL(/\/login$/);
}
