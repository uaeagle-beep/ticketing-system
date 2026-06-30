// Create-user dialog (API_CONTRACT §8). Email + optional password (or auto-generate), an admin
// toggle, and team membership. On success with a generated password we keep the dialog open to
// show the one-time password; otherwise we close.

import { useState, type FormEvent } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { adminUsersApi } from '@/api/endpoints';
import type { CreateUserResponse, Team } from '@/api/types';
import { ApiError } from '@/api/client';
import { errorMessage } from '@/lib/errors';
import { useToast } from '@/components/toast/ToastContext';
import { usersQueryKey } from './useUsers';
import { TeamCheckboxList } from './TeamCheckboxList';
import { GeneratedPasswordNotice } from './GeneratedPasswordNotice';

interface CreateUserDialogProps {
  teams: Team[];
  onClose: () => void;
}

export function CreateUserDialog({ teams, onClose }: CreateUserDialogProps) {
  const queryClient = useQueryClient();
  const toast = useToast();

  const [email, setEmail] = useState('');
  const [autoGenerate, setAutoGenerate] = useState(true);
  const [password, setPassword] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);
  const [selectedTeams, setSelectedTeams] = useState<Set<string>>(new Set());
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreateUserResponse | null>(null);

  const createMutation = useMutation({
    mutationFn: () =>
      adminUsersApi.create({
        email: email.trim(),
        password: autoGenerate ? null : password,
        isAdmin,
        teamIds: Array.from(selectedTeams),
      }),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: usersQueryKey });
      if (result.generatedPassword) {
        // Keep the dialog open to surface the one-time password.
        setCreated(result);
      } else {
        toast.showSuccess('User created.');
        onClose();
      }
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === 'validation_error') {
        setFieldError(err.fieldErrorText() ?? errorMessage(err));
      } else {
        toast.showError(errorMessage(err));
      }
    },
  });

  const toggleTeam = (teamId: string) =>
    setSelectedTeams((prev) => {
      const next = new Set(prev);
      if (next.has(teamId)) next.delete(teamId);
      else next.add(teamId);
      return next;
    });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setFieldError(null);
    if (!email.trim()) {
      setFieldError('Email is required.');
      return;
    }
    if (!autoGenerate && password.length < 8) {
      setFieldError('Password must be at least 8 characters, or choose auto-generate.');
      return;
    }
    createMutation.mutate();
  };

  // After a successful auto-generated create, show the one-time password screen.
  if (created) {
    return (
      <div className="modal-backdrop" onMouseDown={onClose}>
        <div className="modal" role="dialog" aria-modal="true" aria-label="User created" onMouseDown={(e) => e.stopPropagation()}>
          <h3>User created</h3>
          <div className="modal-body">
            <p>
              <strong>{created.user.email}</strong> was created.
            </p>
            {created.generatedPassword ? (
              <GeneratedPasswordNotice password={created.generatedPassword} />
            ) : null}
          </div>
          <div className="modal-actions">
            <button type="button" className="btn btn-primary" onClick={onClose}>
              Done
            </button>
          </div>
        </div>
      </div>
    );
  }

  const busy = createMutation.isPending;

  return (
    <div className="modal-backdrop" onMouseDown={() => !busy && onClose()}>
      <div className="modal" role="dialog" aria-modal="true" aria-label="Create user" onMouseDown={(e) => e.stopPropagation()}>
        <h3>Create user</h3>
        <form onSubmit={submit}>
          <div className="modal-body">
            {fieldError ? <div className="banner banner-error">{fieldError}</div> : null}

            <div className="field">
              <label htmlFor="create-user-email">Email</label>
              <input
                id="create-user-email"
                className="input"
                type="email"
                autoComplete="off"
                value={email}
                autoFocus
                onChange={(e) => setEmail(e.target.value)}
                disabled={busy}
              />
            </div>

            <div className="field">
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={autoGenerate}
                  onChange={(e) => setAutoGenerate(e.target.checked)}
                  disabled={busy}
                />
                <span>Generate a strong password automatically</span>
              </label>
            </div>

            {!autoGenerate ? (
              <div className="field">
                <label htmlFor="create-user-password">Password</label>
                <input
                  id="create-user-password"
                  className="input"
                  type="text"
                  autoComplete="new-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  disabled={busy}
                />
                <p className="field-hint">At least 8 characters.</p>
              </div>
            ) : null}

            <div className="field">
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={isAdmin}
                  onChange={(e) => setIsAdmin(e.target.checked)}
                  disabled={busy}
                />
                <span>Administrator (full access to all teams)</span>
              </label>
            </div>

            <div className="field">
              <label>Teams</label>
              <TeamCheckboxList
                teams={teams}
                selected={selectedTeams}
                disabled={busy}
                onToggle={toggleTeam}
              />
            </div>
          </div>

          <div className="modal-actions">
            <button type="button" className="btn btn-secondary" onClick={onClose} disabled={busy}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={busy || !email.trim()}>
              {busy ? 'Creating…' : 'Create user'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
