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
