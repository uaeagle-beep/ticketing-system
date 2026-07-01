// Ticket activity timeline (Wave 2, §9.3, ADR-0012). A chronological (newest-first) list of the
// ticket's events rendered from the server-rendered summary + relative time. Keyset "load more".

import { useTranslation } from 'react-i18next';
import { relativeTime } from '@/lib/time';
import { errorMessage } from '@/lib/errors';
import { LoadingState } from '@/components/States';
import { CountBadge } from '@/components/Badges';
import { useActivity } from './useActivity';

export function ActivityTimeline({ ticketId }: { ticketId: string }) {
  const { t } = useTranslation('tickets');
  const activityQuery = useActivity(ticketId);
  const items = activityQuery.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div className="panel">
      <div className="row" style={{ marginBottom: 12 }}>
        <h3 style={{ fontSize: 16 }}>{t('activity.title')}</h3>
        <CountBadge count={items.length} />
      </div>

      {activityQuery.isLoading ? (
        <LoadingState label={t('activity.loading')} />
      ) : activityQuery.isError ? (
        <div className="banner banner-error">{errorMessage(activityQuery.error)}</div>
      ) : items.length === 0 ? (
        <p className="muted">{t('activity.empty')}</p>
      ) : (
        <ul className="activity-list">
          {items.map((a) => (
            <li key={a.id} className="activity-item">
              <span className="activity-summary">{a.summary}</span>
              <span className="activity-time">{relativeTime(a.createdAt)}</span>
            </li>
          ))}
        </ul>
      )}

      {activityQuery.hasNextPage ? (
        <div className="row" style={{ justifyContent: 'center', marginTop: 8 }}>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => activityQuery.fetchNextPage()}
            disabled={activityQuery.isFetchingNextPage}
          >
            {activityQuery.isFetchingNextPage ? t('activity.loadingMore') : t('activity.loadMore')}
          </button>
        </div>
      ) : null}
    </div>
  );
}
