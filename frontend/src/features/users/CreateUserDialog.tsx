// Create-user dialog (API_CONTRACT §8). Email + optional password (or auto-generate), an admin
// toggle, and team membership. On success with a generated password we keep the dialog open to
// show the one-time password; otherwise we close.

import { useState, type FormEvent } from 'react';
import { Trans, useTranslation } from 'react-i18next';
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
  const { t } = useTranslation('users');
  const queryClient = useQueryClient();
  const toast = useToast();

  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
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
        // Blank name => null so the UI shows the email (mirrors the backend).
        name: name.trim() || null,
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
        toast.showSuccess(t('toast.userCreated'));
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
      setFieldError(t('create.emailRequired'));
      return;
    }
    if (!autoGenerate && password.length < 8) {
      setFieldError(t('create.passwordTooShort'));
      return;
    }
    createMutation.mutate();
  };

  // After a successful auto-generated create, show the one-time password screen.
  if (created) {
    return (
      <div className="modal-backdrop" onMouseDown={onClose}>
        <div className="modal" role="dialog" aria-modal="true" aria-label={t('create.createdTitle')} onMouseDown={(e) => e.stopPropagation()}>
          <h3>{t('create.createdTitle')}</h3>
          <div className="modal-body">
            <p>
              <Trans t={t} i18nKey="create.createdBody" values={{ email: created.user.email }}>
                <strong>{created.user.email}</strong> was created.
              </Trans>
            </p>
            {created.generatedPassword ? (
              <GeneratedPasswordNotice password={created.generatedPassword} />
            ) : null}
          </div>
          <div className="modal-actions">
            <button type="button" className="btn btn-primary" onClick={onClose}>
              {t('create.done')}
            </button>
          </div>
        </div>
      </div>
    );
  }

  const busy = createMutation.isPending;

  return (
    <div className="modal-backdrop" onMouseDown={() => !busy && onClose()}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={t('create.title')} onMouseDown={(e) => e.stopPropagation()}>
        <h3>{t('create.title')}</h3>
        <form onSubmit={submit}>
          <div className="modal-body">
            {fieldError ? <div className="banner banner-error">{fieldError}</div> : null}

            <div className="field">
              <label htmlFor="create-user-email">{t('create.email')}</label>
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
              <label htmlFor="create-user-name">{t('create.name')}</label>
              <input
                id="create-user-name"
                className="input"
                type="text"
                autoComplete="off"
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={busy}
                maxLength={100}
              />
              <p className="field-hint">{t('create.nameHint')}</p>
            </div>

            <div className="field">
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={autoGenerate}
                  onChange={(e) => setAutoGenerate(e.target.checked)}
                  disabled={busy}
                />
                <span>{t('create.autoGenerate')}</span>
              </label>
            </div>

            {!autoGenerate ? (
              <div className="field">
                <label htmlFor="create-user-password">{t('create.password')}</label>
                <input
                  id="create-user-password"
                  className="input"
                  type="text"
                  autoComplete="new-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  disabled={busy}
                />
                <p className="field-hint">{t('create.passwordHint')}</p>
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
                <span>{t('create.admin')}</span>
              </label>
            </div>

            <div className="field">
              <label>{t('create.teams')}</label>
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
              {t('create.cancel')}
            </button>
            <button type="submit" className="btn btn-primary" disabled={busy || !email.trim()}>
              {busy ? t('create.creating') : t('create.submit')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
