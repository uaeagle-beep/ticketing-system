// Edit-user dialog (API_CONTRACT §8): toggle the admin role and replace the team membership set.
// Role and teams are saved independently (two endpoints). The backend rejects demoting the last
// active admin with 409 last_admin_required, surfaced here as a clear error.

import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { adminUsersApi } from '@/api/endpoints';
import type { AdminUser, Team } from '@/api/types';
import { errorMessage, isApiErrorCode } from '@/lib/errors';
import { useToast } from '@/components/toast/ToastContext';
import { usersQueryKey } from './useUsers';
import { TeamCheckboxList } from './TeamCheckboxList';

interface EditUserDialogProps {
  user: AdminUser;
  teams: Team[];
  onClose: () => void;
}

export function EditUserDialog({ user, teams, onClose }: EditUserDialogProps) {
  const queryClient = useQueryClient();
  const toast = useToast();

  const [isAdmin, setIsAdmin] = useState(user.isAdmin);
  const [selectedTeams, setSelectedTeams] = useState<Set<string>>(
    () => new Set(user.teams.map((t) => t.id)),
  );

  const invalidate = () => queryClient.invalidateQueries({ queryKey: usersQueryKey });

  const saveMutation = useMutation({
    mutationFn: async () => {
      // Save role first (it can fail with last_admin_required); only then teams.
      if (isAdmin !== user.isAdmin) {
        await adminUsersApi.setRole(user.id, { isAdmin });
      }
      const before = new Set(user.teams.map((t) => t.id));
      const after = selectedTeams;
      const changed =
        before.size !== after.size || [...after].some((id) => !before.has(id));
      if (changed) {
        await adminUsersApi.setTeams(user.id, { teamIds: Array.from(after) });
      }
    },
    onSuccess: () => {
      toast.showSuccess('User updated.');
      invalidate();
      onClose();
    },
    onError: (err) => {
      if (isApiErrorCode(err, 'last_admin_required')) {
        // Revert the local role toggle so the UI matches the rejected change.
        setIsAdmin(user.isAdmin);
      }
      toast.showError(errorMessage(err));
      invalidate();
    },
  });

  const toggleTeam = (teamId: string) =>
    setSelectedTeams((prev) => {
      const next = new Set(prev);
      if (next.has(teamId)) next.delete(teamId);
      else next.add(teamId);
      return next;
    });

  const busy = saveMutation.isPending;

  return (
    <div className="modal-backdrop" onMouseDown={() => !busy && onClose()}>
      <div className="modal" role="dialog" aria-modal="true" aria-label="Edit user" onMouseDown={(e) => e.stopPropagation()}>
        <h3>Edit user</h3>
        <div className="modal-body">
          <p>
            <strong>{user.email}</strong>
          </p>

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
            <p className="field-hint">
              Admins ignore team scoping. The system always keeps at least one active admin.
            </p>
          </div>

          <div className="field">
            <label>Teams</label>
            {isAdmin ? (
              <p className="field-hint">Admins can access all teams; membership is optional.</p>
            ) : null}
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
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => saveMutation.mutate()}
            disabled={busy}
          >
            {busy ? 'Saving…' : 'Save changes'}
          </button>
        </div>
      </div>
    </div>
  );
}
