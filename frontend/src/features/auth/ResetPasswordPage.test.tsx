// F-01 ResetPasswordPage (public consume screen). The emailed link is
// /reset-password?token=<token>; the page reads the token from the query string. Verifies: a missing
// token shows the "request a new link" state (no form); client validation for min-length and
// confirm-mismatch (no request); a successful POST shows the success banner + Continue-to-login; and a
// 400 invalid_or_expired_token surfaces the error state with a "request a new link" affordance.
//
// NOTE (env constraint): authored to the existing Vitest + RTL + MSW patterns but NOT executed in this
// QA pass — no Node.js runtime is available on the QA machine (see the QA report's honest gaps).

import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { ResetPasswordPage } from './ResetPasswordPage';
import { renderWithProviders } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';

const VALID = '/reset-password?token=raw-token-123';

describe('ResetPasswordPage', () => {
  it('shows the "request a new link" state when the URL has no token', () => {
    renderWithProviders(<ResetPasswordPage />, { initialEntries: ['/reset-password'] });
    expect(screen.getByText(/No reset token was found/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /request a new link/i })).toHaveAttribute(
      'href',
      '/forgot-password',
    );
    // No password form is rendered.
    expect(screen.queryByLabelText('New password')).not.toBeInTheDocument();
  });

  it('renders the password + confirm fields when a token is present', () => {
    renderWithProviders(<ResetPasswordPage />, { initialEntries: [VALID] });
    expect(screen.getByLabelText('New password')).toBeInTheDocument();
    expect(screen.getByLabelText('Confirm new password')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reset password' })).toBeInTheDocument();
  });

  it('blocks submit with a too-short password and does NOT call the API', async () => {
    let called = false;
    server.use(
      http.post(`${API}/auth/reset-password`, () => {
        called = true;
        return HttpResponse.json({ message: 'should not happen' }, { status: 200 });
      }),
    );
    const { user } = renderWithProviders(<ResetPasswordPage />, { initialEntries: [VALID] });

    await user.type(screen.getByLabelText('New password'), 'short');
    await user.type(screen.getByLabelText('Confirm new password'), 'short');
    await user.click(screen.getByRole('button', { name: 'Reset password' }));

    expect(await screen.findByText(/at least 8 characters/i)).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it('blocks submit when confirm does not match', async () => {
    const { user } = renderWithProviders(<ResetPasswordPage />, { initialEntries: [VALID] });

    await user.type(screen.getByLabelText('New password'), 'a valid password');
    await user.type(screen.getByLabelText('Confirm new password'), 'a different password');
    await user.click(screen.getByRole('button', { name: 'Reset password' }));

    expect(await screen.findByText(/passwords do not match/i)).toBeInTheDocument();
  });

  it('on success shows the success banner and a Continue-to-login link', async () => {
    const { user } = renderWithProviders(<ResetPasswordPage />, { initialEntries: [VALID] });

    await user.type(screen.getByLabelText('New password'), 'brand new password');
    await user.type(screen.getByLabelText('Confirm new password'), 'brand new password');
    await user.click(screen.getByRole('button', { name: 'Reset password' }));

    expect(await screen.findByText(/your password has been reset/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /continue to login/i })).toHaveAttribute('href', '/login');
    // The form is gone after success.
    expect(screen.queryByRole('button', { name: 'Reset password' })).not.toBeInTheDocument();
  });

  it('surfaces a 400 invalid_or_expired_token as an error, keeping the form for a retry', async () => {
    server.use(
      http.post(`${API}/auth/reset-password`, () =>
        HttpResponse.json(
          {
            error: {
              code: 'invalid_or_expired_token',
              message: 'This password reset link is invalid or has expired. Request a new one.',
              errors: null,
            },
          },
          { status: 400 },
        ),
      ),
    );
    const { user } = renderWithProviders(<ResetPasswordPage />, { initialEntries: [VALID] });

    await user.type(screen.getByLabelText('New password'), 'brand new password');
    await user.type(screen.getByLabelText('Confirm new password'), 'brand new password');
    await user.click(screen.getByRole('button', { name: 'Reset password' }));

    expect(await screen.findByText(/invalid or has expired/i)).toBeInTheDocument();
    // A "request a new link" affordance is available from the form footer.
    expect(screen.getByRole('link', { name: /request a new link/i })).toHaveAttribute(
      'href',
      '/forgot-password',
    );
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Reset password' })).toBeInTheDocument(),
    );
  });
});
