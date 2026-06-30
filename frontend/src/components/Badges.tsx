import type { TicketType } from '@/api/types';
import { typeLabel } from '@/lib/labels';

// Type badge shown on cards and in lists (Wireframe 1, UPPERCASE).
export function TypeBadge({ type }: { type: TicketType }) {
  return <span className={`badge type-${type}`}>{typeLabel(type).toUpperCase()}</span>;
}

// Small pill count for column headers and panel counts.
export function CountBadge({ count }: { count: number }) {
  return <span className="badge-count">{count}</span>;
}

// Board column count + WIP limit badge (UX §3). Shows plain `N` when unlimited and
// `N / max` when a limit is set, with three visual states conveyed by color PLUS icon
// PLUS the `/max` text (never color alone, WCAG 1.4.1):
//   under limit -> neutral
//   at limit (full)  -> warning treatment + lock icon
//   over limit       -> danger treatment + warning icon
// The icon is decorative (aria-hidden); the full/over status is also surfaced on the
// column's aria-label so screen-reader users learn it without color (see BoardColumn).
export function WipBadge({ count, limit }: { count: number; limit: number | null }) {
  if (limit === null) {
    return <span className="badge-count">{count}</span>;
  }

  const over = count > limit;
  const full = count === limit;
  const cls = over ? 'badge-count is-over' : full ? 'badge-count is-full' : 'badge-count';
  const icon = over ? '▲' : full ? '▣' : null;

  return (
    <span className={cls}>
      {icon ? (
        <span aria-hidden="true" className="badge-count-icon">
          {icon}
        </span>
      ) : null}
      {count} / {limit}
    </span>
  );
}

// Append the WIP full/over status to a column's accessible label so it is announced
// without relying on color (UX §3.2). Returns the base label unchanged when under/unlimited.
export function wipAriaSuffix(count: number, limit: number | null): string {
  if (limit === null) return '';
  if (count > limit) return `, over limit (${count} of ${limit})`;
  if (count === limit) return `, full (${count} of ${limit})`;
  return '';
}
