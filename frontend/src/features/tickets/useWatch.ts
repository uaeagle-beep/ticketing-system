// Watch/unwatch a ticket (Wave 2, §5.4, ADR-0013). Both endpoints are idempotent and return the
// caller's watching flag. On success we invalidate the ticket detail (which carries isWatching) and
// the watchers list. Watching never bumps modified_at, so the board is unaffected.

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ticketsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';

export function useToggleWatch(ticketId: string) {
  const queryClient = useQueryClient();

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.ticket(ticketId) });
    queryClient.invalidateQueries({ queryKey: queryKeys.watchers(ticketId) });
  };

  const watch = useMutation({
    mutationFn: () => ticketsApi.watch(ticketId),
    onSuccess: invalidate,
  });

  const unwatch = useMutation({
    mutationFn: () => ticketsApi.unwatch(ticketId),
    onSuccess: invalidate,
  });

  return {
    watch,
    unwatch,
    isPending: watch.isPending || unwatch.isPending,
    toggle: (currentlyWatching: boolean) =>
      currentlyWatching ? unwatch.mutate() : watch.mutate(),
  };
}
