// A single droppable Kanban column. Header is the UPPERCASE state label with a
// count badge (Wireframe 1). Body lists cards (already ordered modified desc by
// the API). Shows a per-column empty state when the team has no tickets in that
// state (EC9 case b).

import { useDroppable } from '@dnd-kit/core';
import type { BoardColumn as BoardColumnModel } from '@/api/types';
import { stateLabel } from '@/lib/labels';
import { WipBadge, wipAriaSuffix } from '@/components/Badges';
import { TicketCard } from './TicketCard';

export function BoardColumn({ column }: { column: BoardColumnModel }) {
  const { setNodeRef, isOver } = useDroppable({
    id: column.state,
    data: { state: column.state },
  });

  // The WIP badge compares against the UNFILTERED per-state total (UX §3.1), not the
  // post-filter card count, so a full column still reads as full while a filter is active.
  const limit = column.wipLimit;
  const total = column.total;
  const full = limit !== null && total >= limit;

  return (
    <section
      ref={setNodeRef}
      className={`board-column${isOver ? ' drop-active' : ''}${full ? ' is-full' : ''}`}
      aria-label={`${stateLabel(column.state)}${wipAriaSuffix(total, limit)}`}
    >
      <header className="board-column-header">
        <span>{stateLabel(column.state).toUpperCase()}</span>
        <WipBadge count={total} limit={limit} />
      </header>
      <div className="board-column-body">
        {column.tickets.length === 0 ? (
          <div className="board-column-empty">No tickets</div>
        ) : (
          column.tickets.map((ticket) => <TicketCard key={ticket.id} ticket={ticket} />)
        )}
      </div>
    </section>
  );
}
