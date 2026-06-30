// Reset-password dialog (API_CONTRACT §8). Confirm → POST reset → show the new password ONCE with
// a Copy button. Resetting a blocked user is refused by the backend (403 forbidden), surfaced as a
// clear error. Resetting also invalidates the user's existing sessions (server-side).

import { useState } from 'react';
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
      <div className="modal" role="dialog" aria-modal="true" aria-label="Reset password" onMouseDown={(e) => e.stopPropagation()}>
        <h3>Reset password</h3>
        <div className="modal-body">
          {newPassword ? (
            <>
              <p>
                A new password was generated for <strong>{user.email}</strong>. Their existing
                sessions have been signed out.
              </p>
              <GeneratedPasswordNotice password={newPassword} />
            </>
          ) : (
            <p>
              Generate a new password for <strong>{user.email}</strong>? This signs them out of all
              sessions. The new password is shown only once.
            </p>
          )}
        </div>
        <div className="modal-actions">
          {newPassword ? (
            <button type="button" className="btn btn-primary" onClick={onClose}>
              Done
            </button>
          ) : (
            <>
              <button type="button" className="btn btn-secondary" onClick={onClose} disabled={busy}>
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => resetMutation.mutate()}
                disabled={busy}
              >
                {busy ? 'Generating…' : 'Generate new password'}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
