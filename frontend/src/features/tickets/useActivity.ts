// Ticket activity timeline hook (Wave 2, §5.5, ADR-0012). Newest-first, keyset-paged. Invalidated by
// the ticket page after any ticket mutation and after comment add/edit/delete so the timeline stays current.

import { useInfiniteQuery } from '@tanstack/react-query';
import { ticketsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { ActivityList } from '@/api/types';

export function useActivity(ticketId: string | undefined) {
  return useInfiniteQuery({
    queryKey: ticketId ? queryKeys.activity(ticketId) : ['activity', 'none'],
    queryFn: ({ pageParam, signal }) =>
      ticketsApi.activity(ticketId as string, pageParam ?? undefined, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage: ActivityList) =>
      lastPage.hasMore ? lastPage.nextCursor ?? undefined : undefined,
    enabled: !!ticketId,
  });
}
