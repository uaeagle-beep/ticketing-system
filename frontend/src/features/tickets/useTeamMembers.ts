// Candidate assignees for a team = team members ∪ admins (F-02 eligibility, ADR-0009).
//
// CONTRACT GAP (documented for the Architect): there is no member-visible endpoint that lists a
// team's members (GET /api/admin/users is admin-only; /api/auth/me exposes only the caller's own
// memberships). The minimal consistent choice for Wave 1 is to source the candidate pool from the
// admin users list when the caller is an admin, and to degrade to an EMPTY pool for a non-admin
// member (who then can filter by "Assigned to me" and remove existing assignees, but cannot add
// arbitrary users from the UI). A future backend endpoint (e.g. GET /api/teams/{id}/members,
// member-visible) would let members pick assignees too. The backend already enforces eligibility
// (400 keyed userIds), so this is purely a UI affordance gap, not a security one.

import { useQuery } from '@tanstack/react-query';
import { adminUsersApi } from '@/api/endpoints';
import type { AssigneeRef } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { displayName } from '@/lib/displayName';

/**
 * Returns the eligible-assignee candidates for a team (members ∪ admins), as AssigneeRef[]. Only
 * queries when the caller is an admin (the sole listing source today); otherwise returns an empty
 * pool. Safe to call unconditionally.
 */
export function useTeamMembers(teamId: string | undefined): {
  candidates: AssigneeRef[];
  isLoading: boolean;
  canList: boolean;
} {
  const { user } = useAuth();
  const canList = Boolean(user?.isAdmin);

  const query = useQuery({
    queryKey: ['admin-users-for-assignees'],
    queryFn: ({ signal }) => adminUsersApi.list(signal),
    enabled: canList,
    staleTime: 60_000,
  });

  if (!canList || !teamId) {
    return { candidates: [], isLoading: false, canList };
  }

  const users = query.data ?? [];
  const candidates: AssigneeRef[] = users
    .filter((u) => u.isAdmin || u.teams.some((t) => t.id === teamId))
    .map((u) => ({ id: u.id, displayName: displayName(u.name, u.email) }))
    .sort((a, b) => a.displayName.localeCompare(b.displayName));

  return { candidates, isLoading: query.isLoading, canList };
}
