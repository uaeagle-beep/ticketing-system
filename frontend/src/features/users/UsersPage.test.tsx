import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { UsersPage } from './UsersPage';
import { renderWithProviders } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleAdminUser, sampleMemberUser } from '@/test/handlers';

// Foundation tests for the admin Users zone (API_CONTRACT §8). The exhaustive admin-flow matrix is
// the tester's to extend; these prove the list renders and the core actions call the right routes.

describe('UsersPage', () => {
  it('lists users with role, teams, status and a Created column', async () => {
    renderWithProviders(<UsersPage />);

    expect(await screen.findByText(sampleAdminUser.email)).toBeInTheDocument();
    const memberRow = (await screen.findByText(sampleMemberUser.email)).closest('tr')!;
    const row = within(memberRow);
    expect(row.getByText('Member')).toBeInTheDocument();
    expect(row.getByText('Platform')).toBeInTheDocument(); // team chip
    expect(row.getByText('Active')).toBeInTheDocument(); // status badge

    // The admin row shows "All teams" rather than chips.
    const adminRow = within(screen.getByText(sampleAdminUser.email).closest('tr')!);
    expect(adminRow.getByText('All teams')).toBeInTheDocument();
  });

  it('opens the create-user dialog and shows the generated password once on success', async () => {
    const { user } = renderWithProviders(<UsersPage />);

    await user.click(await screen.findByRole('button', { name: '+ Create user' }));

    const dialog = await screen.findByRole('dialog', { name: 'Create user' });
    await user.type(within(dialog).getByLabelText('Email'), 'newdev@dataart.com');
    // Auto-generate is checked by default → password field hidden.
    await user.click(within(dialog).getByRole('button', { name: 'Create user' }));

    // Default handler returns a generatedPassword → the one-time password screen appears.
    expect(await screen.findByTestId('generated-password')).toHaveTextContent('Xk9$mPq2vLr7Wn4t');
  });

  it('reset-password is disabled for a blocked user', async () => {
    server.use(
      http.get(`${API}/admin/users`, () =>
        HttpResponse.json(
          [{ ...sampleMemberUser, isBlocked: true, status: 'blocked' }],
          { status: 200 },
        ),
      ),
    );

    renderWithProviders(<UsersPage />);

    const row = (await screen.findByText(sampleMemberUser.email)).closest('tr')!;
    const resetBtn = within(row).getByRole('button', { name: 'Reset password' });
    expect(resetBtn).toBeDisabled();
    // And a blocked user offers "Unblock" rather than "Block".
    expect(within(row).getByRole('button', { name: 'Unblock' })).toBeInTheDocument();
  });

  it('blocking a user confirms then calls the block endpoint', async () => {
    let blockCalled = false;
    server.use(
      http.post(`${API}/admin/users/:id/block`, () => {
        blockCalled = true;
        return HttpResponse.json(
          { ...sampleMemberUser, isBlocked: true, status: 'blocked' },
          { status: 200 },
        );
      }),
    );

    const { user } = renderWithProviders(<UsersPage />);
    const row = (await screen.findByText(sampleMemberUser.email)).closest('tr')!;
    await user.click(within(row).getByRole('button', { name: 'Block' }));

    // Confirmation dialog → confirm.
    const confirm = await screen.findByRole('dialog', { name: 'Block user?' });
    await user.click(within(confirm).getByRole('button', { name: 'Block' }));

    await waitFor(() => expect(blockCalled).toBe(true));
  });
});
