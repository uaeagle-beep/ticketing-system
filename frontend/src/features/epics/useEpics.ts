import { useQuery } from '@tanstack/react-query';
import { epicsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';

// Epics for a single team. Disabled until a team is selected; teamId is
// required by the API (§5.1).
export function useEpics(teamId: string | undefined) {
  return useQuery({
    queryKey: teamId ? queryKeys.epics(teamId) : ['epics', 'none'],
    queryFn: ({ signal }) => epicsApi.list(teamId as string, signal),
    enabled: !!teamId,
  });
}
