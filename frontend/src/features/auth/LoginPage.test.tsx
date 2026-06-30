import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { LoginPage } from './LoginPage';
import { renderWithProviders } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';

describe('LoginPage', () => {
  it('renders email and password fields and the log-in button', () => {
    renderWithProviders(<LoginPage />);
    expect(screen.getByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Log in' })).toBeInTheDocument();
  });

  it('offers a "Resend email" link/button that reveals the resend form', async () => {
    const { user } = renderWithProviders(<LoginPage />);

    // The footer affordance is a button (btn-link), not an <a>.
    await user.click(screen.getByRole('button', { name: /account not verified\? resend email/i }));

    expect(
      await screen.findByRole('button', { name: 'Resend verification email' }),
    ).toBeInTheDocument();
  });

  it('shows a client-side error and skips the request when fields are empty', async () => {
    let loginCalled = false;
    server.use(
      http.post(`${API}/auth/login`, () => {
        loginCalled = true;
        return HttpResponse.json({}, { status: 200 });
      }),
    );

    const { user } = renderWithProviders(<LoginPage />);
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(
      await screen.findByText('Please enter your email and password.'),
    ).toBeInTheDocument();
    expect(loginCalled).toBe(false);
  });

  it('on a 403 unverified login surfaces the verification hint and the resend form', async () => {
    server.use(
      http.post(`${API}/auth/login`, () =>
        HttpResponse.json(
          {
            error: {
              code: 'account_not_verified',
              message:
                'Your account is not verified. Check your email or request a new verification link.',
            },
          },
          { status: 403 },
        ),
      ),
    );

    const { user } = renderWithProviders(<LoginPage />);
    await user.type(screen.getByLabelText('Email'), 'alex@dataart.com');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    // Friendly error banner from errorMessage(account_not_verified).
    expect(
      await screen.findByText(
        'Your account is not verified. Check your email or request a new verification link.',
      ),
    ).toBeInTheDocument();
    // The inline resend affordance appears.
    expect(
      screen.getByText('Your account is not verified. Request a new verification email below.'),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Resend verification email' }),
    ).toBeInTheDocument();
  });

  it('shows the anti-enumeration message on invalid credentials (401)', async () => {
    server.use(
      http.post(`${API}/auth/login`, () =>
        HttpResponse.json(
          { error: { code: 'invalid_credentials', message: 'nope' } },
          { status: 401 },
        ),
      ),
    );

    const { user } = renderWithProviders(<LoginPage />);
    await user.type(screen.getByLabelText('Email'), 'alex@dataart.com');
    await user.type(screen.getByLabelText('Password'), 'wrong-password');
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument();
  });

  it('shows a clear blocked message on account_blocked (401) and does not offer resend', async () => {
    server.use(
      http.post(`${API}/auth/login`, () =>
        HttpResponse.json(
          { error: { code: 'account_blocked', message: 'srv' } },
          { status: 401 },
        ),
      ),
    );

    const { user } = renderWithProviders(<LoginPage />);
    await user.type(screen.getByLabelText('Email'), 'blocked@dataart.com');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(
      await screen.findByText('This account has been blocked. Contact an administrator.'),
    ).toBeInTheDocument();
    // A blocked account must NOT see the verification-resend affordance.
    expect(
      screen.queryByText('Your account is not verified. Request a new verification email below.'),
    ).not.toBeInTheDocument();
  });
});
