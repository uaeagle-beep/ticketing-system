// Kanban board — the primary screen (Wireframe 1, US-BOARD-*).
//
// Responsibilities:
//  - Team selector (selected team persisted in the URL ?team= so refresh keeps it).
//  - "+ New ticket" entry to ticket creation (team prefilled).
//  - Filter bar (type, epic, title search) with AND logic + total count + Clear.
//  - Exactly five droppable columns in workflow order, each with a count badge.
//  - Drag-and-drop between columns with immediate persist; optimistic with
//    rollback + error toast on failure (FR-E6-5 / EC10).
//  - Three distinct empty states (EC9): no teams; team with no tickets; filtered-to-empty.

import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type Announcements,
  type DragEndEvent,
  type DragStartEvent,
  type ScreenReaderInstructions,
} from '@dnd-kit/core';
import type { BoardFilters, TicketCard as TicketCardModel, TicketState } from '@/api/types';
import { TICKET_STATES } from '@/api/types';
import { stateLabel } from '@/lib/labels';
import { useTeams } from '@/features/teams/useTeams';
import { useEpics } from '@/features/epics/useEpics';
import { useBoardQuery, useMoveTicketMutation, emptyBoard, normalizeBoard } from './useBoard';
import { BoardColumn } from './BoardColumn';
import { FilterBar } from './FilterBar';
import { TicketCardPreview } from './TicketCard';
import { boardKeyboardCoordinates } from './keyboardCoordinates';
import { LoadingState, EmptyState, ErrorState } from '@/components/States';
import { useToast } from '@/components/toast/ToastContext';
import { errorMessage } from '@/lib/errors';

function isTicketState(value: string): value is TicketState {
  return (TICKET_STATES as readonly string[]).includes(value);
}

// Remember the last board team the user selected so a return visit reopens it ([ПРИПУЩЕННЯ UM-9]).
// localStorage is a convenience only — the server is authoritative about which teams are accessible.
const LAST_TEAM_KEY = 'tt.board.lastTeamId';
function readLastTeamId(): string | null {
  try {
    return window.localStorage.getItem(LAST_TEAM_KEY);
  } catch {
    return null;
  }
}
function writeLastTeamId(teamId: string): void {
  try {
    window.localStorage.setItem(LAST_TEAM_KEY, teamId);
  } catch {
    /* localStorage may be unavailable (private mode); selection just won't persist */
  }
}

// Resolve a droppable/column id (or a card's data) to a human column label for
// screen-reader announcements. `over.id` is a column state for an empty column,
// or a card id when hovering a populated column — in the latter case the column
// state rides along in `over.data.current.state`.
function columnLabel(id: string | undefined, fallbackState?: TicketState): string {
  if (id && isTicketState(id)) return stateLabel(id);
  if (fallbackState) return stateLabel(fallbackState);
  return 'unknown column';
}

const screenReaderInstructions: ScreenReaderInstructions = {
  draggable: `To move a ticket between columns with the keyboard: focus its "Move ticket" handle and press Space or Enter to pick it up. Use the Left and Right arrow keys to choose a column, then press Space or Enter again to drop it. Press Escape to cancel. Click a card (or press Enter on it) to open the ticket.`,
};

export function BoardPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();

  const teamsQuery = useTeams();
  const teams = teamsQuery.data ?? [];

  // Selected team resolution (ADR-0007, [ПРИПУЩЕННЯ UM-9]): the URL ?team= wins (so refresh/links
  // keep the team); else the last team the user picked (localStorage); else the first team. A team
  // the user can no longer access (e.g. membership changed) falls through to the next candidate.
  const teamParam = searchParams.get('team') ?? undefined;
  const selectedTeamId = useMemo(() => {
    if (teamParam && teams.some((t) => t.id === teamParam)) return teamParam;
    const lastTeamId = readLastTeamId();
    if (lastTeamId && teams.some((t) => t.id === lastTeamId)) return lastTeamId;
    return teams[0]?.id;
  }, [teamParam, teams]);

  // Remember the resolved team so the next visit (without a ?team= link) reopens it.
  useEffect(() => {
    if (selectedTeamId) writeLastTeamId(selectedTeamId);
  }, [selectedTeamId]);

  const [filters, setFilters] = useState<BoardFilters>({});

  const epicsQuery = useEpics(selectedTeamId);
  const epics = epicsQuery.data ?? [];

  const boardQuery = useBoardQuery(selectedTeamId, filters);
  const moveMutation = useMoveTicketMutation(selectedTeamId);

  const [activeCard, setActiveCard] = useState<TicketCardModel | null>(null);

  // Require a small movement before a pointer drag starts, so a plain click on a
  // card navigates to the ticket instead of being swallowed as a drag. The
  // KeyboardSensor (activated from each card's dedicated "Move ticket" handle)
  // makes column moves possible without a pointer (A11Y-1); its coordinate getter
  // steps the dragged card between columns with the arrow keys (A11Y-1/A11Y-3).
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: boardKeyboardCoordinates }),
  );

  const board = boardQuery.data
    ? normalizeBoard(boardQuery.data)
    : selectedTeamId
      ? emptyBoard(selectedTeamId)
      : null;

  const hasActiveFilters = Boolean(filters.type || filters.epicId || filters.search);

  const handleTeamChange = (teamId: string) => {
    setFilters({}); // Filters are team-scoped (epics differ per team); reset on switch.
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('team', teamId);
      return next;
    });
  };

  const handleDragStart = (event: DragStartEvent) => {
    const id = String(event.active.id);
    const card = board?.columns
      .flatMap((c) => c.tickets)
      .find((t) => t.id === id);
    setActiveCard(card ?? null);
  };

  const handleDragEnd = (event: DragEndEvent) => {
    setActiveCard(null);
    const { active, over } = event;
    if (!over) return;

    const ticketId = String(active.id);
    const from = active.data.current?.state as TicketState | undefined;
    const overId = String(over.id);
    const to = isTicketState(overId) ? overId : (over.data.current?.state as TicketState | undefined);

    if (!from || !to || from === to) return;

    moveMutation.mutate(
      { ticketId, from, to },
      {
        onError: (err) => {
          // Card already rolled back in the mutation's onError; surface the error.
          toast.showError(errorMessage(err));
        },
      },
    );
  };

  // Screen-reader announcements for the whole drag lifecycle (A11Y-1). dnd-kit
  // pipes these into its built-in aria-live region. `active.data.current.state`
  // and `over.data.current.state` carry the source/target column state.
  const ticketName = (id: string | undefined) =>
    board?.columns.flatMap((c) => c.tickets).find((t) => t.id === id)?.title ?? 'ticket';

  const announcements: Announcements = {
    onDragStart: ({ active }) => {
      const from = active.data.current?.state as TicketState | undefined;
      return `Picked up ticket "${ticketName(String(active.id))}" from the ${columnLabel(
        undefined,
        from,
      )} column. Use the left and right arrow keys to choose a column, then press Space or Enter to drop.`;
    },
    onDragOver: ({ active, over }) => {
      if (!over) return undefined;
      const to = columnLabel(String(over.id), over.data.current?.state as TicketState | undefined);
      return `Ticket "${ticketName(String(active.id))}" is over the ${to} column.`;
    },
    onDragEnd: ({ active, over }) => {
      const name = ticketName(String(active.id));
      if (!over) return `Ticket "${name}" was dropped. It stayed in its original column.`;
      const to = columnLabel(String(over.id), over.data.current?.state as TicketState | undefined);
      return `Ticket "${name}" was dropped into the ${to} column.`;
    },
    onDragCancel: ({ active }) =>
      `Move cancelled. Ticket "${ticketName(String(active.id))}" stayed in its original column.`,
  };

  // ---- Render branches ----

  if (teamsQuery.isLoading) {
    return <LoadingState label="Loading teams…" />;
  }

  if (teamsQuery.isError) {
    return (
      <ErrorState message={errorMessage(teamsQuery.error)} onRetry={() => teamsQuery.refetch()} />
    );
  }

  // EC9 (a): no teams at all.
  if (teams.length === 0) {
    return (
      <EmptyState
        title="No teams yet"
        message="Create your first team to start tracking tickets."
        action={
          <button type="button" className="btn btn-primary" onClick={() => navigate('/teams')}>
            Go to Team management
          </button>
        }
      />
    );
  }

  return (
    <div>
      <div className="board-toolbar">
        <div className="field" style={{ margin: 0 }}>
          <select
            className="select"
            aria-label="Select team"
            value={selectedTeamId ?? ''}
            onChange={(e) => handleTeamChange(e.target.value)}
          >
            {teams.map((team) => (
              <option key={team.id} value={team.id}>
                {team.name}
              </option>
            ))}
          </select>
        </div>
        <div className="spacer" />
        <button
          type="button"
          className="btn btn-primary"
          onClick={() =>
            navigate(selectedTeamId ? `/tickets/new?team=${selectedTeamId}` : '/tickets/new')
          }
        >
          + New ticket
        </button>
      </div>

      <FilterBar
        filters={filters}
        epics={epics}
        epicsLoading={epicsQuery.isLoading}
        total={board?.total ?? 0}
        onChange={setFilters}
        onClear={() => setFilters({})}
      />

      {boardQuery.isError ? (
        <ErrorState message={errorMessage(boardQuery.error)} onRetry={() => boardQuery.refetch()} />
      ) : boardQuery.isLoading && !board ? (
        <LoadingState label="Loading board…" />
      ) : board ? (
        <>
          {/* EC9 (c): filtered-to-empty — distinct from a team with no tickets. */}
          {hasActiveFilters && board.total === 0 ? (
            <EmptyState
              title="No matching tickets"
              message="No tickets match the current filters."
              action={
                <button type="button" className="btn btn-secondary" onClick={() => setFilters({})}>
                  Clear filters
                </button>
              }
            />
          ) : /* EC9 (b): team selected, no tickets at all. */ !hasActiveFilters &&
            board.total === 0 ? (
            <EmptyState
              title="No tickets on this board"
              message="Create a ticket to get started."
              action={
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() =>
                    navigate(
                      selectedTeamId ? `/tickets/new?team=${selectedTeamId}` : '/tickets/new',
                    )
                  }
                >
                  + New ticket
                </button>
              }
            />
          ) : (
            <DndContext
              sensors={sensors}
              accessibility={{ announcements, screenReaderInstructions }}
              onDragStart={handleDragStart}
              onDragEnd={handleDragEnd}
              onDragCancel={() => setActiveCard(null)}
            >
              <div className="board-columns">
                {board.columns.map((column) => (
                  <BoardColumn key={column.state} column={column} />
                ))}
              </div>
              <DragOverlay>
                {activeCard ? <TicketCardPreview ticket={activeCard} /> : null}
              </DragOverlay>
            </DndContext>
          )}
        </>
      ) : null}
    </div>
  );
}
