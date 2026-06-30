// Smoke E2E — public pages only. These DO NOT require a verified account, a
// running Mailpit, or any seeded data; they exercise the SPA's public routes,
// client-side validation, navigation, and the verification-error screen.
//
// All selectors/text below are matched 1:1 against the real components:
//   - LoginPage.tsx        (heading "Log in", #login-email/#login-password)
//   - SignupPage.tsx       (heading "Create an account", #signup-* fields,
//                           client validation strings)
//   - VerifyEmailPage.tsx  (heading "Email verification"; invalid token ->
//                           banner-error + "Resend verification email" button)

import { test, expect } from '@playwright/test';

test.describe('public pages smoke', () => {
  test('login page renders its core fields and links', async ({ page }) => {
    await page.goto('/login');

    await expect(page.getByRole('heading', { name: 'Log in' })).toBeVisible();
    await expect(page.locator('#login-email')).toBeVisible();
    await expect(page.locator('#login-password')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Log in' })).toBeVisible();

    // The unverified-resend affordance and the create-account link are present.
    await expect(
      page.getByRole('button', { name: 'Account not verified? Resend email' }),
    ).toBeVisible();
    await expect(page.getByRole('link', { name: /Create an account/ })).toBeVisible();
  });

  test('signup page renders its core fields', async ({ page }) => {
    await page.goto('/signup');

    await expect(page.getByRole('heading', { name: 'Create an account' })).toBeVisible();
    await expect(page.locator('#signup-email')).toBeVisible();
    await expect(page.locator('#signup-password')).toBeVisible();
    await expect(page.locator('#signup-confirm')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign up' })).toBeVisible();
  });

  test('signup blocks a password shorter than 8 characters (client validation)', async ({
    page,
  }) => {
    await page.goto('/signup');

    await page.locator('#signup-email').fill('shortpw@example.com');
    await page.locator('#signup-password').fill('short'); // 5 chars < 8
    await page.locator('#signup-confirm').fill('short');
    await page.getByRole('button', { name: 'Sign up' }).click();

    // Inline field error from SignupPage.tsx; form stays on the signup screen.
    await expect(
      page.getByText('Password must be at least 8 characters.'),
    ).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Create an account' })).toBeVisible();
  });

  test('signup blocks mismatched passwords (client validation)', async ({ page }) => {
    await page.goto('/signup');

    await page.locator('#signup-email').fill('mismatch@example.com');
    await page.locator('#signup-password').fill('longenough1');
    await page.locator('#signup-confirm').fill('longenough2'); // differs
    await page.getByRole('button', { name: 'Sign up' }).click();

    await expect(page.getByText('Passwords do not match.')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Create an account' })).toBeVisible();
  });

  test('can navigate login <-> signup', async ({ page }) => {
    await page.goto('/login');

    await page.getByRole('link', { name: /Create an account/ }).click();
    await expect(page).toHaveURL(/\/signup$/);
    await expect(page.getByRole('heading', { name: 'Create an account' })).toBeVisible();

    await page.getByRole('link', { name: /Log in/ }).click();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole('heading', { name: 'Log in' })).toBeVisible();
  });

  test('verify-email with an invalid token shows an error and a resend action', async ({
    page,
  }) => {
    // A syntactically present but unknown token -> the SPA POSTs it, the API
    // returns 400 invalid_or_expired_token, and the page shows the error state.
    await page.goto('/verify-email?token=definitely-not-a-real-token');

    await expect(page.getByRole('heading', { name: 'Email verification' })).toBeVisible();

    // Error banner (message comes from the API; assert the resilient parts).
    const errorBanner = page.locator('.banner-error');
    await expect(errorBanner).toBeVisible();
    await expect(errorBanner).toContainText(/invalid|expired/i);

    // Resend affordance is offered; clicking it reveals the resend email form.
    const resendButton = page.getByRole('button', { name: 'Resend verification email' });
    await expect(resendButton).toBeVisible();
    await resendButton.click();
    await expect(page.locator('#resend-email')).toBeVisible();
  });
});
