// Mapping between canonical API enum values (lowercase) and human-readable UI
// labels (A2). The API ALWAYS receives/returns canonical lowercase; the UI only
// ever displays the localized labels here. Column headers and type badges render
// UPPERCASE per Wireframe 1 — callers apply text-transform / .toUpperCase() at the edge.
//
// Wave 3 i18n (ADR-0022): labels come from the `enums` namespace of the i18n singleton
// (src/i18n/config.ts) rather than a hardcoded English map, so they follow the active
// language. These are plain functions (not React hooks) that read i18n at call time, so
// they work from anywhere and re-render correctly when the language changes (callers that
// need reactivity read them inside a component that also subscribes via useTranslation).
// The option lists are FUNCTIONS (not const arrays) so each call reflects the active language.

import i18n from '@/i18n/config';
import type { DueFilter, TicketPriority, TicketState, TicketType } from '@/api/types';
import { DUE_FILTERS, TICKET_PRIORITIES, TICKET_STATES, TICKET_TYPES } from '@/api/types';

export function stateLabel(state: TicketState): string {
  return i18n.t(`enums:state.${state}`);
}

export function typeLabel(type: TicketType): string {
  return i18n.t(`enums:type.${type}`);
}

export function priorityLabel(priority: TicketPriority): string {
  return i18n.t(`enums:priority.${priority}`);
}

export function dueFilterLabel(due: DueFilter): string {
  return i18n.t(`enums:dueFilter.${due}`);
}

// Ordered lists for selects / column rendering. FUNCTIONS so the labels reflect the
// active language on every call (canonical value order is invariant).
export function stateOptions(): ReadonlyArray<{ value: TicketState; label: string }> {
  return TICKET_STATES.map((value) => ({ value, label: stateLabel(value) }));
}

export function typeOptions(): ReadonlyArray<{ value: TicketType; label: string }> {
  return TICKET_TYPES.map((value) => ({ value, label: typeLabel(value) }));
}

export function priorityOptions(): ReadonlyArray<{ value: TicketPriority; label: string }> {
  return TICKET_PRIORITIES.map((value) => ({ value, label: priorityLabel(value) }));
}

export function dueFilterOptions(): ReadonlyArray<{ value: DueFilter; label: string }> {
  return DUE_FILTERS.map((value) => ({ value, label: dueFilterLabel(value) }));
}

// Workflow-ordered states for the five board columns (FR-E6-2). Language-invariant.
export const orderedStates: ReadonlyArray<TicketState> = TICKET_STATES;
