// Per-team WIP limits panel (UX §2.4). Rendered inline below a team's row on the
// Team management screen. One numeric field per state (5 fields); empty = "No limit".
// Explicit "Save limits" button saves all five in one request; Cancel discards.
//
// Validation (on blur + on submit, never per keystroke — UX §2.2):
//   valid  = empty OR a whole number in [1, 999]
//   invalid -> "Enter a whole number of 1 or more, or leave blank for no limit."
//   > 999   -> "Enter a number no greater than 999."
// Save is disabled while any field is invalid or a save is in flight.

import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import type { Team, TicketState } from '@/api/types';
import { orderedStates, stateLabel } from '@/lib/labels';

const RANGE_MESSAGE = 'Enter a whole number of 1 or more, or leave blank for no limit.';
const MAX_MESSAGE = 'Enter a number no greater than 999.';
const WIP_MAX = 999;

type FieldValues = Record<TicketState, string>;
type FieldErrors = Partial<Record<TicketState, string>>;

interface WipLimitsPanelProps {
  team: Team;
  busy: boolean;
  onSave: (limits: Partial<Record<TicketState, number | null>>) => void;
  onCancel: () => void;
}

// Build the controlled string field values from a team's limits (null -> '').
function initialValues(team: Team): FieldValues {
  const values = {} as FieldValues;
  for (const state of orderedStates) {
    const limit = team.wipLimits[state];
    values[state] = limit == null ? '' : String(limit);
  }
  return values;
}

// Validate a single raw field value. Empty is valid (unlimited). Returns an error
// message or undefined. Rejects non-integers, < 1, and > 999.
function validateField(raw: string): string | undefined {
  const trimmed = raw.trim();
  if (trimmed === '') return undefined; // blank = no limit
  // Whole numbers only: digits, no decimals/sign/exponent.
  if (!/^\d+$/.test(trimmed)) return RANGE_MESSAGE;
  const value = Number(trimmed);
  if (value < 1) return RANGE_MESSAGE;
  if (value > WIP_MAX) return MAX_MESSAGE;
  return undefined;
}

export function WipLimitsPanel({ team, busy, onSave, onCancel }: WipLimitsPanelProps) {
  const [values, setValues] = useState<FieldValues>(() => initialValues(team));
  const [errors, setErrors] = useState<FieldErrors>({});
  const firstFieldRef = useRef<HTMLInputElement | null>(null);

  // Focus the first field when the panel opens (UX §2.4 a11y).
  useEffect(() => {
    firstFieldRef.current?.focus();
  }, []);

  const hasErrors = useMemo(() => Object.values(errors).some(Boolean), [errors]);

  const setField = (state: TicketState, raw: string) => {
    setValues((prev) => ({ ...prev, [state]: raw }));
    // Clear a field's error as the user edits it; re-validate on blur/submit.
    setErrors((prev) => (prev[state] ? { ...prev, [state]: undefined } : prev));
  };

  const validateOnBlur = (state: TicketState) => {
    setErrors((prev) => ({ ...prev, [state]: validateField(values[state] ?? '') }));
  };

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    // Validate every field; focus the first invalid one if any.
    const nextErrors: FieldErrors = {};
    for (const state of orderedStates) {
      nextErrors[state] = validateField(values[state] ?? '');
    }
    setErrors(nextErrors);
    const firstInvalid = orderedStates.find((s) => nextErrors[s]);
    if (firstInvalid) {
      document.getElementById(`wip-${firstInvalid}`)?.focus();
      return;
    }

    // Convert to the request map: '' -> null (unlimited), otherwise the integer.
    const limits: Partial<Record<TicketState, number | null>> = {};
    for (const state of orderedStates) {
      const trimmed = (values[state] ?? '').trim();
      limits[state] = trimmed === '' ? null : Number(trimmed);
    }
    onSave(limits);
  };

  return (
    <form
      className="panel wip-panel"
      onSubmit={handleSubmit}
      onKeyDown={(e) => {
        if (e.key === 'Escape') {
          e.preventDefault();
          onCancel();
        }
      }}
      role="group"
      aria-label={`WIP limits for ${team.name}`}
      noValidate
    >
      <p className="wip-panel-intro">
        Cap how many tickets each column can hold. Leave a field blank for no limit.
      </p>

      <div className="wip-field-list">
        {orderedStates.map((state, index) => {
          const error = errors[state];
          const errorId = `wip-${state}-error`;
          return (
            <div className="wip-field-row" key={state}>
              <label className="wip-field-label" htmlFor={`wip-${state}`}>
                {stateLabel(state).toUpperCase()}
              </label>
              <div className="wip-field-control">
                <input
                  id={`wip-${state}`}
                  ref={index === 0 ? firstFieldRef : undefined}
                  className="input"
                  type="number"
                  min={1}
                  max={WIP_MAX}
                  step={1}
                  inputMode="numeric"
                  placeholder="No limit"
                  value={values[state] ?? ''}
                  disabled={busy}
                  aria-invalid={error ? true : undefined}
                  aria-describedby={error ? errorId : undefined}
                  onChange={(e) => setField(state, e.target.value)}
                  onBlur={() => validateOnBlur(state)}
                />
                <span className="wip-field-unit">tickets</span>
              </div>
              {error ? (
                <span id={errorId} className="field-error">
                  {error}
                </span>
              ) : (
                <span className="field-hint">Blank = no limit</span>
              )}
            </div>
          );
        })}
      </div>

      <div className="row" style={{ justifyContent: 'flex-end', gap: 10, marginTop: 12 }}>
        <button type="button" className="btn btn-secondary btn-sm" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
        <button type="submit" className="btn btn-primary btn-sm" disabled={busy || hasErrors}>
          {busy ? 'Saving…' : 'Save limits'}
        </button>
      </div>
    </form>
  );
}
