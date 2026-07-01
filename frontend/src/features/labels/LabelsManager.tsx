// Label management panel for a team (Wave 2, §9.4, ADR-0016). Member-managed: any member of the team
// (or an admin) can create, rename/recolor and delete the team's labels. Rendered as an expandable panel
// on the Teams page row (mirroring the WIP-limits panel). Create is an inline form; each existing label
// has inline Edit (name + color) and Delete. Delete is disposable (removes the label from all tickets) —
// confirmed to avoid an accidental removal. Errors surface as toasts (409 duplicate_label_name mapped).

import { useState, type FormEvent } from 'react';
import type { Label } from '@/api/types';
import { useLabels, useLabelMutations } from './useLabels';
import { LabelChip } from '@/components/Badges';
import { errorMessage } from '@/lib/errors';
import { useToast } from '@/components/toast/ToastContext';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { LoadingState, ErrorState } from '@/components/States';

const DEFAULT_COLOR = '#3b82f6';

// A small fixed swatch palette (§9.4) plus a free hex input, so members pick a sensible color fast.
const PALETTE = [
  '#ef4444', '#f97316', '#eab308', '#22c55e',
  '#3b82f6', '#6366f1', '#a855f7', '#ec4899',
  '#64748b', '#0ea5e9', '#14b8a6', '#111827',
];

export function LabelsManager({ teamId }: { teamId: string }) {
  const toast = useToast();
  const labelsQuery = useLabels(teamId);
  const { create, update, remove } = useLabelMutations(teamId);

  const [newName, setNewName] = useState('');
  const [newColor, setNewColor] = useState(DEFAULT_COLOR);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [editColor, setEditColor] = useState(DEFAULT_COLOR);
  const [deleteTarget, setDeleteTarget] = useState<Label | null>(null);

  const submitCreate = (e: FormEvent) => {
    e.preventDefault();
    if (!newName.trim()) return;
    create.mutate(
      { teamId, name: newName.trim(), color: newColor },
      {
        onSuccess: () => {
          toast.showSuccess('Label created.');
          setNewName('');
          setNewColor(DEFAULT_COLOR);
        },
        onError: (err) => toast.showError(errorMessage(err)),
      },
    );
  };

  const startEdit = (label: Label) => {
    setEditingId(label.id);
    setEditName(label.name);
    setEditColor(label.color);
  };

  const submitEdit = (e: FormEvent) => {
    e.preventDefault();
    if (!editingId || !editName.trim()) return;
    update.mutate(
      { id: editingId, body: { name: editName.trim(), color: editColor } },
      {
        onSuccess: () => {
          toast.showSuccess('Label saved.');
          setEditingId(null);
        },
        onError: (err) => toast.showError(errorMessage(err)),
      },
    );
  };

  const confirmDelete = () => {
    if (!deleteTarget) return;
    remove.mutate(deleteTarget.id, {
      onSuccess: () => {
        toast.showSuccess('Label deleted.');
        setDeleteTarget(null);
      },
      onError: (err) => {
        setDeleteTarget(null);
        toast.showError(errorMessage(err));
      },
    });
  };

  const labels = labelsQuery.data ?? [];

  return (
    <div className="labels-manager">
      <form className="inline-form" onSubmit={submitCreate} aria-label="Create label">
        <div className="grow">
          <input
            className="input"
            placeholder="Label name"
            value={newName}
            maxLength={50}
            onChange={(e) => setNewName(e.target.value)}
            disabled={create.isPending}
            aria-label="New label name"
          />
        </div>
        <ColorSelect value={newColor} onChange={setNewColor} disabled={create.isPending} ariaLabel="New label color" />
        <button type="submit" className="btn btn-primary btn-sm" disabled={create.isPending || !newName.trim()}>
          {create.isPending ? 'Adding…' : 'Add label'}
        </button>
      </form>

      {labelsQuery.isLoading ? (
        <LoadingState label="Loading labels…" />
      ) : labelsQuery.isError ? (
        <ErrorState message={errorMessage(labelsQuery.error)} onRetry={() => labelsQuery.refetch()} />
      ) : labels.length === 0 ? (
        <p className="muted" style={{ marginTop: 8 }}>
          No labels yet. Add one above.
        </p>
      ) : (
        <ul className="labels-list">
          {labels.map((label) =>
            editingId === label.id ? (
              <li key={label.id}>
                <form className="inline-form" onSubmit={submitEdit} aria-label={`Edit label ${label.name}`}>
                  <div className="grow">
                    <input
                      className="input"
                      value={editName}
                      maxLength={50}
                      autoFocus
                      onChange={(e) => setEditName(e.target.value)}
                      disabled={update.isPending}
                      aria-label="Label name"
                    />
                  </div>
                  <ColorSelect value={editColor} onChange={setEditColor} disabled={update.isPending} ariaLabel="Label color" />
                  <button type="submit" className="btn btn-primary btn-sm" disabled={update.isPending || !editName.trim()}>
                    Save
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={() => setEditingId(null)}
                    disabled={update.isPending}
                  >
                    Cancel
                  </button>
                </form>
              </li>
            ) : (
              <li key={label.id} className="labels-list-row">
                <LabelChip label={label} />
                <div className="spacer" />
                <button type="button" className="btn btn-secondary btn-sm" onClick={() => startEdit(label)}>
                  Edit
                </button>
                <button type="button" className="btn btn-danger btn-sm" onClick={() => setDeleteTarget(label)}>
                  Delete
                </button>
              </li>
            ),
          )}
        </ul>
      )}

      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete label?"
        message={
          <>
            Delete label <strong>{deleteTarget?.name}</strong>? It will be removed from all tickets. This
            cannot be undone.
          </>
        }
        confirmLabel="Delete"
        danger
        busy={remove.isPending}
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}

// A color picker = a fixed swatch palette (as a native select) + a hex color input, keeping the value a
// valid "#rrggbb" the backend accepts. Both edit the same value.
function ColorSelect({
  value,
  onChange,
  disabled,
  ariaLabel,
}: {
  value: string;
  onChange: (color: string) => void;
  disabled?: boolean;
  ariaLabel: string;
}) {
  const inPalette = PALETTE.includes(value.toLowerCase());
  return (
    <div className="color-select">
      <select
        className="select"
        aria-label={`${ariaLabel} palette`}
        value={inPalette ? value.toLowerCase() : ''}
        onChange={(e) => e.target.value && onChange(e.target.value)}
        disabled={disabled}
      >
        {!inPalette ? <option value="">Custom</option> : null}
        {PALETTE.map((c) => (
          <option key={c} value={c}>
            {c}
          </option>
        ))}
      </select>
      <input
        type="color"
        aria-label={ariaLabel}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
      />
    </div>
  );
}
