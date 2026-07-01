import { useQuery } from '@tanstack/react-query';
import { analyticsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { DashboardRange } from '@/api/types';

// Analytics dashboard read for a team + optional date range (Wave 3, ADR-0020, §10.3). The endpoint
// returns pre-aggregated counts/buckets, so the client plots a small fixed number of points regardless
// of ticket volume (the "100+ tickets" NFR is satisfied server-side). Disabled until a team is selected.
export function useDashboard(teamId: string | undefined, range: DashboardRange) {
  return useQuery({
    queryKey: teamId
      ? queryKeys.dashboard(teamId, range.from, range.to)
      : ['dashboard', 'none'],
    queryFn: ({ signal }) => analyticsApi.dashboard(teamId as string, range, signal),
    enabled: !!teamId,
    // Keep the previous dashboard visible while a range change refetches so charts don't flash empty.
    placeholderData: (prev) => prev,
  });
}
