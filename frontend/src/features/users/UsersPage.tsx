// Admin "Users" management screen (API_CONTRACT §8, USER_MANAGEMENT_DESIGN concept). Admin-only —
// reached only via the RequireAdmin route guard. Lists every user with role, teams, verification,
// status and timestamps, and offers create / edit / reset-password / block-unblock actions.

import { useMemo, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { adminUsersApi } from '@/api/endpoints';
import type { AdminUser } from '@/api/types';
import { useUsers, usersQueryKey } from './useUsers';
import { useTeams } from '@/features/teams/useTeams';
import { formatUtc } from '@/lib/time';
import { displayName } from '@/lib/displayName';
import { errorMessage } from '@/lib/errors';
import { LoadingState, ErrorState, EmptyState } from '@/components/States';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { useToast } from '@/components/toast/ToastContext';
import { CreateUserDialog } from './CreateUserDialog';
import { EditUserDialog } from './EditUserDialog';
import { ResetPasswordDialog } from './ResetPasswordDialog';
import { UsersFilterBar, EMPTY_USERS_FILTERS, type UsersFilters } from './UsersFilterBar';
import { filterUsers } from './usersFilter';

function StatusBadge({ status }: { status: AdminUser['status'] }) {
  const { t } = useTranslation('users');
  const cls =
    status === 'blocked' ? 'badge type-bug' : status === 'unverified' ? 'badge type-feature' : 'badge type-fix';
  const label =
    status === 'blocked'
      ? t('status.blocked')
      : status === 'unverified'
        ? t('status.unverified')
        : t('status.active');
  return <span className={cls}>{label}</span>;
}

export function UsersPage() {
  const { t } = useTranslation('users');
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
  const [filters, setFilters] = useState<UsersFilters>(EMPTY_USERS_FILTERS);

  // Client-side AND filtering over the loaded list (Feature 2). Recomputed when users or filters change.
  const filteredUsers = useMemo(() => filterUsers(users, filters), [users, filters]);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: usersQueryKey });

  const blockMutation = useMutation({
    mutationFn: (user: AdminUser) =>
      user.isBlocked ? adminUsersApi.unblock(user.id) : adminUsersApi.block(user.id),
    onSuccess: (_result, user) => {
      toast.showSuccess(user.isBlocked ? t('toast.userUnblocked') : t('toast.userBlocked'));
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
        <h1>{t('page.title')}</h1>
        <div className="spacer" />
        <button type="button" className="btn btn-primary" onClick={() => setShowCreate(true)}>
          {t('page.createUser')}
        </button>
      </div>
      <p className="page-note">{t('page.note')}</p>

      {usersQuery.isLoading ? (
        <LoadingState label={t('page.loading')} />
      ) : usersQuery.isError ? (
        <ErrorState message={errorMessage(usersQuery.error)} onRetry={() => usersQuery.refetch()} />
      ) : users.length === 0 ? (
        <EmptyState title={t('page.empty.title')} message={t('page.empty.message')} />
      ) : (
        <>
          <UsersFilterBar
            filters={filters}
            teams={teams}
            matchCount={filteredUsers.length}
            onChange={setFilters}
            onClear={() => setFilters(EMPTY_USERS_FILTERS)}
          />

          {filteredUsers.length === 0 ? (
            <EmptyState
              title={t('page.noMatches.title')}
              message={t('page.noMatches.message')}
            />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>{t('table.user')}</th>
                  <th>{t('table.role')}</th>
                  <th>{t('table.teams')}</th>
                  <th>{t('table.verified')}</th>
                  <th>{t('table.status')}</th>
                  <th>{t('table.created')}</th>
                  <th className="text-right">{t('table.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {filteredUsers.map((user) => (
                  <tr key={user.id}>
                    <td>
                      <div className="user-cell">
                        <span className="user-cell-name">{displayName(user.name, user.email)}</span>
                        {user.name ? <span className="user-cell-email muted">{user.email}</span> : null}
                      </div>
                    </td>
                    <td>{user.isAdmin ? t('role.admin') : t('role.member')}</td>
                <td>
                  {user.isAdmin ? (
                    <span className="muted">{t('table.allTeams')}</span>
                  ) : user.teams.length === 0 ? (
                    <span className="muted">{t('table.noTeams')}</span>
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
                <td>{user.emailVerified ? t('table.yes') : t('table.no')}</td>
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
                      {t('actions.edit')}
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={user.isBlocked}
                      title={user.isBlocked ? t('resetPasswordDisabledTitle') : undefined}
                      onClick={() => setResetTarget(user)}
                    >
                      {t('actions.resetPassword')}
                    </button>
                    <button
                      type="button"
                      className={`btn btn-sm ${user.isBlocked ? 'btn-secondary' : 'btn-danger'}`}
                      onClick={() => setBlockTarget(user)}
                    >
                      {user.isBlocked ? t('actions.unblock') : t('actions.block')}
                    </button>
                  </div>
                </td>
              </tr>
            ))}
              </tbody>
            </table>
          )}
        </>
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
        title={blockTarget?.isBlocked ? t('confirm.unblockTitle') : t('confirm.blockTitle')}
        message={
          blockTarget?.isBlocked ? (
            <Trans t={t} i18nKey="confirm.unblockMessage" values={{ email: blockTarget?.email }}>
              Unblock <strong>{blockTarget?.email}</strong>? They will be able to log in
              again.
            </Trans>
          ) : (
            <Trans t={t} i18nKey="confirm.blockMessage" values={{ email: blockTarget?.email }}>
              Block <strong>{blockTarget?.email}</strong>? They will be signed out and
              unable to log in until unblocked.
            </Trans>
          )
        }
        confirmLabel={blockTarget?.isBlocked ? t('actions.unblock') : t('actions.block')}
        danger={!blockTarget?.isBlocked}
        busy={blockMutation.isPending}
        onConfirm={() => blockTarget && blockMutation.mutate(blockTarget)}
        onCancel={() => setBlockTarget(null)}
      />
    </div>
  );
}
