// Notifications hooks (Wave 2, ADR-0013/0016). Refresh strategy = polling + refetch-on-focus (no
// websockets): the bell polls unread-count every ~30s and refetches when the window regains focus.
// The list uses keyset (cursor) pagination via useInfiniteQuery. Mark-read/mark-all mutations
// invalidate the unread-count and list so the badge stays in sync.

import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { notificationsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { NotificationList } from '@/api/types';

// Poll interval for the bell badge (ADR-0016). 30s + refetch-on-focus keeps it "instant-ish".
const UNREAD_POLL_MS = 30_000;

/** The unread-count poll target for the header bell badge. */
export function useUnreadCount() {
  return useQuery({
    queryKey: queryKeys.notificationsUnread,
    queryFn: ({ signal }) => notificationsApi.unreadCount(signal),
    refetchInterval: UNREAD_POLL_MS,
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
