import { useQuery } from '@tanstack/react-query';
import { teamsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';

// Shared teams query, reused by the board team selector, team management,
// and epic management screens.
export function useTeams() {
  return useQuery({
    queryKey: queryKeys.teams,
    queryFn: ({ signal }) => teamsApi.list(signal),
  });
}
