import type { AssigneeRef, TicketPriority, TicketType } from '@/api/types';
import { priorityLabel, typeLabel } from '@/lib/labels';
import { formatDueDate } from '@/lib/time';

// Type badge shown on cards and in lists (Wireframe 1, UPPERCASE).
export function TypeBadge({ type }: { type: TicketType }) {
  return <span className={`badge type-${type}`}>{typeLabel(type).toUpperCase()}</span>;
}

// Priority badge (F-03). Color by priority; UPPERCASE like the type badge (A2). The label text
// (never color alone) conveys the value for WCAG 1.4.1.
export function PriorityBadge({ priority }: { priority: TicketPriority }) {
  return (
    <span className={`badge priority-${priority}`}>{priorityLabel(priority).toUpperCase()}</span>
  );
}

// Due-date pill (F-08). When overdue, a danger treatment plus an "Overdue" prefix conveys status
// without relying on color alone (WCAG 1.4.1). `isOverdue` is backend-computed and authoritative.
export function DueDatePill({ dueDate, isOverdue }: { dueDate: string; isOverdue: boolean }) {
  const label = formatDueDate(dueDate);
  return (
    <span
      className={`due-pill${isOverdue ? ' is-overdue' : ''}`}
      title={isOverdue ? `Overdue — was due ${label}` : `Due ${label}`}
    >
      <span aria-hidden="true" className="due-pill-icon">
        {isOverdue ? '⚠' : '📅'}
      </span>
      {isOverdue ? `Overdue: ${label}` : `Due ${label}`}
    </span>
  );
}

// Assignee avatars/initials (F-02). Shows up to `max` initial circles, then "+N". Each carries the
// display name as its title/aria-label so the person is identifiable without color.
export function AssigneeAvatars({
  assignees,
  max = 3,
}: {
  assignees: AssigneeRef[];
  max?: number;
}) {
  if (assignees.length === 0) return null;
  const shown = assignees.slice(0, max);
  const extra = assignees.length - shown.length;
  return (
    <span className="assignee-avatars" aria-label={`Assignees: ${assignees.map((a) => a.displayName).join(', ')}`}>
      {shown.map((a) => (
        <span key={a.id} className="assignee-avatar" title={a.displayName} aria-hidden="true">
          {a.displayName.charAt(0).toUpperCase() || '?'}
        </span>
      ))}
      {extra > 0 ? (
        <span className="assignee-avatar assignee-avatar-more" aria-hidden="true">
          +{extra}
        </span>
      ) : null}
    </span>
  );
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
