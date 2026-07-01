// Team management screen (Wireframe 4, US-TEAM-*).
//
// Table: Name, Tickets, Epics, Modified, Actions (Edit / Delete).
// - Create via an inline form ("+ Create team").
// - Rename inline (the row's name becomes an editable input).
// - Delete is DISABLED while the team has tickets or epics, with an explanatory
//   hint (the backend is still authoritative and returns 409 — surfaced as a toast).

import { Fragment, useState, type FormEvent } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { teamsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { Team, TicketState } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { useTeams } from './useTeams';
import { formatUtc } from '@/lib/time';
import { errorMessage } from '@/lib/errors';
import { LoadingState, ErrorState, EmptyState } from '@/components/States';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { useToast } from '@/components/toast/ToastContext';
import { WipLimitsPanel } from './WipLimitsPanel';
import { LabelsManager } from '@/features/labels/LabelsManager';

export function TeamsPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { user } = useAuth();
  const isAdmin = Boolean(user?.isAdmin); // team create/rename/delete is admin-only (ADR-0007)
  const teamsQuery = useTeams();
  const teams = teamsQuery.data ?? [];

  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState('');
  const [wipTeamId, setWipTeamId] = useState<string | null>(null);
  const [labelsTeamId, setLabelsTeamId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Team | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: queryKeys.teams });

  const createMutation = useMutation({
    mutationFn: (name: string) => teamsApi.create({ name }),
    onSuccess: () => {
      toast.showSuccess('Team created.');
      setNewName('');
      setShowCreate(false);
      invalidate();
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const renameMutation = useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) => teamsApi.rename(id, { name }),
    onSuccess: () => {
      toast.showSuccess('Team renamed.');
      setEditingId(null);
      setEditingName('');
      invalidate();
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const wipMutation = useMutation({
    mutationFn: ({ id, limits }: { id: string; limits: Partial<Record<TicketState, number | null>> }) =>
      teamsApi.setWipLimits(id, { wipLimits: limits }),
    onSuccess: (_team, { id }) => {
      toast.showSuccess('WIP limits saved.');
      setWipTeamId(null);
      invalidate();
      // Board badges read the limits from the board response; refresh that team's board.
      queryClient.invalidateQueries({ queryKey: ['board', id] });
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => teamsApi.remove(id),
    onSuccess: () => {
      toast.showSuccess('Team deleted.');
      setDeleteTarget(null);
      invalidate();
    },
    onError: (err) => {
      setDeleteTarget(null);
      toast.showError(errorMessage(err));
    },
  });

  const startEdit = (team: Team) => {
    setEditingId(team.id);
    setEditingName(team.name);
  };

  const submitCreate = (e: FormEvent) => {
    e.preventDefault();
    if (!newName.trim()) return;
    createMutation.mutate(newName.trim());
  };

  const submitRename = (e: FormEvent) => {
    e.preventDefault();
    if (!editingId || !editingName.trim()) return;
    renameMutation.mutate({ id: editingId, name: editingName.trim() });
  };

  return (
    <div className="page-container">
      <div className="page-header">
        <h1>Teams</h1>
        <div className="spacer" />
        {isAdmin ? (
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => setShowCreate((v) => !v)}
          >
            + Create team
          </button>
        ) : null}
      </div>
      <p className="page-note">
        {isAdmin
          ? 'Admins create, rename and delete teams and set WIP limits.'
          : 'You can view your teams and set their WIP limits. Only admins manage team creation.'}
      </p>

      {isAdmin && showCreate ? (
        <form className="inline-form" onSubmit={submitCreate}>
          <div className="grow">
            <input
              className="input"
              placeholder="Team name"
              value={newName}
              autoFocus
              onChange={(e) => setNewName(e.target.value)}
              disabled={createMutation.isPending}
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={createMutation.isPending || !newName.trim()}>
            {createMutation.isPending ? 'Creating…' : 'Create'}
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => {
              setShowCreate(false);
              setNewName('');
            }}
            disabled={createMutation.isPending}
          >
            Cancel
          </button>
        </form>
      ) : null}

      {teamsQuery.isLoading ? (
        <LoadingState label="Loading teams…" />
      ) : teamsQuery.isError ? (
        <ErrorState message={errorMessage(teamsQuery.error)} onRetry={() => teamsQuery.refetch()} />
      ) : teams.length === 0 ? (
        <EmptyState
          title="No teams yet"
          message={
            isAdmin
              ? 'Create your first team to start organizing tickets.'
              : 'You are not a member of any team yet. Ask an admin to add you.'
          }
          action={
            isAdmin ? (
              <button type="button" className="btn btn-primary" onClick={() => setShowCreate(true)}>
                + Create team
              </button>
            ) : undefined
          }
        />
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Tickets</th>
              <th>Epics</th>
              <th>Modified</th>
              <th className="text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            {teams.map((team) => {
              const hasChildren = team.ticketCount > 0 || team.epicCount > 0;
              const isEditing = editingId === team.id;
              const isWipOpen = wipTeamId === team.id;
              const isLabelsOpen = labelsTeamId === team.id;
              return (
                <Fragment key={team.id}>
                <tr>
                  <td>
                    {isEditing ? (
                      <form onSubmit={submitRename} className="row" style={{ gap: 8 }}>
                        <input
                          className="input"
                          value={editingName}
                          autoFocus
                          onChange={(e) => setEditingName(e.target.value)}
                          disabled={renameMutation.isPending}
                        />
                        <button
                          type="submit"
                          className="btn btn-primary btn-sm"
                          disabled={renameMutation.isPending || !editingName.trim()}
                        >
                          Save
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => setEditingId(null)}
                          disabled={renameMutation.isPending}
                        >
                          Cancel
                        </button>
                      </form>
                    ) : (
                      team.name
                    )}
                  </td>
                  <td>{team.ticketCount}</td>
                  <td>{team.epicCount}</td>
                  <td className="nowrap muted">{formatUtc(team.modifiedAt)}</td>
                  <td>
                    <div className="table-actions">
                      {isAdmin && !isEditing ? (
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => startEdit(team)}
                        >
                          Edit
                        </button>
                      ) : null}
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        aria-expanded={isWipOpen}
                        onClick={() => setWipTeamId((cur) => (cur === team.id ? null : team.id))}
                      >
                        WIP limits
                      </button>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        aria-expanded={isLabelsOpen}
                        onClick={() => setLabelsTeamId((cur) => (cur === team.id ? null : team.id))}
                      >
                        Labels
                      </button>
                      {isAdmin ? (
                        <button
                          type="button"
                          className="btn btn-danger btn-sm"
                          disabled={hasChildren}
                          title={
                            hasChildren
                              ? 'Cannot delete a team that still has tickets or epics. Remove them first.'
                              : undefined
                          }
                          onClick={() => setDeleteTarget(team)}
                        >
                          Delete
                        </button>
                      ) : null}
                    </div>
                  </td>
                </tr>
                {isWipOpen ? (
                  <tr className="wip-panel-row">
                    <td colSpan={5}>
                      <WipLimitsPanel
                        team={team}
                        busy={wipMutation.isPending}
                        onSave={(limits) => wipMutation.mutate({ id: team.id, limits })}
                        onCancel={() => setWipTeamId(null)}
                      />
                    </td>
                  </tr>
                ) : null}
                {isLabelsOpen ? (
                  <tr className="wip-panel-row">
                    <td colSpan={5}>
                      <LabelsManager teamId={team.id} />
                    </td>
                  </tr>
                ) : null}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      )}

      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete team?"
        message={
          <>
            Delete team <strong>{deleteTarget?.name}</strong>? This cannot be undone.
          </>
        }
        confirmLabel="Delete"
        danger
        busy={deleteMutation.isPending}
        onConfirm={() => deleteTarget && deleteMutation.mutate(deleteTarget.id)}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}
