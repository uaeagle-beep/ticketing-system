// Centralized TanStack Query keys (ARCHITECTURE §7), so reads and the
// invalidations after mutations always agree on the same key shape.

import type { BoardFilters } from '@/api/types';

export const queryKeys = {
  me: ['me'] as const,

  teams: ['teams'] as const,

  epics: (teamId: string) => ['epics', teamId] as const,

  // Board key includes the filters so each filter combination is cached
  // independently and refetches when filters change.
  board: (teamId: string, filters: BoardFilters) =>
    [
      'board',
      teamId,
      filters.type ?? null,
      filters.epicId ?? null,
      filters.search ?? '',
      filters.priority ?? null,
      filters.assignedToMe ? 'me' : (filters.assigneeId ?? null),
      filters.dueFilter ?? null,
      filters.labelId ?? null,
    ] as const,

  ticket: (id: string) => ['ticket', id] as const,

  comments: (ticketId: string) => ['comments', ticketId] as const,

  // Wave 3 attachments (ADR-0018): a ticket's attachment metadata list.
  attachments: (ticketId: string) => ['attachments', ticketId] as const,

  // Wave 2 labels (ADR-0016): a team's label set (for pickers + management).
  labels: (teamId: string) => ['labels', teamId] as const,

  // Wave 3 webhooks (ADR-0021): a team's subscriptions + a subscription's delivery audit.
  webhooks: (teamId: string) => ['webhooks', teamId] as const,
  webhookDeliveries: (subscriptionId: string) => ['webhook-deliveries', subscriptionId] as const,

  // Wave 3 API keys (ADR-0021): the caller's personal access tokens (Self).
  apiKeys: ['api-keys'] as const,

  // Wave 3 analytics (ADR-0020): a team's dashboard for a given date range.
  dashboard: (teamId: string, from: string | undefined, to: string | undefined) =>
    ['dashboard', teamId, from ?? null, to ?? null] as const,

  // Wave 2 notifications subsystem.
  notifications: ['notifications'] as const,
  notificationsUnread: ['notifications', 'unread-count'] as const,

  activity: (ticketId: string) => ['activity', ticketId] as const,

  watchers: (ticketId: string) => ['watchers', ticketId] as const,

  notificationSettings: ['notification-settings'] as const,
};
