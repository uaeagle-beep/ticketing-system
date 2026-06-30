// Team management screen (Wireframe 4, US-TEAM-*).
//
// Table: Name, Tickets, Epics, Modified, Actions (Edit / Delete).
// - Create via an inline form ("+ Create team").
// - Rename inline (the row's name becomes an editable input).
// - Delete is DISABLED while the team has tickets or epics, with an explanatory
//   hint (the backend is still authoritative and returns 409 — surfaced as a toast).

import { useState, type FormEvent } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { teamsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { Team } from '@/api/types';
import { useTeams } from './useTeams';
import { formatUtc } from '@/lib/time';
import { errorMessage } from '@/lib/errors';
import { LoadingState, ErrorState, EmptyState } from '@/components/States';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { useToast } from '@/components/toast/ToastContext';

export function TeamsPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const teamsQuery = useTeams();
  const teams = teamsQuery.data ?? [];

  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState('');
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
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => setShowCreate((v) => !v)}
        >
          + Create team
        </button>
      </div>
      <p className="page-note">All verified users can view and manage all teams.</p>

      {showCreate ? (
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
          message="Create your first team to start organizing tickets."
          action={
            <button type="button" className="btn btn-primary" onClick={() => setShowCreate(true)}>
              + Create team
            </button>
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
              return (
                <tr key={team.id}>
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
                      {!isEditing ? (
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
                    </div>
                  </td>
                </tr>
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
