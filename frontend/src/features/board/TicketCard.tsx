// A ticket card on the board (Wireframe 1): TYPE badge, title, epic name,
// relative modified time. Uses @dnd-kit useDraggable.
//
// Two distinct affordances (A11Y-3 — separate name/role/value, no Space clash):
//  - The CARD BODY is the "open" affordance: role="button", Enter or click
//    navigates to the ticket detail/edit view. It is NOT the drag activator, so
//    Space no longer does double duty here.
//  - A dedicated DRAG HANDLE (focusable <button aria-label="Move ticket …">)
//    carries dnd-kit's listeners/attributes via setActivatorNodeRef. Space/Enter
//    on the handle picks the card up for keyboard dragging (A11Y-1); pointer drag
//    on the handle still works for mouse/touch users.

import type { CSSProperties, KeyboardEvent } from 'react';
import { useDraggable } from '@dnd-kit/core';
import { CSS } from '@dnd-kit/utilities';
import { useNavigate } from 'react-router-dom';
import type { TicketCard as TicketCardModel } from '@/api/types';
import { AssigneeAvatars, DueDatePill, LabelChips, PriorityBadge, TypeBadge } from '@/components/Badges';
import { stateLabel } from '@/lib/labels';
import { relativeTime } from '@/lib/time';

export function TicketCard({ ticket }: { ticket: TicketCardModel }) {
  const navigate = useNavigate();
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, isDragging } =
    useDraggable({
      id: ticket.id,
      data: { state: ticket.state },
    });

  const style: CSSProperties = {
    transform: CSS.Translate.toString(transform),
  };

  const open = () => navigate(`/tickets/${ticket.id}`);

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`ticket-card${isDragging ? ' dragging' : ''}`}
      role="button"
      tabIndex={0}
      aria-label={`Open ticket: ${ticket.title}`}
      // Open the ticket on click / Enter only. Space is intentionally NOT handled
      // here so it can't conflict with the drag handle's pick-up gesture (A11Y-3).
      onClick={open}
      onKeyDown={(e: KeyboardEvent<HTMLDivElement>) => {
        if (e.key === 'Enter') {
          e.preventDefault();
          open();
        }
      }}
    >
      <div className="ticket-card-top">
        <div className="ticket-card-badges">
          <TypeBadge type={ticket.type} />
          <PriorityBadge priority={ticket.priority} />
        </div>
        <button
          type="button"
          ref={setActivatorNodeRef}
          className="ticket-card-handle"
          // aria-label includes the source column so AT users know the starting state.
          aria-label={`Move ticket: ${ticket.title} (currently ${stateLabel(ticket.state)})`}
          title="Move ticket"
          // Keep handle activation from also triggering the card's open handlers.
          onClick={(e) => e.stopPropagation()}
          {...listeners}
          {...attributes}
        >
          <span aria-hidden="true">⠿</span>
        </button>
      </div>
      <div className="ticket-card-title">{ticket.title}</div>
      {ticket.labels.length > 0 ? (
        <div className="ticket-card-subrow">
          <LabelChips labels={ticket.labels} />
        </div>
      ) : null}
      {ticket.dueDate || ticket.assignees.length > 0 ? (
        <div className="ticket-card-subrow">
          {ticket.dueDate ? (
            <DueDatePill dueDate={ticket.dueDate} isOverdue={ticket.isOverdue} />
          ) : null}
          <AssigneeAvatars assignees={ticket.assignees} />
        </div>
      ) : null}
      <div className="ticket-card-meta">
        <span className="ticket-card-epic" title={ticket.epicTitle ?? undefined}>
          {ticket.epicTitle ?? 'No epic'}
        </span>
        <span className="nowrap">{relativeTime(ticket.modifiedAt)}</span>
      </div>
    </div>
  );
}

// Non-interactive presentation used inside the DragOverlay while dragging.
export function TicketCardPreview({ ticket }: { ticket: TicketCardModel }) {
  return (
    <div className="ticket-card drag-overlay-card">
      <div className="ticket-card-top">
        <div className="ticket-card-badges">
          <TypeBadge type={ticket.type} />
          <PriorityBadge priority={ticket.priority} />
        </div>
      </div>
      <div className="ticket-card-title">{ticket.title}</div>
      {ticket.labels.length > 0 ? (
        <div className="ticket-card-subrow">
          <LabelChips labels={ticket.labels} />
        </div>
      ) : null}
      {ticket.dueDate || ticket.assignees.length > 0 ? (
        <div className="ticket-card-subrow">
          {ticket.dueDate ? (
            <DueDatePill dueDate={ticket.dueDate} isOverdue={ticket.isOverdue} />
          ) : null}
          <AssigneeAvatars assignees={ticket.assignees} />
        </div>
      ) : null}
      <div className="ticket-card-meta">
        <span className="ticket-card-epic">{ticket.epicTitle ?? 'No epic'}</span>
        <span className="nowrap">{relativeTime(ticket.modifiedAt)}</span>
      </div>
    </div>
  );
}
