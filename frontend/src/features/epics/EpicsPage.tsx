// Epic management screen (Wireframe 5, US-EPIC-*).
//
// Team selector + table (Title, Tickets, Modified, Actions: Edit / ×).
// - Create epic (title + optional description) for the selected team. Team is
//   chosen at creation and is read-only afterwards (FR-E3-1).
// - Edit title/description in a panel.
// - Delete is DISABLED while tickets reference the epic, with a hint (backend
//   stays authoritative and returns 409 — surfaced as a toast).

import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { epicsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { Epic } from '@/api/types';
import { useTeams } from '@/features/teams/useTeams';
import { useEpics } from './useEpics';
import { formatUtc } from '@/lib/time';
import { errorMessage } from '@/lib/errors';
import { LoadingState, ErrorState, EmptyState } from '@/components/States';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { useToast } from '@/components/toast/ToastContext';

interface EpicFormState {
  title: string;
  description: string;
}

const EMPTY_EPIC_FORM: EpicFormState = { title: '', description: '' };

export function EpicsPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();

  const teamsQuery = useTeams();
  const teams = teamsQuery.data ?? [];

  const teamParam = searchParams.get('team') ?? undefined;
  const selectedTeamId = useMemo(() => {
    if (teamParam && teams.some((t) => t.id === teamParam)) return teamParam;
    return teams[0]?.id;
  }, [teamParam, teams]);

  const epicsQuery = useEpics(selectedTeamId);
  const epics = epicsQuery.data ?? [];

  // 'create' | epicId (editing existing) | null (closed)
  const [panelMode, setPanelMode] = useState<'create' | string | null>(null);
  const [form, setForm] = useState<EpicFormState>(EMPTY_EPIC_FORM);
  const [deleteTarget, setDeleteTarget] = useState<Epic | null>(null);

  // Close any open panel when the team changes.
  useEffect(() => {
    setPanelMode(null);
    setForm(EMPTY_EPIC_FORM);
  }, [selectedTeamId]);

  const invalidate = () => {
    if (selectedTeamId) queryClient.invalidateQueries({ queryKey: queryKeys.epics(selectedTeamId) });
    // Team ticket/epic counts may have changed.
    queryClient.invalidateQueries({ queryKey: queryKeys.teams });
  };

  const createMutation = useMutation({
    mutationFn: () =>
      epicsApi.create({
        teamId: selectedTeamId as string,
        title: form.title.trim(),
        description: form.description.trim() || null,
      }),
    onSuccess: () => {
      toast.showSuccess('Epic created.');
      setPanelMode(null);
      setForm(EMPTY_EPIC_FORM);
      invalidate();
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const updateMutation = useMutation({
    mutationFn: (epicId: string) =>
      epicsApi.update(epicId, {
        title: form.title.trim(),
        description: form.description.trim() || null,
      }),
    onSuccess: () => {
      toast.showSuccess('Epic saved.');
      setPanelMode(null);
      setForm(EMPTY_EPIC_FORM);
      invalidate();
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const deleteMutation = useMutation({
    mutationFn: (epicId: string) => epicsApi.remove(epicId),
    onSuccess: () => {
      toast.showSuccess('Epic deleted.');
      setDeleteTarget(null);
      invalidate();
    },
    onError: (err) => {
      setDeleteTarget(null);
      toast.showError(errorMessage(err));
    },
  });

  const handleTeamChange = (teamId: string) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('team', teamId);
      return next;
    });
  };

  const openCreate = () => {
    setForm(EMPTY_EPIC_FORM);
    setPanelMode('create');
  };

  const openEdit = (epic: Epic) => {
    setForm({ title: epic.title, description: epic.description ?? '' });
    setPanelMode(epic.id);
  };

  const submitPanel = (e: FormEvent) => {
    e.preventDefault();
    if (!form.title.trim()) return;
    if (panelMode === 'create') {
      createMutation.mutate();
    } else if (panelMode) {
      updateMutation.mutate(panelMode);
    }
  };

  const panelBusy = createMutation.isPending || updateMutation.isPending;
  const editingTeam = teams.find((t) => t.id === selectedTeamId);

  return (
    <div className="page-container">
      <div className="page-header">
        <h1>Epics</h1>
        <div className="spacer" />
        <select
          className="select"
          style={{ width: 'auto', minWidth: 160 }}
          aria-label="Select team"
          value={selectedTeamId ?? ''}
          onChange={(e) => handleTeamChange(e.target.value)}
          disabled={teamsQuery.isLoading || teams.length === 0}
        >
          {teams.length === 0 ? <option value="">No teams</option> : null}
          {teams.map((team) => (
            <option key={team.id} value={team.id}>
              {team.name}
            </option>
          ))}
        </select>
        <button
          type="button"
          className="btn btn-primary"
          onClick={openCreate}
          disabled={!selectedTeamId}
        >
          + Create epic
        </button>
      </div>

      {teamsQuery.isLoading ? (
        <LoadingState label="Loading teams…" />
      ) : teamsQuery.isError ? (
        <ErrorState message={errorMessage(teamsQuery.error)} onRetry={() => teamsQuery.refetch()} />
      ) : teams.length === 0 ? (
        <EmptyState
          title="No teams yet"
          message="Create a team first; epics belong to a team."
        />
      ) : (
        <>
          {panelMode !== null ? (
            <form className="inline-form" onSubmit={submitPanel} style={{ flexDirection: 'column' }}>
              <h3 style={{ fontSize: 15 }}>
                {panelMode === 'create'
                  ? `New epic in ${editingTeam?.name ?? ''}`
                  : 'Edit epic'}
              </h3>
              <div className="field" style={{ width: '100%', marginBottom: 8 }}>
                <label htmlFor="epic-title">Title</label>
                <input
                  id="epic-title"
                  className="input"
                  value={form.title}
                  autoFocus
                  onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))}
                  disabled={panelBusy}
                  maxLength={512}
                />
              </div>
              <div className="field" style={{ width: '100%', marginBottom: 8 }}>
                <label htmlFor="epic-description">Description (optional)</label>
                <textarea
                  id="epic-description"
                  className="textarea"
                  value={form.description}
                  onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
                  disabled={panelBusy}
                />
              </div>
              <div className="row" style={{ justifyContent: 'flex-end', width: '100%' }}>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setPanelMode(null)}
                  disabled={panelBusy}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={panelBusy || !form.title.trim()}
                >
                  {panelBusy ? 'Saving…' : panelMode === 'create' ? 'Create' : 'Save'}
                </button>
              </div>
            </form>
          ) : null}

          {epicsQuery.isLoading ? (
            <LoadingState label="Loading epics…" />
          ) : epicsQuery.isError ? (
            <ErrorState message={errorMessage(epicsQuery.error)} onRetry={() => epicsQuery.refetch()} />
          ) : epics.length === 0 ? (
            <EmptyState
              title="No epics for this team"
              message="Create the first epic to group related tickets."
              action={
                <button type="button" className="btn btn-primary" onClick={openCreate}>
                  + Create epic
                </button>
              }
            />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Tickets</th>
                  <th>Modified</th>
                  <th className="text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                {epics.map((epic) => {
                  const referenced = epic.ticketCount > 0;
                  return (
                    <tr key={epic.id}>
                      <td>
                        <div>{epic.title}</div>
                        {epic.description ? (
                          <div className="muted" style={{ fontSize: 12 }}>
                            {epic.description}
                          </div>
                        ) : null}
                      </td>
                      <td>{epic.ticketCount}</td>
                      <td className="nowrap muted">{formatUtc(epic.modifiedAt)}</td>
                      <td>
                        <div className="table-actions">
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            onClick={() => openEdit(epic)}
                          >
                            Edit
                          </button>
                          <button
                            type="button"
                            className="btn btn-danger btn-sm"
                            disabled={referenced}
                            title={
                              referenced
                                ? 'Cannot delete an epic that is referenced by tickets. Reassign or remove those tickets first.'
                                : undefined
                            }
                            onClick={() => setDeleteTarget(epic)}
                            aria-label={`Delete ${epic.title}`}
                          >
                            ×
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </>
      )}

      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete epic?"
        message={
          <>
            Delete epic <strong>{deleteTarget?.title}</strong>? This cannot be undone.
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
