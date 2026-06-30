// Admin "Users" management screen (API_CONTRACT §8, USER_MANAGEMENT_DESIGN concept). Admin-only —
// reached only via the RequireAdmin route guard. Lists every user with role, teams, verification,
// status and timestamps, and offers create / edit / reset-password / block-unblock actions.

import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { adminUsersApi } from '@/api/endpoints';
import type { AdminUser } from '@/api/types';
import { useUsers, usersQueryKey } from './useUsers';
import { useTeams } from '@/features/teams/useTeams';
import { formatUtc } from '@/lib/time';
import { errorMessage } from '@/lib/errors';
import { LoadingState, ErrorState, EmptyState } from '@/components/States';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { useToast } from '@/components/toast/ToastContext';
import { CreateUserDialog } from './CreateUserDialog';
import { EditUserDialog } from './EditUserDialog';
import { ResetPasswordDialog } from './ResetPasswordDialog';

function StatusBadge({ status }: { status: AdminUser['status'] }) {
  const cls =
    status === 'blocked' ? 'badge type-bug' : status === 'unverified' ? 'badge type-feature' : 'badge type-fix';
  const label = status === 'blocked' ? 'Blocked' : status === 'unverified' ? 'Unverified' : 'Active';
  return <span className={cls}>{label}</span>;
}

export function UsersPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const usersQuery = useUsers();
  const teamsQuery = useTeams();
  const users = usersQuery.data ?? [];
  const teams = teamsQuery.data ?? [];

  const [showCreate, setShowCreate] = useState(false);
  const [editTarget, setEditTarget] = useState<AdminUser | null>(null);
  const [resetTarget, setResetTarget] = useState<AdminUser | null>(null);
  const [blockTarget, setBlockTarget] = useState<AdminUser | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: usersQueryKey });

  const blockMutation = useMutation({
    mutationFn: (user: AdminUser) =>
      user.isBlocked ? adminUsersApi.unblock(user.id) : adminUsersApi.block(user.id),
    onSuccess: (_result, user) => {
      toast.showSuccess(user.isBlocked ? 'User unblocked.' : 'User blocked.');
      setBlockTarget(null);
      invalidate();
    },
    onError: (err) => {
      setBlockTarget(null);
      toast.showError(errorMessage(err));
    },
  });

  return (
    <div className="page-container">
      <div className="page-header">
        <h1>Users</h1>
        <div className="spacer" />
        <button type="button" className="btn btn-primary" onClick={() => setShowCreate(true)}>
          + Create user
        </button>
      </div>
      <p className="page-note">
        Admins manage all accounts here. Members only see and act within their assigned teams.
      </p>

      {usersQuery.isLoading ? (
        <LoadingState label="Loading users…" />
      ) : usersQuery.isError ? (
        <ErrorState message={errorMessage(usersQuery.error)} onRetry={() => usersQuery.refetch()} />
      ) : users.length === 0 ? (
        <EmptyState title="No users" message="Create the first user to get started." />
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Email</th>
              <th>Role</th>
              <th>Teams</th>
              <th>Verified</th>
              <th>Status</th>
              <th>Created</th>
              <th className="text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            {users.map((user) => (
              <tr key={user.id}>
                <td>{user.email}</td>
                <td>{user.isAdmin ? 'Admin' : 'Member'}</td>
                <td>
                  {user.isAdmin ? (
                    <span className="muted">All teams</span>
                  ) : user.teams.length === 0 ? (
                    <span className="muted">—</span>
                  ) : (
                    <div className="chip-list">
                      {user.teams.map((team) => (
                        <span key={team.id} className="badge badge-count">
                          {team.name}
                        </span>
                      ))}
                    </div>
                  )}
                </td>
                <td>{user.emailVerified ? 'Yes' : 'No'}</td>
                <td>
                  <StatusBadge status={user.status} />
                </td>
                <td className="nowrap muted">{formatUtc(user.createdAt)}</td>
                <td>
                  <div className="table-actions">
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      onClick={() => setEditTarget(user)}
                    >
                      Edit
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={user.isBlocked}
                      title={user.isBlocked ? 'Unblock the account before resetting its password.' : undefined}
                      onClick={() => setResetTarget(user)}
                    >
                      Reset password
                    </button>
                    <button
                      type="button"
                      className={`btn btn-sm ${user.isBlocked ? 'btn-secondary' : 'btn-danger'}`}
                      onClick={() => setBlockTarget(user)}
                    >
                      {user.isBlocked ? 'Unblock' : 'Block'}
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {showCreate ? <CreateUserDialog teams={teams} onClose={() => setShowCreate(false)} /> : null}
      {editTarget ? (
        <EditUserDialog user={editTarget} teams={teams} onClose={() => setEditTarget(null)} />
      ) : null}
      {resetTarget ? (
        <ResetPasswordDialog user={resetTarget} onClose={() => setResetTarget(null)} />
      ) : null}

      <ConfirmDialog
        open={blockTarget !== null}
        title={blockTarget?.isBlocked ? 'Unblock user?' : 'Block user?'}
        message={
          blockTarget?.isBlocked ? (
            <>
              Unblock <strong>{blockTarget?.email}</strong>? They will be able to log in again.
            </>
          ) : (
            <>
              Block <strong>{blockTarget?.email}</strong>? They will be signed out and unable to log
              in until unblocked.
            </>
          )
        }
        confirmLabel={blockTarget?.isBlocked ? 'Unblock' : 'Block'}
        danger={!blockTarget?.isBlocked}
        busy={blockMutation.isPending}
        onConfirm={() => blockTarget && blockMutation.mutate(blockTarget)}
        onCancel={() => setBlockTarget(null)}
      />
    </div>
  );
}
