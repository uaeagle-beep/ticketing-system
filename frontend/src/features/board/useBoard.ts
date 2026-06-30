import { useMutation, useQuery, useQueryClient, type QueryKey } from '@tanstack/react-query';
import { ticketsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { Board, BoardColumn, BoardFilters, TicketState } from '@/api/types';
import { orderedStates } from '@/lib/labels';

// Board read for a team + active filters. Server-side filtering & grouping is
// preferred at scale (NFR-PERF-1); the API returns all five columns in workflow
// order with post-filter counts (A23).
export function useBoardQuery(teamId: string | undefined, filters: BoardFilters) {
  return useQuery({
    queryKey: teamId ? queryKeys.board(teamId, filters) : ['board', 'none'],
    queryFn: ({ signal }) => ticketsApi.board(teamId as string, filters, signal),
    enabled: !!teamId,
    // Keep showing the previous board while a filtered refetch is in flight so
    // the columns don't flash empty as the user types in the search box.
    placeholderData: (prev) => prev,
  });
}

// Move a card to a new column (drag-drop). Optimistic update with rollback on
// error (FR-E6-5, EC10). We snapshot every cached board query for this team,
// move the card across columns, advance its modifiedAt locally so it sorts to
// the top of the target column (A22), and restore the snapshot if the PATCH fails.
interface MoveArgs {
  ticketId: string;
  from: TicketState;
  to: TicketState;
}

function moveCardInBoard(board: Board, ticketId: string, to: TicketState): Board {
  let moved: BoardColumn['tickets'][number] | undefined;
  let from: TicketState | undefined;

  // Remove the card from whatever column currently holds it. Also decrement that
  // column's unfiltered `total` so the WIP badge "N / max" updates optimistically (UX §3.1).
  const stripped = board.columns.map((col) => {
    const idx = col.tickets.findIndex((t) => t.id === ticketId);
    if (idx === -1) return col;
    moved = col.tickets[idx];
    from = col.state;
    const tickets = col.tickets.filter((t) => t.id !== ticketId);
    return { ...col, tickets, count: tickets.length, total: Math.max(0, col.total - 1) };
  });

  if (!moved || from === to) return board;

  const movedCard = { ...moved, state: to, modifiedAt: new Date().toISOString() };

  // Insert into the target column, re-sort by modifiedAt desc, and bump its unfiltered total.
  const columns = stripped.map((col) => {
    if (col.state !== to) return col;
    const tickets = [...col.tickets, movedCard].sort(
      (a, b) => new Date(b.modifiedAt).getTime() - new Date(a.modifiedAt).getTime(),
    );
    return { ...col, tickets, count: tickets.length, total: col.total + 1 };
  });

  return { ...board, columns };
}

export function useMoveTicketMutation(teamId: string | undefined) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ ticketId, to }: MoveArgs) =>
      ticketsApi.patchState(ticketId, { state: to }),

    onMutate: async ({ ticketId, to }) => {
      if (!teamId) return { snapshots: [] as Array<[QueryKey, Board | undefined]> };
      // Cancel in-flight board refetches so they don't clobber the optimistic state.
      await queryClient.cancelQueries({ queryKey: ['board', teamId] });

      // Snapshot + patch every cached board variant (different filter combos).
      const entries = queryClient.getQueriesData<Board>({ queryKey: ['board', teamId] });
      const snapshots: Array<[QueryKey, Board | undefined]> = [];
      for (const [key, data] of entries) {
        snapshots.push([key, data]);
        if (data) {
          queryClient.setQueryData<Board>(key, moveCardInBoard(data, ticketId, to));
        }
      }
      return { snapshots };
    },

    onError: (_err, _vars, context) => {
      // Roll the card back to its previous column (FR-E6-5). The caller shows
      // the error toast.
      if (!context) return;
      for (const [key, data] of context.snapshots) {
        queryClient.setQueryData(key, data);
      }
    },

    onSettled: () => {
      // Re-sync with the server (authoritative modifiedAt ordering).
      if (teamId) {
        queryClient.invalidateQueries({ queryKey: ['board', teamId] });
      }
    },
  });
}

// Build a board with five empty columns in workflow order. Used as a stable
// fallback so we always render exactly five columns (FR-E6-2) even before data.
export function emptyBoard(teamId: string): Board {
  return {
    teamId,
    total: 0,
    columns: orderedStates.map((state) => ({
      state,
      count: 0,
      total: 0,
      wipLimit: null,
      tickets: [],
    })),
  };
}

// Normalize a board response so columns are always present and in workflow
// order, defensively (in case the API omits an empty column).
export function normalizeBoard(board: Board): Board {
  const byState = new Map(board.columns.map((c) => [c.state, c]));
  const columns: BoardColumn[] = orderedStates.map(
    (state) => byState.get(state) ?? { state, count: 0, total: 0, wipLimit: null, tickets: [] },
  );
  return { ...board, columns };
}
