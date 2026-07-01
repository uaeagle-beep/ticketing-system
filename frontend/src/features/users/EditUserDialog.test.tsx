import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { QueryClientProvider } from '@tanstack/react-query';
import { EditUserDialog } from './EditUserDialog';
import { ToastProvider } from '@/components/toast/ToastContext';
import { makeTestQueryClient } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleTeam } from '@/test/handlers';
import type { AdminUser, SetRoleRequest, SetTeamsRequest, SetNameRequest, Team } from '@/api/types';

// Direct-mount tests for the EditUserDialog (API_CONTRACT §8.3/§8.3.1/§8.4). Not covered by the
// UsersPage suite at all. The load-bearing case is the last-admin guard UI: a 409 last_admin_required
// on role change must surface a clear error AND revert the local admin toggle so the UI matches the
// rejected state. Also proves name set/clear and team-set replacement call the right endpoints only
// when changed, and that unchanged fields make NO call.

const platform: Team = { ...sampleTeam };
const payments: Team = { ...sampleTeam, id: 'team-payments', name: 'Payments' };

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
    teams: [{ id: platform.id, name: platform.name }],
    ...overrides,
  };
}

function mount(user: AdminUser, teams: Team[] = [platform, payments]) {
  const client = makeTestQueryClient();
  const ue = userEvent.setup();
  const onClose = vi.fn();
  render(
    <QueryClientProvider client={client}>
      <ToastProvider>
        <EditUserDialog user={user} teams={teams} onClose={onClose} />
      </ToastProvider>
    </QueryClientProvider>,
  );
  return { ue, onClose };
}

describe('EditUserDialog', () => {
  it('reverts the admin toggle and shows an error when demoting the last admin (409)', async () => {
    const admin = makeUser({ isAdmin: true, teams: [] });
    let roleCalled = false;
    server.use(
      http.put(`${API}/admin/users/:id/role`, () => {
        roleCalled = true;
        return HttpResponse.json(
          {
            error: {
              code: 'last_admin_required',
              message: 'The system must keep at least one active administrator.',
            },
          },
          { status: 409 },
        );
      }),
    );
    const { ue, onClose } = mount(admin);

    const toggle = screen.getByRole('checkbox', { name: /Administrator/ });
    expect(toggle).toBeChecked();
    await ue.click(toggle); // attempt demote
    expect(toggle).not.toBeChecked(); // local optimistic un-check

    await ue.click(screen.getByRole('button', { name: 'Save changes' }));

    // The backend rejects → error surfaced AND the toggle reverts to admin (matches rejected state).
    expect(await screen.findByText('The system must keep at least one active administrator.')).toBeInTheDocument();
    await waitFor(() => expect(toggle).toBeChecked());
    expect(roleCalled).toBe(true);
    expect(onClose).not.toHaveBeenCalled();
  });

  it('promotes a member: calls setRole with isAdmin true and closes on success', async () => {
    const member = makeUser({ isAdmin: false });
    let captured: SetRoleRequest | null = null;
    server.use(
      http.put(`${API}/admin/users/:id/role`, async ({ request }) => {
        captured = (await request.json()) as SetRoleRequest;
        return HttpResponse.json({ ...member, isAdmin: true }, { status: 200 });
      }),
    );
    const { ue, onClose } = mount(member);

    await ue.click(screen.getByRole('checkbox', { name: /Administrator/ }));
    await ue.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(captured).toEqual({ isAdmin: true });
  });

  it('replaces the team set only when membership changed', async () => {
    const member = makeUser({ isAdmin: false, teams: [{ id: platform.id, name: platform.name }] });
    let capturedTeams: SetTeamsRequest | null = null;
    server.use(
      http.put(`${API}/admin/users/:id/teams`, async ({ request }) => {
        capturedTeams = (await request.json()) as SetTeamsRequest;
        return HttpResponse.json(member, { status: 200 });
      }),
    );
    const { ue, onClose } = mount(member);

    // Add Payments (Platform already checked) → the set becomes {platform, payments}.
    const teamGroup = screen.getByRole('group', { name: 'Teams' });
    await ue.click(within(teamGroup).getByLabelText('Payments'));
    await ue.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(capturedTeams).not.toBeNull();
    expect(capturedTeams!.teamIds).toEqual(expect.arrayContaining([platform.id, payments.id]));
    expect(capturedTeams!.teamIds).toHaveLength(2);
  });

  it('sets a name via PUT /name when changed', async () => {
    const member = makeUser({ isAdmin: false, name: null });
    let capturedName: SetNameRequest | null = null;
    server.use(
      http.put(`${API}/admin/users/:id/name`, async ({ request }) => {
        capturedName = (await request.json()) as SetNameRequest;
        return HttpResponse.json({ ...member, name: 'Grace Hopper' }, { status: 200 });
      }),
    );
    const { ue, onClose } = mount(member);

    await ue.type(screen.getByLabelText('Name'), 'Grace Hopper');
    await ue.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(capturedName).toEqual({ name: 'Grace Hopper' });
  });

  it('clears a name to null when blanked out', async () => {
    const named = makeUser({ isAdmin: false, name: 'Ada Lovelace' });
    let capturedName: SetNameRequest | null = null;
    server.use(
      http.put(`${API}/admin/users/:id/name`, async ({ request }) => {
        capturedName = (await request.json()) as SetNameRequest;
        return HttpResponse.json({ ...named, name: null }, { status: 200 });
      }),
    );
    const { ue, onClose } = mount(named);

    await ue.clear(screen.getByLabelText('Name'));
    await ue.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(capturedName).toEqual({ name: null }); // blank → null (UI shows email)
  });

  it('makes no role/name/team calls when nothing changed', async () => {
    const member = makeUser({ isAdmin: false, name: 'Unchanged', teams: [{ id: platform.id, name: platform.name }] });
    let anyCall = false;
    server.use(
      http.put(`${API}/admin/users/:id/role`, () => { anyCall = true; return HttpResponse.json(member); }),
      http.put(`${API}/admin/users/:id/name`, () => { anyCall = true; return HttpResponse.json(member); }),
      http.put(`${API}/admin/users/:id/teams`, () => { anyCall = true; return HttpResponse.json(member); }),
    );
    const { ue, onClose } = mount(member);

    // Save with no edits → the dialog closes and issues no mutating calls (each save is guarded by a diff).
    await ue.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(anyCall).toBe(false);
  });
});
