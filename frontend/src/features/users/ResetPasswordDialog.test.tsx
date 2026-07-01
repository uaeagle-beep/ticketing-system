import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { QueryClientProvider } from '@tanstack/react-query';
import { ResetPasswordDialog } from './ResetPasswordDialog';
import { ToastProvider } from '@/components/toast/ToastContext';
import { makeTestQueryClient } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';
import type { AdminUser } from '@/api/types';

// Direct-mount tests for the ResetPasswordDialog (API_CONTRACT §8.7). Not covered by the UsersPage
// suite. Proves: the confirm → generate flow shows the new password ONCE, the pre-generate confirm
// UI hides the password, and a blocked-user 403 surfaces the refusal without revealing a password.

function makeUser(overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id: 'user-1',
    email: 'dev@dataart.com',
    name: null,
    isAdmin: false,
    isBlocked: false,
    emailVerified: true,
    status: 'active',
    createdAt: '2026-06-20T08:00:00Z',
    teams: [],
    ...overrides,
  };
}

function mount(user: AdminUser) {
  const client = makeTestQueryClient();
  const ue = userEvent.setup();
  const onClose = vi.fn();
  render(
    <QueryClientProvider client={client}>
      <ToastProvider>
        <ResetPasswordDialog user={user} onClose={onClose} />
      </ToastProvider>
    </QueryClientProvider>,
  );
  return { ue, onClose };
}

describe('ResetPasswordDialog', () => {
  it('confirms then shows the generated password exactly once', async () => {
    server.use(
      http.post(`${API}/admin/users/:id/reset-password`, () =>
        HttpResponse.json({ generatedPassword: 'Nw7&pQz3xKr9Vm2t' }, { status: 200 }),
      ),
    );
    const { ue } = mount(makeUser());

    // Before confirming, no password is shown — just the confirm copy.
    expect(screen.queryByTestId('generated-password')).not.toBeInTheDocument();
    expect(screen.getByText(/shown only once/i)).toBeInTheDocument();

    await ue.click(screen.getByRole('button', { name: 'Generate new password' }));

    // After confirming, the one-time password appears once.
    const shown = await screen.findByTestId('generated-password');
    expect(shown).toHaveTextContent('Nw7&pQz3xKr9Vm2t');
    // The generate button is replaced by Done (no way to regenerate/re-show).
    expect(screen.queryByRole('button', { name: 'Generate new password' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Done' })).toBeInTheDocument();
  });

  it('surfaces a 403 forbidden for a blocked user and shows no password', async () => {
    server.use(
      http.post(`${API}/admin/users/:id/reset-password`, () =>
        HttpResponse.json(
          { error: { code: 'forbidden', message: 'Unblock the account before resetting its password.' } },
          { status: 403 },
        ),
      ),
    );
    const { ue, onClose } = mount(makeUser({ isBlocked: true, status: 'blocked' }));

    await ue.click(screen.getByRole('button', { name: 'Generate new password' }));

    // The friendly forbidden message is toasted; no password is ever revealed; dialog stays open.
    expect(await screen.findByText('You do not have permission to perform this action.')).toBeInTheDocument();
    expect(screen.queryByTestId('generated-password')).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('closes without a network call when cancelled', async () => {
    let called = false;
    server.use(
      http.post(`${API}/admin/users/:id/reset-password`, () => {
        called = true;
        return HttpResponse.json({ generatedPassword: 'x' }, { status: 200 });
      }),
    );
    const { ue, onClose } = mount(makeUser());

    await ue.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(called).toBe(false);
  });
});
