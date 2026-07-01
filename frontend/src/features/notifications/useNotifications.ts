// Notifications hooks (Wave 2, ADR-0013/0016; Wave 3 real-time, ADR-0019). Refresh strategy = SignalR
// push (primary) + polling fallback: the bell is pushed a `notify` ping over the hub and refetches
// instantly, and it ALSO polls the unread-count as a safety net. When the hub is Connected the poll is
// THROTTLED from ~30s to ~120s (push does the work); when disconnected/reconnecting it reverts to the
// Wave-2 30s cadence so a dropped socket never leaves the badge stale ([ASSUMPTION W3-RT-FALLBACK]).
// The list uses keyset (cursor) pagination via useInfiniteQuery. Mark-read/mark-all mutations invalidate
// the unread-count and list so the badge stays in sync.

import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { notificationsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import { useRealtime } from '@/features/realtime/RealtimeProvider';
import type { NotificationList } from '@/api/types';

// Poll intervals for the bell badge (ADR-0016 / ADR-0019). Fast when the push socket is down (the poll IS
// the freshness source); slow safety net when connected (push drives updates, §ASSUMPTION W3-RT-FALLBACK).
const UNREAD_POLL_MS = 30_000;
const UNREAD_POLL_MS_CONNECTED = 120_000;

/** The unread-count poll target for the header bell badge. */
export function useUnreadCount() {
  const { connected } = useRealtime();
  return useQuery({
    queryKey: queryKeys.notificationsUnread,
    queryFn: ({ signal }) => notificationsApi.unreadCount(signal),
    // Throttle polling while the hub is connected; revert to the fast cadence when it drops.
    refetchInterval: connected ? UNREAD_POLL_MS_CONNECTED : UNREAD_POLL_MS,
    refetchOnWindowFocus: true,
    // The badge is a lightweight, always-current signal — keep it fresh.
    staleTime: 0,
  });
}

/** Paged notification list (newest-first), keyset cursor pagination. */
export function useNotificationsList() {
  return useInfiniteQuery({
    queryKey: queryKeys.notifications,
    queryFn: ({ pageParam, signal }) =>
      notificationsApi.list(pageParam ?? undefined, 20, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage: NotificationList) =>
      lastPage.hasMore ? lastPage.nextCursor ?? undefined : undefined,
    refetchOnWindowFocus: true,
  });
}

/** Mark a single notification read; invalidate the badge + list. */
export function useMarkNotificationRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => notificationsApi.markRead(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.notificationsUnread });
      queryClient.invalidateQueries({ queryKey: queryKeys.notifications });
    },
  });
}

/** Mark all of the caller's notifications read; invalidate the badge + list. */
export function useMarkAllNotificationsRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => notificationsApi.markAllRead(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.notificationsUnread });
      queryClient.invalidateQueries({ queryKey: queryKeys.notifications });
    },
  });
}
