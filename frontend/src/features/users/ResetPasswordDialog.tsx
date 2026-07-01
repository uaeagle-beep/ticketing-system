// Reset-password dialog (API_CONTRACT §8). Confirm → POST reset → show the new password ONCE with
// a Copy button. Resetting a blocked user is refused by the backend (403 forbidden), surfaced as a
// clear error. Resetting also invalidates the user's existing sessions (server-side).

import { useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { adminUsersApi } from '@/api/endpoints';
import type { AdminUser } from '@/api/types';
import { errorMessage } from '@/lib/errors';
import { useToast } from '@/components/toast/ToastContext';
import { usersQueryKey } from './useUsers';
import { GeneratedPasswordNotice } from './GeneratedPasswordNotice';

interface ResetPasswordDialogProps {
  user: AdminUser;
  onClose: () => void;
}

export function ResetPasswordDialog({ user, onClose }: ResetPasswordDialogProps) {
  const { t } = useTranslation('users');
  const queryClient = useQueryClient();
  const toast = useToast();
  const [newPassword, setNewPassword] = useState<string | null>(null);

  const resetMutation = useMutation({
    mutationFn: () => adminUsersApi.resetPassword(user.id),
    onSuccess: (result) => {
      setNewPassword(result.generatedPassword);
      queryClient.invalidateQueries({ queryKey: usersQueryKey });
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const busy = resetMutation.isPending;

  return (
    <div className="modal-backdrop" onMouseDown={() => !busy && onClose()}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={t('reset.title')} onMouseDown={(e) => e.stopPropagation()}>
        <h3>{t('reset.title')}</h3>
        <div className="modal-body">
          {newPassword ? (
            <>
              <p>
                <Trans t={t} i18nKey="reset.generatedBody" values={{ email: user.email }}>
                  A new password was generated for <strong>{user.email}</strong>. Their
                  existing sessions have been signed out.
                </Trans>
              </p>
              <GeneratedPasswordNotice password={newPassword} />
            </>
          ) : (
            <p>
              <Trans t={t} i18nKey="reset.confirmBody" values={{ email: user.email }}>
                Generate a new password for <strong>{user.email}</strong>? This signs them
                out of all sessions. The new password is shown only once.
              </Trans>
            </p>
          )}
        </div>
        <div className="modal-actions">
          {newPassword ? (
            <button type="button" className="btn btn-primary" onClick={onClose}>
              {t('reset.done')}
            </button>
          ) : (
            <>
              <button type="button" className="btn btn-secondary" onClick={onClose} disabled={busy}>
                {t('reset.cancel')}
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => resetMutation.mutate()}
                disabled={busy}
              >
                {busy ? t('reset.generating') : t('reset.generate')}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
