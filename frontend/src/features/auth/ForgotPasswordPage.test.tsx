// F-01 ForgotPasswordPage (public request screen). Verifies: the email field + submit render; client
// validation blocks an empty email (no request); a successful POST shows the non-committal success
// banner (anti-enumeration — identical regardless of account existence) and hides the form; a server
// error surfaces as an error banner without leaving the form; the success state offers a Back-to-login
// link.
//
// NOTE (env constraint): authored to the existing Vitest + RTL + MSW patterns but NOT executed in this
// QA pass — no Node.js runtime is available on the QA machine (see the QA report's honest gaps).

import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { ForgotPasswordPage } from './ForgotPasswordPage';
import { renderWithProviders } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';

describe('ForgotPasswordPage', () => {
  it('renders the email field and submit button', () => {
    renderWithProviders(<ForgotPasswordPage />);
    expect(screen.getByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send reset link' })).toBeInTheDocument();
  });

  it('blocks submit and does NOT call the API when the email is empty', async () => {
    let called = false;
    server.use(
      http.post(`${API}/auth/forgot-password`, () => {
        called = true;
        return HttpResponse.json({ message: 'should not happen' }, { status: 202 });
      }),
    );
    const { user } = renderWithProviders(<ForgotPasswordPage />);

    await user.click(screen.getByRole('button', { name: 'Send reset link' }));

    expect(await screen.findByText('Please enter your email address.')).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it('shows the non-committal success banner and hides the form on success', async () => {
    const { user } = renderWithProviders(<ForgotPasswordPage />);

    await user.type(screen.getByLabelText('Email'), 'alex@dataart.com');
    await user.click(screen.getByRole('button', { name: 'Send reset link' }));

    expect(
      await screen.findByText(/a password reset link has been sent/i),
    ).toBeInTheDocument();
    // Form is gone; a Back-to-login link is offered.
    expect(screen.queryByRole('button', { name: 'Send reset link' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to login/i })).toHaveAttribute('href', '/login');
  });

  it('shows the SAME success message for an unknown email (non-enumeration)', async () => {
    // The backend returns an identical 202 for unknown emails; the UI must not reveal existence.
    const { user } = renderWithProviders(<ForgotPasswordPage />);

    await user.type(screen.getByLabelText('Email'), 'nobody@dataart.com');
    await user.click(screen.getByRole('button', { name: 'Send reset link' }));

    expect(
      await screen.findByText(/a password reset link has been sent/i),
    ).toBeInTheDocument();
  });

  it('surfaces a server error as an error banner without entering the success state', async () => {
    server.use(
      http.post(`${API}/auth/forgot-password`, () =>
        HttpResponse.json(
          { error: { code: 'service_unavailable', message: 'Down.', errors: null } },
          { status: 503 },
        ),
      ),
    );
    const { user } = renderWithProviders(<ForgotPasswordPage />);

    await user.type(screen.getByLabelText('Email'), 'alex@dataart.com');
    await user.click(screen.getByRole('button', { name: 'Send reset link' }));

    expect(await screen.findByText(/server is unavailable/i)).toBeInTheDocument();
    // Still on the form (success banner not shown).
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Send reset link' })).toBeInTheDocument(),
    );
  });
});
