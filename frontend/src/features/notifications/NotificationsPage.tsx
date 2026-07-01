// Notifications page (Wave 2, §9.2, ADR-0013/0016). Lists notifications newest-first with unread
// styling; clicking a notification navigates to its ticket and marks it read. A null ticketId is a
// deleted-ticket tombstone — non-navigable, but still markable. "Mark all read" clears the badge.
// Refresh = polling + refetch-on-focus (the underlying hooks), no websockets.

import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import type { Notification } from '@/api/types';
import { relativeTime } from '@/lib/time';
import { errorMessage } from '@/lib/errors';
import { LoadingState, ErrorState, EmptyState } from '@/components/States';
import {
  useMarkAllNotificationsRead,
  useMarkNotificationRead,
  useNotificationsList,
} from './useNotifications';

export function NotificationsPage() {
  const { t } = useTranslation('notifications');
  const navigate = useNavigate();
  const listQuery = useNotificationsList();
  const markRead = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();

  const items = listQuery.data?.pages.flatMap((p) => p.items) ?? [];
  const unreadCount = listQuery.data?.pages[0]?.unreadCount ?? 0;

  const open = (n: Notification) => {
    // Mark read regardless of whether it's navigable (tombstones are markable, §9.2).
    if (!n.readAt) markRead.mutate(n.id);
    // A null ticketId is a deleted-ticket tombstone — non-navigable.
    if (n.ticketId) navigate(`/tickets/${n.ticketId}`);
  };

  return (
    <div className="notifications-page">
      <div className="page-header" style={{ marginBottom: 12 }}>
        <h1 style={{ fontSize: 20 }}>{t('title')}</h1>
        <div className="spacer" />
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={() => markAll.mutate()}
          disabled={markAll.isPending || unreadCount === 0}
        >
          {t('markAllRead')}
        </button>
      </div>

      {listQuery.isLoading ? (
        <LoadingState label={t('loading')} />
      ) : listQuery.isError ? (
        <ErrorState message={errorMessage(listQuery.error)} onRetry={() => listQuery.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t('empty.title')} message={t('empty.message')} />
      ) : (
        <>
          <ul className="notification-list" aria-label={t('listLabel')}>
            {items.map((n) => {
              const unread = !n.readAt;
              const tombstone = n.ticketId === null;
              return (
                <li
                  key={n.id}
                  className={`notification-item${unread ? ' unread' : ''}${
                    tombstone ? ' tombstone' : ''
                  }`}
                >
                  <button
                    type="button"
                    className="notification-button"
                    onClick={() => open(n)}
                    // A tombstone with no unread state has nothing to do on click.
                    disabled={tombstone && !unread}
                  >
                    {unread ? <span className="unread-dot" aria-label={t('unread')} /> : null}
                    <span className="notification-summary">{n.summary}</span>
                    <span className="notification-time">{relativeTime(n.createdAt)}</span>
                  </button>
                </li>
              );
            })}
          </ul>

          {listQuery.hasNextPage ? (
            <div className="row" style={{ justifyContent: 'center', marginTop: 12 }}>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => listQuery.fetchNextPage()}
                disabled={listQuery.isFetchingNextPage}
              >
                {listQuery.isFetchingNextPage ? t('loadingMore') : t('loadMore')}
              </button>
            </div>
          ) : null}
        </>
      )}
    </div>
  );
}
