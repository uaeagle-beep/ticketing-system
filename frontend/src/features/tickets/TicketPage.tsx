// Ticket create / edit / details view (Wireframe 3, US-TICKET-*).
//
// Route /tickets/new          -> create mode (team may be prefilled via ?team=)
// Route /tickets/:id          -> edit/details mode (loads the ticket)
//
// Fields: Team, Type, State, Epic (dropdown scoped to the selected team), Title,
// Body. On team change the selected epic is cleared (FR-E4-5) and the epic
// dropdown reloads for the new team. Meta line (id, created by, created/modified
// UTC) is shown in edit mode. Delete requires explicit confirmation (FR-E4-6).
// Comments panel is shown in edit mode only (a ticket must exist first).

import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ticketsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type {
  CreateTicketRequest,
  TicketPriority,
  TicketState,
  TicketType,
  UpdateTicketRequest,
} from '@/api/types';
import { priorityOptions, stateOptions, typeOptions } from '@/lib/labels';
import { formatUtc } from '@/lib/time';
import { displayName } from '@/lib/displayName';
import { errorMessage, isApiErrorCode } from '@/lib/errors';
import { useTeams } from '@/features/teams/useTeams';
import { useEpics } from '@/features/epics/useEpics';
import { useLabels } from '@/features/labels/useLabels';
import { LabelPicker } from '@/features/labels/LabelPicker';
import { LabelChips } from '@/components/Badges';
import { MultiSelectDropdown } from '@/components/MultiSelectDropdown';
import { useTeamMembers } from './useTeamMembers';
import { LoadingState, ErrorState } from '@/components/States';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { useToast } from '@/components/toast/ToastContext';
import { CommentsPanel } from './CommentsPanel';
import { AttachmentsPanel } from './AttachmentsPanel';
import { ActivityTimeline } from './ActivityTimeline';
import { useToggleWatch } from './useWatch';
import { useRealtime } from '@/features/realtime/RealtimeProvider';

interface FormState {
  teamId: string;
  type: TicketType;
  state: TicketState;
  priority: TicketPriority;
  epicId: string; // '' means no epic
  dueDate: string; // '' means no due date; otherwise "YYYY-MM-DD"
  title: string;
  body: string;
  assigneeIds: string[];
  labelIds: string[];
}

const EMPTY_FORM: FormState = {
  teamId: '',
  type: 'bug',
  state: 'new',
  priority: 'medium',
  epicId: '',
  dueDate: '',
  title: '',
  body: '',
  assigneeIds: [],
  labelIds: [],
};

// Order-insensitive comparison of two id sets (used to decide whether assignees changed on edit).
function sameIdSet(a: string[], b: string[]): boolean {
  if (a.length !== b.length) return false;
  const set = new Set(a);
  return b.every((id) => set.has(id));
}

export function TicketPage() {
  const { t } = useTranslation('tickets');
  const { id } = useParams<{ id: string }>();
  const isCreate = !id || id === 'new';
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [searchParams] = useSearchParams();

  const teamsQuery = useTeams();
  const teams = teamsQuery.data ?? [];

  const ticketQuery = useQuery({
    queryKey: id ? queryKeys.ticket(id) : ['ticket', 'new'],
    queryFn: ({ signal }) => ticketsApi.get(id as string, signal),
    enabled: !isCreate && !!id,
  });

  // Real-time (Wave 3, ADR-0019): join the ticket group while the detail page is open so a `ticketChanged`
  // push invalidates this ticket's queries the moment a teammate edits it/comments; leave on unmount. The
  // team is re-checked server-side before the join, so this never subscribes to a ticket the caller can't see.
  const { subscribeTicket } = useRealtime();
  const detailTeamId = ticketQuery.data?.teamId;
  useEffect(() => {
    if (isCreate || !id || !detailTeamId) return;
    return subscribeTicket(id, detailTeamId);
  }, [isCreate, id, detailTeamId, subscribeTicket]);

  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [initialized, setInitialized] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<keyof FormState, string>>>({});
  const [confirmOpen, setConfirmOpen] = useState(false);
  // Set when a create/edit is rejected because the target State is at its WIP limit (UX §4.3).
  // Shown inline (banner + State field-error); cleared when the user changes the State value.
  const [wipBlocked, setWipBlocked] = useState(false);

  const WIP_BLOCK_MESSAGE = t('form.wipBlocked');

  // Initialize the form once: from the loaded ticket (edit) or from defaults
  // with an optional prefilled team (create).
  useEffect(() => {
    if (initialized) return;
    if (isCreate) {
      if (teamsQuery.isLoading) return;
      const prefTeam = searchParams.get('team');
      const teamId =
        prefTeam && teams.some((t) => t.id === prefTeam) ? prefTeam : teams[0]?.id ?? '';
      setForm({ ...EMPTY_FORM, teamId });
      setInitialized(true);
    } else if (ticketQuery.data) {
      const t = ticketQuery.data;
      setForm({
        teamId: t.teamId,
        type: t.type,
        state: t.state,
        priority: t.priority,
        epicId: t.epicId ?? '',
        dueDate: t.dueDate ?? '',
        title: t.title,
        body: t.body,
        assigneeIds: t.assignees.map((a) => a.id),
        labelIds: t.labels.map((l) => l.id),
      });
      setInitialized(true);
    }
  }, [initialized, isCreate, teamsQuery.isLoading, teams, ticketQuery.data, searchParams]);

  const epicsQuery = useEpics(form.teamId || undefined);
  const epics = epicsQuery.data ?? [];

  // Candidate assignees for the selected team (members ∪ admins). Empty for non-admins (no
  // member-listing endpoint — see useTeamMembers's contract-gap note).
  const { candidates: assigneeCandidates, canList: canListAssignees } = useTeamMembers(
    form.teamId || undefined,
  );

  // The selected team's labels for the picker (member-visible; §9.4). Empty until a team is chosen.
  const labelsQuery = useLabels(form.teamId || undefined);
  const teamLabels = labelsQuery.data ?? [];

  // If the currently-selected epic isn't in the loaded team's epic list (e.g.
  // after a team change), clear it so we never submit a cross-team epic (V16).
  useEffect(() => {
    if (!form.epicId) return;
    if (epicsQuery.isLoading) return;
    if (!epics.some((e) => e.id === form.epicId)) {
      setForm((f) => ({ ...f, epicId: '' }));
    }
  }, [epics, epicsQuery.isLoading, form.epicId]);

  const updateField = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((f) => ({ ...f, [key]: value }));
  };

  const handleTeamChange = (teamId: string) => {
    // Changing team clears the selected epic (FR-E4-5); the epic dropdown then
    // reloads for the new team. Assignees and labels are also team-scoped, so clear them on team change
    // too (analogous to clear-epic, §7.1 / §9.4). A team change also changes which states are full, so
    // clear any stale WIP block (UX §4.3).
    setForm((f) => ({ ...f, teamId, epicId: '', assigneeIds: [], labelIds: [] }));
    setWipBlocked(false);
  };

  const toggleAssignee = (userId: string) => {
    setForm((f) => ({
      ...f,
      assigneeIds: f.assigneeIds.includes(userId)
        ? f.assigneeIds.filter((id) => id !== userId)
        : [...f.assigneeIds, userId],
    }));
  };

  const toggleLabel = (labelId: string) => {
    setForm((f) => ({
      ...f,
      labelIds: f.labelIds.includes(labelId)
        ? f.labelIds.filter((id) => id !== labelId)
        : [...f.labelIds, labelId],
    }));
  };

  const handleStateChange = (state: TicketState) => {
    updateField('state', state);
    // The block is anchored to the previously-chosen state; clear it on change.
    setWipBlocked(false);
  };

  // On a WIP rejection: keep all entered values (no navigation), show the inline message,
  // and move focus to the State select (UX §4.3). Other errors fall back to a toast.
  const handleSaveError = (err: unknown) => {
    if (isApiErrorCode(err, 'wip_limit_reached')) {
      setWipBlocked(true);
      document.getElementById('ticket-state')?.focus();
      return;
    }
    toast.showError(errorMessage(err));
  };

  const createMutation = useMutation({
    // Create the ticket, then (only if labels were picked) apply the full set via the dedicated
    // sub-resource (§5.7). assigneeIds go in the create body directly; labels use the sub-resource.
    mutationFn: async (payload: CreateTicketRequest) => {
      const created = await ticketsApi.create(payload);
      if (form.labelIds.length > 0) {
        return ticketsApi.setLabels(created.id, { labelIds: form.labelIds });
      }
      return created;
    },
    onSuccess: (created) => {
      toast.showSuccess(t('toast.created'));
      queryClient.invalidateQueries({ queryKey: ['board', created.teamId] });
      queryClient.invalidateQueries({ queryKey: queryKeys.teams });
      navigate(`/tickets/${created.id}`, { replace: true });
    },
    onError: handleSaveError,
  });

  const updateMutation = useMutation({
    // Save the scalar fields, then (only if the assignee set changed) replace it via the dedicated
    // sub-resource (§4.2 recommended primary path). The main PUT omits assigneeIds so it leaves the
    // set untouched (R-10). Assignment does not bump modified_at, so ordering stays stable.
    mutationFn: async (payload: UpdateTicketRequest) => {
      let saved = await ticketsApi.update(id as string, payload);
      const originalAssignees = (ticketQuery.data?.assignees ?? []).map((a) => a.id);
      if (!sameIdSet(originalAssignees, form.assigneeIds)) {
        saved = await ticketsApi.setAssignees(id as string, { userIds: form.assigneeIds });
      }
      // Labels are also team-scoped metadata replaced via their own sub-resource (§5.7); apply only
      // when the set changed. The last sub-resource response carries the freshest detail (labels[]).
      const originalLabels = (ticketQuery.data?.labels ?? []).map((l) => l.id);
      if (!sameIdSet(originalLabels, form.labelIds)) {
        saved = await ticketsApi.setLabels(id as string, { labelIds: form.labelIds });
      }
      return saved;
    },
    onSuccess: (updated) => {
      toast.showSuccess(t('toast.saved'));
      queryClient.setQueryData(queryKeys.ticket(updated.id), updated);
      queryClient.invalidateQueries({ queryKey: ['board', updated.teamId] });
      // A field/state/assignee change writes activity — refresh the timeline (§9.3).
      queryClient.invalidateQueries({ queryKey: queryKeys.activity(updated.id) });
    },
    onError: handleSaveError,
  });

  // Watch toggle (edit mode only; a ticket must exist). Reflects ticket.isWatching (§6.7).
  const watchToggle = useToggleWatch(id ?? '');

  const deleteMutation = useMutation({
    mutationFn: () => ticketsApi.remove(id as string),
    onSuccess: () => {
      toast.showSuccess(t('toast.deleted'));
      const teamId = ticketQuery.data?.teamId;
      if (teamId) queryClient.invalidateQueries({ queryKey: ['board', teamId] });
      queryClient.invalidateQueries({ queryKey: queryKeys.teams });
      navigate(teamId ? `/board?team=${teamId}` : '/board');
    },
    onError: (err) => {
      setConfirmOpen(false);
      toast.showError(errorMessage(err));
    },
  });

  const validate = (): boolean => {
    const errs: Partial<Record<keyof FormState, string>> = {};
    if (!form.teamId) errs.teamId = t('form.validation.team');
    if (!form.title.trim()) errs.title = t('form.validation.title');
    if (!form.body.trim()) errs.body = t('form.validation.body');
    setFieldErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (!validate()) return;

    const epicId = form.epicId || null;
    const dueDate = form.dueDate || null;
    if (isCreate) {
      createMutation.mutate({
        teamId: form.teamId,
        type: form.type,
        title: form.title.trim(),
        body: form.body.trim(),
        epicId,
        state: form.state,
        priority: form.priority,
        dueDate,
        assigneeIds: form.assigneeIds.length ? form.assigneeIds : undefined,
      });
    } else {
      updateMutation.mutate({
        teamId: form.teamId,
        type: form.type,
        epicId,
        title: form.title.trim(),
        body: form.body.trim(),
        state: form.state,
        priority: form.priority,
        dueDate,
      });
    }
  };

  const detail = ticketQuery.data;
  const backTeamId = detail?.teamId ?? form.teamId;
  const saving = createMutation.isPending || updateMutation.isPending;

  // ---- Render branches ----
  if (!isCreate && ticketQuery.isLoading) {
    return <LoadingState label={t('loading')} />;
  }
  if (!isCreate && ticketQuery.isError) {
    return (
      <ErrorState message={errorMessage(ticketQuery.error)} onRetry={() => ticketQuery.refetch()} />
    );
  }
  if (teamsQuery.isError) {
    return <ErrorState message={errorMessage(teamsQuery.error)} onRetry={() => teamsQuery.refetch()} />;
  }

  return (
    <div className="ticket-detail">
      <div>
        <div className="page-header" style={{ marginBottom: 4 }}>
          <Link to={backTeamId ? `/board?team=${backTeamId}` : '/board'}>{t('backToBoard')}</Link>
          <div className="spacer" />
          {!isCreate && detail ? (
            <button
              type="button"
              className={`btn btn-sm ${detail.isWatching ? 'btn-secondary' : 'btn-ghost'}`}
              onClick={() => watchToggle.toggle(detail.isWatching)}
              disabled={watchToggle.isPending}
              aria-pressed={detail.isWatching}
              title={detail.isWatching ? t('watch.stopTitle') : t('watch.startTitle')}
            >
              {detail.isWatching ? t('watch.watching') : t('watch.watch')}
            </button>
          ) : null}
          {!isCreate ? (
            <button
              type="button"
              className="btn btn-danger btn-sm"
              onClick={() => setConfirmOpen(true)}
              disabled={deleteMutation.isPending}
            >
              {t('delete')}
            </button>
          ) : null}
        </div>

        <h1 style={{ fontSize: 20, marginBottom: 4 }}>
          {isCreate ? t('newTicket') : detail?.title}
        </h1>

        {!isCreate && detail ? (
          <div className="ticket-meta-line">
            <span>{detail.id}</span>
            <span className="dot">{t('meta.createdBy', { name: displayName(detail.createdByName, detail.createdByEmail) })}</span>
            <span className="dot">{t('meta.created', { date: formatUtc(detail.createdAt) })}</span>
            <span className="dot">{t('meta.modified', { date: formatUtc(detail.modifiedAt) })}</span>
          </div>
        ) : null}

        {!isCreate && detail && detail.labels.length > 0 ? (
          <div style={{ marginBottom: 8 }}>
            <LabelChips labels={detail.labels} />
          </div>
        ) : null}

        <form className="panel" onSubmit={handleSubmit} noValidate>
          {wipBlocked ? (
            <div className="banner banner-error" role="alert">
              {WIP_BLOCK_MESSAGE}
            </div>
          ) : null}
          <div className="form-grid">
            <div className="field">
              <label htmlFor="ticket-team">{t('fields.team')}</label>
              <select
                id="ticket-team"
                className="select"
                value={form.teamId}
                onChange={(e) => handleTeamChange(e.target.value)}
                disabled={saving || teamsQuery.isLoading}
              >
                <option value="" disabled>
                  {t('fields.teamPlaceholder')}
                </option>
                {teams.map((team) => (
                  <option key={team.id} value={team.id}>
                    {team.name}
                  </option>
                ))}
              </select>
              {fieldErrors.teamId ? <span className="field-error">{fieldErrors.teamId}</span> : null}
            </div>

            <div className="field">
              <label htmlFor="ticket-epic">{t('fields.epic')}</label>
              <select
                id="ticket-epic"
                className="select"
                value={form.epicId}
                onChange={(e) => updateField('epicId', e.target.value)}
                disabled={saving || !form.teamId || epicsQuery.isLoading}
              >
                <option value="">{t('fields.noEpic')}</option>
                {epics.map((epic) => (
                  <option key={epic.id} value={epic.id}>
                    {epic.title}
                  </option>
                ))}
              </select>
            </div>

            <div className="field">
              <label htmlFor="ticket-type">{t('fields.type')}</label>
              <select
                id="ticket-type"
                className="select"
                value={form.type}
                onChange={(e) => updateField('type', e.target.value as TicketType)}
                disabled={saving}
              >
                {typeOptions().map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>

            <div className="field">
              <label htmlFor="ticket-state">{t('fields.state')}</label>
              <select
                id="ticket-state"
                className="select"
                value={form.state}
                onChange={(e) => handleStateChange(e.target.value as TicketState)}
                disabled={saving}
                aria-invalid={wipBlocked ? true : undefined}
                aria-describedby={wipBlocked ? 'ticket-state-error' : undefined}
              >
                {stateOptions().map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
              {wipBlocked ? (
                <span id="ticket-state-error" className="field-error">
                  {WIP_BLOCK_MESSAGE}
                </span>
              ) : null}
            </div>

            <div className="field">
              <label htmlFor="ticket-priority">{t('fields.priority')}</label>
              <select
                id="ticket-priority"
                className="select"
                value={form.priority}
                onChange={(e) => updateField('priority', e.target.value as TicketPriority)}
                disabled={saving}
              >
                {priorityOptions().map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>

            <div className="field">
              <label htmlFor="ticket-due-date">{t('fields.dueDate')}</label>
              <input
                id="ticket-due-date"
                className="input"
                type="date"
                value={form.dueDate}
                onChange={(e) => updateField('dueDate', e.target.value)}
                disabled={saving}
              />
            </div>

            <div className="field full">
              <label htmlFor="ticket-assignees">{t('fields.assignees')}</label>
              {canListAssignees ? (
                assigneeCandidates.length > 0 ? (
                  <MultiSelectDropdown
                    id="ticket-assignees"
                    ariaLabel={t('fields.assignees')}
                    options={assigneeCandidates}
                    selectedIds={form.assigneeIds}
                    onToggle={toggleAssignee}
                    disabled={saving}
                    placeholder={t('fields.assigneesPlaceholder')}
                    renderOption={(u) => <span>{u.displayName}</span>}
                    renderSelected={(u) => <span className="muted">{u.displayName}</span>}
                  />
                ) : (
                  <span className="muted">{t('assignees.noEligible')}</span>
                )
              ) : (
                // No member-listing endpoint is available to a non-admin (contract gap); show the
                // current assignees read-only. Admins can manage assignees; a member cannot add here.
                <span className="muted">
                  {form.assigneeIds.length > 0
                    ? t('assignees.assignedCount', { count: form.assigneeIds.length })
                    : t('assignees.unassigned')}
                  {t('assignees.adminOnly')}
                </span>
              )}
            </div>

            <div className="field full">
              <label htmlFor="ticket-labels">{t('fields.labels')}</label>
              {form.teamId ? (
                <LabelPicker
                  labels={teamLabels}
                  selectedIds={form.labelIds}
                  disabled={saving || labelsQuery.isLoading}
                  onToggle={toggleLabel}
                />
              ) : (
                <span className="muted">{t('fields.labelsPlaceholder')}</span>
              )}
            </div>

            <div className="field full">
              <label htmlFor="ticket-title">{t('fields.title')}</label>
              <input
                id="ticket-title"
                className="input"
                value={form.title}
                onChange={(e) => updateField('title', e.target.value)}
                disabled={saving}
                maxLength={512}
              />
              {fieldErrors.title ? <span className="field-error">{fieldErrors.title}</span> : null}
            </div>

            <div className="field full">
              <label htmlFor="ticket-body">{t('fields.body')}</label>
              <textarea
                id="ticket-body"
                className="textarea"
                style={{ minHeight: 160 }}
                value={form.body}
                onChange={(e) => updateField('body', e.target.value)}
                disabled={saving}
              />
              {fieldErrors.body ? <span className="field-error">{fieldErrors.body}</span> : null}
            </div>
          </div>

          <div className="row" style={{ justifyContent: 'flex-end', gap: 10 }}>
            <Link
              to={backTeamId ? `/board?team=${backTeamId}` : '/board'}
              className="btn btn-secondary"
            >
              {t('cancel')}
            </Link>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? t('form.saving') : isCreate ? t('form.create') : t('form.save')}
            </button>
          </div>
        </form>
      </div>

      {/* Comments + activity only exist for a persisted ticket. */}
      {!isCreate && id ? (
        <div className="ticket-side">
          <AttachmentsPanel ticketId={id} />
          <CommentsPanel ticketId={id} />
          <ActivityTimeline ticketId={id} />
        </div>
      ) : (
        <div />
      )}

      <ConfirmDialog
        open={confirmOpen}
        title={t('deleteConfirm.title')}
        message={t('deleteConfirm.message')}
        confirmLabel={t('deleteConfirm.confirm')}
        danger
        busy={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setConfirmOpen(false)}
      />
    </div>
  );
}
