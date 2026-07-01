// F-04 AccountPage (authenticated self-service). Two sections:
//  - Profile: PUT /api/me/profile sets/clears the display name; the request body sends null to clear;
//    a >100-char name is a client-side field error (no request); a validation_error from the server is
//    shown as a field error.
//  - Change password: POST /api/me/password with current-password re-auth. Client validation for
//    min-length and confirm-mismatch (no request). A wrong current password (401 invalid_credentials)
//    maps to a "current password is incorrect" field error; a success clears the fields.
//
// The email is shown read-only. The tree is rendered authenticated (seedAuthToken + a seeded /me).
//
// NOTE (env constraint): authored to the existing Vitest + RTL + MSW patterns but NOT executed in this
// QA pass — no Node.js runtime is available on the QA machine (see the QA report's honest gaps).

import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { AccountPage } from './AccountPage';
import { renderWithProviders, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleUser } from '@/test/handlers';

function seedMe(overrides: Partial<typeof sampleUser> = {}) {
  server.use(
    http.get(`${API}/auth/me`, () =>
      HttpResponse.json({ ...sampleUser, ...overrides }, { status: 200 }),
    ),
  );
}

async function renderAccount(overrides: Partial<typeof sampleUser> = {}) {
  seedAuthToken('t');
  seedMe(overrides);
  const result = renderWithProviders(<AccountPage />);
  // Wait until the authenticated identity has loaded (email is shown read-only).
  await screen.findByDisplayValue(overrides.email ?? sampleUser.email);
  return result;
}

describe('AccountPage — profile', () => {
  it('shows the account email read-only', async () => {
    await renderAccount({ email: 'alex@dataart.com' });
    const emailInput = screen.getByLabelText('Email') as HTMLInputElement;
    expect(emailInput.value).toBe('alex@dataart.com');
    expect(emailInput).toBeDisabled();
  });

  it('sends the trimmed name on save', async () => {
    let sent: unknown;
    server.use(
      http.put(`${API}/me/profile`, async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ ...sampleUser, name: 'Alex Doe' }, { status: 200 });
      }),
    );
    const { user } = await renderAccount();

    await user.type(screen.getByLabelText('Display name'), '  Alex Doe  ');
    await user.click(screen.getByRole('button', { name: 'Save profile' }));

    await waitFor(() => expect(sent).toEqual({ name: 'Alex Doe' }));
  });

  it('sends null to clear the name when the field is blank', async () => {
    let sent: unknown;
    server.use(
      http.put(`${API}/me/profile`, async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ ...sampleUser, name: null }, { status: 200 });
      }),
    );
    const { user } = await renderAccount({ name: 'Existing Name' });

    await user.clear(screen.getByLabelText('Display name'));
    await user.click(screen.getByRole('button', { name: 'Save profile' }));

    await waitFor(() => expect(sent).toEqual({ name: null }));
  });

  it('shows a validation field error from the server (e.g. name too long)', async () => {
    server.use(
      http.put(`${API}/me/profile`, () =>
        HttpResponse.json(
          {
            error: {
              code: 'validation_error',
              message: 'Invalid.',
              errors: { name: ['Name must be at most 100 characters.'] },
            },
          },
          { status: 400 },
        ),
      ),
    );
    const { user } = await renderAccount();

    await user.type(screen.getByLabelText('Display name'), 'Some Name');
    await user.click(screen.getByRole('button', { name: 'Save profile' }));

    expect(await screen.findByText('Name must be at most 100 characters.')).toBeInTheDocument();
  });
});

describe('AccountPage — change password', () => {
  it('blocks submit with a too-short new password and does NOT call the API', async () => {
    let called = false;
    server.use(
      http.post(`${API}/me/password`, () => {
        called = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = await renderAccount();

    await user.type(screen.getByLabelText('Current password'), 'correct horse battery');
    await user.type(screen.getByLabelText('New password'), 'short');
    await user.type(screen.getByLabelText('Confirm new password'), 'short');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText(/at least 8 characters/i)).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it('blocks submit when the new passwords do not match', async () => {
    const { user } = await renderAccount();

    await user.type(screen.getByLabelText('Current password'), 'correct horse battery');
    await user.type(screen.getByLabelText('New password'), 'a valid new password');
    await user.type(screen.getByLabelText('Confirm new password'), 'a different password');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText(/new passwords do not match/i)).toBeInTheDocument();
  });

  it('maps a 401 invalid_credentials to a "current password is incorrect" field error', async () => {
    server.use(
      http.post(`${API}/me/password`, () =>
        HttpResponse.json(
          { error: { code: 'invalid_credentials', message: 'nope', errors: null } },
          { status: 401 },
        ),
      ),
    );
    const { user } = await renderAccount();

    await user.type(screen.getByLabelText('Current password'), 'wrong password');
    await user.type(screen.getByLabelText('New password'), 'a valid new password');
    await user.type(screen.getByLabelText('Confirm new password'), 'a valid new password');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText('The current password is incorrect.')).toBeInTheDocument();
  });

  it('clears the password fields on a successful change', async () => {
    const { user } = await renderAccount();

    const current = screen.getByLabelText('Current password') as HTMLInputElement;
    const next = screen.getByLabelText('New password') as HTMLInputElement;
    const confirm = screen.getByLabelText('Confirm new password') as HTMLInputElement;

    await user.type(current, 'correct horse battery');
    await user.type(next, 'a valid new password');
    await user.type(confirm, 'a valid new password');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    await waitFor(() => expect(current.value).toBe(''));
    expect(next.value).toBe('');
    expect(confirm.value).toBe('');
  });
});
