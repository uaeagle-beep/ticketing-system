import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { UsersPage } from './UsersPage';
import { renderWithProviders } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleAdminUser, sampleMemberUser, sampleTeam } from '@/test/handlers';
import type { AdminUser } from '@/api/types';

// A second team + a roster spanning every filterable dimension (role, team, verified, status, name).
const payments = { id: 'team-payments', name: 'Payments' };

function makeUser(overrides: Partial<AdminUser>): AdminUser {
  return {
    id: crypto.randomUUID(),
    email: 'someone@dataart.com',
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

const ada = makeUser({
  email: 'ada@dataart.com',
  name: 'Ada Lovelace',
  isAdmin: true,
});
const grace = makeUser({
  email: 'grace@dataart.com',
  name: 'Grace Hopper',
  teams: [{ id: sampleTeam.id, name: sampleTeam.name }],
});
const linus = makeUser({
  email: 'linus@dataart.com',
  name: null,
  teams: [payments],
});
const blockedUser = makeUser({
  email: 'blocked@dataart.com',
  name: 'Blocked Person',
  isBlocked: true,
  status: 'blocked',
});
const pending = makeUser({
  email: 'pending@dataart.com',
  name: null,
  emailVerified: false,
  status: 'unverified',
});

/** Override GET /admin/users (and /teams) with the rich roster for filtering tests. */
function seedRoster() {
  server.use(
    http.get(`${API}/admin/users`, () =>
      HttpResponse.json([ada, grace, linus, blockedUser, pending], { status: 200 }),
    ),
    http.get(`${API}/teams`, () =>
      HttpResponse.json(
        [sampleTeam, { ...sampleTeam, id: payments.id, name: payments.name }],
        { status: 200 },
      ),
    ),
  );
}

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

  // ---- Display name in the list (Feature 1) ----

  describe('display name', () => {
    it('shows the name as primary with the email beneath when a name is set', async () => {
      seedRoster();
      renderWithProviders(<UsersPage />);

      // Ada has a name → both her name and her email appear in her row.
      const row = (await screen.findByText('Ada Lovelace')).closest('tr')!;
      expect(within(row).getByText('Ada Lovelace')).toBeInTheDocument();
      expect(within(row).getByText(ada.email)).toBeInTheDocument();
    });

    it('shows the email as the display value when no name is set', async () => {
      seedRoster();
      renderWithProviders(<UsersPage />);

      // Linus has no name → his email is the only identity shown, and it appears exactly once.
      const row = (await screen.findByText(linus.email)).closest('tr')!;
      expect(within(row).getAllByText(linus.email)).toHaveLength(1);
    });
  });

  // ---- Client-side filtering (Feature 2) ----

  describe('filtering', () => {
    it('searches by name OR email (case-insensitive)', async () => {
      seedRoster();
      const { user } = renderWithProviders(<UsersPage />);
      await screen.findByText('Ada Lovelace');

      const search = screen.getByRole('searchbox', { name: 'Search by name or email' });

      // By name.
      await user.type(search, 'grace');
      expect(screen.getByText('Grace Hopper')).toBeInTheDocument();
      expect(screen.queryByText('Ada Lovelace')).not.toBeInTheDocument();

      // By email (clear then type an email fragment of a user with no name).
      await user.clear(search);
      await user.type(search, 'linus@');
      expect(screen.getByText(linus.email)).toBeInTheDocument();
      expect(screen.queryByText('Grace Hopper')).not.toBeInTheDocument();
    });

    it('filters by role', async () => {
      seedRoster();
      const { user } = renderWithProviders(<UsersPage />);
      await screen.findByText('Ada Lovelace');

      await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by role' }), 'admin');
      expect(screen.getByText('Ada Lovelace')).toBeInTheDocument();
      expect(screen.queryByText('Grace Hopper')).not.toBeInTheDocument();
    });

    it('filters by team membership', async () => {
      seedRoster();
      const { user } = renderWithProviders(<UsersPage />);
      await screen.findByText('Ada Lovelace');

      await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by team' }), payments.id);
      expect(screen.getByText(linus.email)).toBeInTheDocument(); // linus ∈ Payments
      expect(screen.queryByText('Grace Hopper')).not.toBeInTheDocument(); // grace ∈ Platform
    });

    it('filters by email verification', async () => {
      seedRoster();
      const { user } = renderWithProviders(<UsersPage />);
      await screen.findByText('Ada Lovelace');

      await user.selectOptions(
        screen.getByRole('combobox', { name: 'Filter by email verification' }),
        'unverified',
      );
      expect(screen.getByText(pending.email)).toBeInTheDocument();
      expect(screen.queryByText('Ada Lovelace')).not.toBeInTheDocument();
    });

    it('filters by status', async () => {
      seedRoster();
      const { user } = renderWithProviders(<UsersPage />);
      await screen.findByText('Ada Lovelace');

      await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by status' }), 'blocked');
      expect(screen.getByText('Blocked Person')).toBeInTheDocument();
      expect(screen.queryByText('Ada Lovelace')).not.toBeInTheDocument();
    });

    it('combines filters with AND logic', async () => {
      seedRoster();
      const { user } = renderWithProviders(<UsersPage />);
      await screen.findByText('Ada Lovelace');

      // member + verified + Platform team => only Grace (Ada is admin, Linus is on Payments).
      await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by role' }), 'member');
      await user.selectOptions(
        screen.getByRole('combobox', { name: 'Filter by email verification' }),
        'verified',
      );
      await user.selectOptions(
        screen.getByRole('combobox', { name: 'Filter by team' }),
        sampleTeam.id,
      );

      expect(screen.getByText('Grace Hopper')).toBeInTheDocument();
      expect(screen.queryByText('Ada Lovelace')).not.toBeInTheDocument();
      expect(screen.queryByText(linus.email)).not.toBeInTheDocument();
      expect(screen.getByText('1 user')).toBeInTheDocument();
    });

    it('shows a no-matches empty state and Clear resets the filters', async () => {
      seedRoster();
      const { user } = renderWithProviders(<UsersPage />);
      await screen.findByText('Ada Lovelace');

      await user.type(
        screen.getByRole('searchbox', { name: 'Search by name or email' }),
        'no-such-user',
      );
      expect(await screen.findByText('No matching users')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Clear' }));
      expect(await screen.findByText('Ada Lovelace')).toBeInTheDocument();
      expect(screen.queryByText('No matching users')).not.toBeInTheDocument();
    });
  });
});
