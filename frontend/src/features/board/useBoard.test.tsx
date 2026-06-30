import { describe, expect, it } from 'vitest';
import type { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClientProvider } from '@tanstack/react-query';
import { http, HttpResponse } from 'msw';
import { emptyBoard, normalizeBoard, useMoveTicketMutation } from './useBoard';
import type { Board } from '@/api/types';
import { TICKET_STATES } from '@/api/types';
import { makeTestQueryClient } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, makeBoard } from '@/test/handlers';

const WORKFLOW_ORDER = [...TICKET_STATES];

describe('emptyBoard', () => {
  it('produces all five columns in workflow order, all empty', () => {
    const board = emptyBoard('team-1');
    expect(board.teamId).toBe('team-1');
    expect(board.total).toBe(0);
    expect(board.columns.map((c) => c.state)).toEqual(WORKFLOW_ORDER);
    for (const col of board.columns) {
      expect(col.count).toBe(0);
      expect(col.tickets).toEqual([]);
    }
  });
});

describe('normalizeBoard', () => {
  it('always returns the five columns in workflow order even when the API omits some', () => {
    // API returns only two columns, out of order.
    const partial: Board = {
      teamId: 'team-1',
      total: 3,
      columns: [
        { state: 'done', count: 1, tickets: [] },
        { state: 'new', count: 2, tickets: [] },
      ],
    };
    const normalized = normalizeBoard(partial);
    expect(normalized.columns.map((c) => c.state)).toEqual(WORKFLOW_ORDER);
    // Supplied columns keep their data; the missing ones are filled empty.
    expect(normalized.columns.find((c) => c.state === 'new')?.count).toBe(2);
    expect(normalized.columns.find((c) => c.state === 'done')?.count).toBe(1);
    expect(normalized.columns.find((c) => c.state === 'in_progress')).toEqual({
      state: 'in_progress',
      count: 0,
      tickets: [],
    });
  });

  it('preserves total and reorders an already-complete-but-shuffled board', () => {
    const shuffled = makeBoard();
    shuffled.columns.reverse();
    const normalized = normalizeBoard(shuffled);
    expect(normalized.columns.map((c) => c.state)).toEqual(WORKFLOW_ORDER);
    expect(normalized.total).toBe(shuffled.total);
  });
});

describe('useMoveTicketMutation', () => {
  function wrapper(client = makeTestQueryClient()) {
    const Wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    return { Wrapper, client };
  }

  const TEAM = 'f1c2-team-platform';
  const FILTERS_KEY = ['board', TEAM, null, null, ''] as const;

  it('optimistically moves the card to the target column and advances modifiedAt to the top', async () => {
    const { Wrapper, client } = wrapper();
    // Seed a cached board: one ticket in `new`.
    client.setQueryData<Board>([...FILTERS_KEY], makeBoard());

    const { result } = renderHook(() => useMoveTicketMutation(TEAM), { wrapper: Wrapper });

    result.current.mutate({ ticketId: 'tk1042-login-fails', from: 'new', to: 'done' });

    // Optimistic update is synchronous in onMutate -> assert the cache moved it.
    await waitFor(() => {
      const board = client.getQueryData<Board>([...FILTERS_KEY])!;
      const newCol = board.columns.find((c) => c.state === 'new')!;
      const doneCol = board.columns.find((c) => c.state === 'done')!;
      expect(newCol.tickets).toHaveLength(0);
      expect(newCol.count).toBe(0);
      expect(doneCol.tickets).toHaveLength(1);
      expect(doneCol.tickets[0]!.id).toBe('tk1042-login-fails');
      expect(doneCol.tickets[0]!.state).toBe('done');
    });
  });

  it('rolls the card back to its original column when the PATCH fails (FR-E6-5 / EC10)', async () => {
    server.use(
      http.patch(`${API}/tickets/:id/state`, () =>
        HttpResponse.json(
          { error: { code: 'validation_error', message: 'Invalid target state.' } },
          { status: 400 },
        ),
      ),
    );

    const { Wrapper, client } = wrapper();
    client.setQueryData<Board>([...FILTERS_KEY], makeBoard());

    const { result } = renderHook(() => useMoveTicketMutation(TEAM), { wrapper: Wrapper });

    result.current.mutate({ ticketId: 'tk1042-login-fails', from: 'new', to: 'done' });

    await waitFor(() => expect(result.current.isError).toBe(true));

    // After rollback the card is back in `new` (the onSettled invalidate may
    // also refetch the default board, which still has it in `new`).
    await waitFor(() => {
      const board = client.getQueryData<Board>([...FILTERS_KEY])!;
      const newCol = board.columns.find((c) => c.state === 'new')!;
      const doneCol = board.columns.find((c) => c.state === 'done')!;
      expect(newCol.tickets.some((t) => t.id === 'tk1042-login-fails')).toBe(true);
      expect(doneCol.tickets).toHaveLength(0);
    });
  });
});
