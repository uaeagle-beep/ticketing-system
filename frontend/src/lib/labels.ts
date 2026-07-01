// Mapping between canonical API enum values (lowercase) and human-readable UI
// labels (A2). The API ALWAYS receives/returns canonical lowercase; the UI only
// ever displays the labels here. Column headers and type badges render UPPERCASE
// per Wireframe 1 — callers apply text-transform / .toUpperCase() at the edge.

import type { DueFilter, TicketPriority, TicketState, TicketType } from '@/api/types';
import { DUE_FILTERS, TICKET_PRIORITIES, TICKET_STATES, TICKET_TYPES } from '@/api/types';

const STATE_LABELS: Record<TicketState, string> = {
  new: 'New',
  ready_for_implementation: 'Ready for implementation',
  in_progress: 'In progress',
  ready_for_acceptance: 'Ready for acceptance',
  done: 'Done',
};

const TYPE_LABELS: Record<TicketType, string> = {
  bug: 'Bug',
  feature: 'Feature',
  fix: 'Fix',
};

const PRIORITY_LABELS: Record<TicketPriority, string> = {
  low: 'Low',
  medium: 'Medium',
  high: 'High',
  urgent: 'Urgent',
};

const DUE_FILTER_LABELS: Record<DueFilter, string> = {
  overdue: 'Overdue',
  has_due_date: 'Has due date',
  no_due_date: 'No due date',
};

export function stateLabel(state: TicketState): string {
  return STATE_LABELS[state];
}

export function typeLabel(type: TicketType): string {
  return TYPE_LABELS[type];
}

export function priorityLabel(priority: TicketPriority): string {
  return PRIORITY_LABELS[priority];
}

export function dueFilterLabel(due: DueFilter): string {
  return DUE_FILTER_LABELS[due];
}

// Ordered lists for selects / column rendering.
export const stateOptions: ReadonlyArray<{ value: TicketState; label: string }> =
  TICKET_STATES.map((value) => ({ value, label: STATE_LABELS[value] }));

export const typeOptions: ReadonlyArray<{ value: TicketType; label: string }> =
  TICKET_TYPES.map((value) => ({ value, label: TYPE_LABELS[value] }));

export const priorityOptions: ReadonlyArray<{ value: TicketPriority; label: string }> =
  TICKET_PRIORITIES.map((value) => ({ value, label: PRIORITY_LABELS[value] }));

export const dueFilterOptions: ReadonlyArray<{ value: DueFilter; label: string }> =
  DUE_FILTERS.map((value) => ({ value, label: DUE_FILTER_LABELS[value] }));

// Workflow-ordered states for the five board columns (FR-E6-2).
export const orderedStates: ReadonlyArray<TicketState> = TICKET_STATES;
