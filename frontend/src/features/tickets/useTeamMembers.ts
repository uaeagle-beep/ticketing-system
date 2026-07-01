// Candidate assignees for a team = team members ∪ admins (F-02 eligibility, ADR-0009).
//
// Wave 2 (§8bis / ADR-0017): this now sources from the member-visible endpoint
// GET /api/teams/{id}/members, so a NON-admin member can pick teammates too (closing the Wave-1
// gap where the pool degraded to empty for members). The backend still enforces eligibility
// server-side (400 keyed userIds on assign), so this is purely the UI candidate pool. Admins are
// global and use the admin surface; the response is the team's members only (server decision).

import { useQuery } from '@tanstack/react-query';
import { teamsApi } from '@/api/endpoints';
import type { AssigneeRef } from '@/api/types';

/**
 * Returns the eligible-assignee candidates for a team (its members), as AssigneeRef[]. Queries for
 * any team member (no admin gate), enabled only when a teamId is given. `canList` stays true whenever
 * a team is selected so callers can render the picker for members and admins alike. Safe to call
 * unconditionally.
 */
export function useTeamMembers(teamId: string | undefined): {
  candidates: AssigneeRef[];
  isLoading: boolean;
  canList: boolean;
} {
  const query = useQuery({
    queryKey: ['team-members', teamId],
    queryFn: ({ signal }) => teamsApi.members(teamId as string, signal),
    enabled: !!teamId,
    staleTime: 60_000,
  });

  if (!teamId) {
    return { candidates: [], isLoading: false, canList: false };
  }

  const members = query.data ?? [];
  // displayName is already computed server-side (name?.trim() || email); do not recompute.
  const candidates: AssigneeRef[] = members
    .map((m) => ({ id: m.id, displayName: m.displayName }))
    .sort((a, b) => a.displayName.localeCompare(b.displayName));

  return { candidates, isLoading: query.isLoading, canList: true };
}
