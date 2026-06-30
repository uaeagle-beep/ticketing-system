import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { SignupPage } from './SignupPage';
import { renderWithProviders } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';

describe('SignupPage', () => {
  it('shows a min-length error and does NOT submit when the password is shorter than 8', async () => {
    let signupCalled = false;
    server.use(
      http.post(`${API}/auth/signup`, () => {
        signupCalled = true;
        return HttpResponse.json({ message: 'should not happen' }, { status: 201 });
      }),
    );

    const { user } = renderWithProviders(<SignupPage />);

    await user.type(screen.getByLabelText('Email'), 'alex@dataart.com');
    await user.type(screen.getByLabelText('Password'), 'short');
    await user.type(screen.getByLabelText('Confirm password'), 'short');
    await user.click(screen.getByRole('button', { name: 'Sign up' }));

    expect(
      await screen.findByText('Password must be at least 8 characters.'),
    ).toBeInTheDocument();
    expect(signupCalled).toBe(false);
  });

  it('shows a mismatch error when confirm does not match', async () => {
    const { user } = renderWithProviders(<SignupPage />);

    await user.type(screen.getByLabelText('Email'), 'alex@dataart.com');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.type(screen.getByLabelText('Confirm password'), 'different value!!');
    await user.click(screen.getByRole('button', { name: 'Sign up' }));

    expect(await screen.findByText('Passwords do not match.')).toBeInTheDocument();
  });

  it('on success shows the verification banner with a Continue-to-login link and hides the form', async () => {
    const { user } = renderWithProviders(<SignupPage />);

    await user.type(screen.getByLabelText('Email'), 'alex@dataart.com');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.type(screen.getByLabelText('Confirm password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Sign up' }));

    // Banner uses the server's "verification required" message.
    expect(
      await screen.findByText(/check your email to verify your account/i),
    ).toBeInTheDocument();
    // Continue link points at /login.
    const continueLink = screen.getByRole('link', { name: /continue to login/i });
    expect(continueLink).toHaveAttribute('href', '/login');
    // The form is gone after success (Sign up button no longer rendered).
    expect(screen.queryByRole('button', { name: 'Sign up' })).not.toBeInTheDocument();
  });

  it('surfaces a server validation error as an error banner without leaving success state', async () => {
    server.use(
      http.post(`${API}/auth/signup`, () =>
        HttpResponse.json(
          {
            error: {
              code: 'validation_error',
              message: 'Invalid email.',
              errors: { email: ['Email is not valid.'] },
            },
          },
          { status: 400 },
        ),
      ),
    );

    const { user } = renderWithProviders(<SignupPage />);

    await user.type(screen.getByLabelText('Email'), 'bad-email');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.type(screen.getByLabelText('Confirm password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Sign up' }));

    // errorMessage prefers the per-field validation text.
    expect(await screen.findByText('Email is not valid.')).toBeInTheDocument();
    // Form still present (no success banner).
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Sign up' })).toBeInTheDocument(),
    );
  });
});
