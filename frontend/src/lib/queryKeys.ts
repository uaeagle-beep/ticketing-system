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
    ['board', teamId, filters.type ?? null, filters.epicId ?? null, filters.search ?? ''] as const,

  ticket: (id: string) => ['ticket', id] as const,

  comments: (ticketId: string) => ['comments', ticketId] as const,
};
