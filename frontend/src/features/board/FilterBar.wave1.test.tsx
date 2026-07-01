// Wave 1 FilterBar coverage (F-03 priority, F-08 due, F-02 assignee) — complements the existing
// FilterBar.test.tsx (type/epic/search/clear/count). Verifies the new controls render, list the
// correct options, emit the right BoardFilters via onChange, that "Assigned to me" is always offered
// (no member-listing needed), that admin-sourced by-user options appear when supplied, and that
// assignedToMe/assigneeId are mutually exclusive in the encoded select. Also asserts the priority /
// due / assignee filters each enable Clear (hasActiveFilters).
//
// NOTE (env constraint): authored to the existing Vitest + RTL patterns but NOT executed in this QA
// pass — no Node.js runtime is available on the QA machine (see the QA report's honest gaps). Ready
// to run via `npm test -- --run`.

import { describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FilterBar } from './FilterBar';
import type { AssigneeRef, BoardFilters, Epic } from '@/api/types';

const epics: Epic[] = [
  {
    id: 'ep01-billing-revamp',
    teamId: 'f1c2-team-platform',
    title: 'Billing Revamp',
    description: null,
    ticketCount: 5,
    createdAt: '2026-06-20T09:00:00Z',
    modifiedAt: '2026-06-23T12:00:00Z',
  },
];

const assigneeOptions: AssigneeRef[] = [
  { id: 'u-alex', displayName: 'Alex Doe' },
  { id: 'u-sam', displayName: 'sam@dataart.com' },
];

function setup(overrides: Partial<ComponentProps<typeof FilterBar>> = {}) {
  const onChange = vi.fn();
  const onClear = vi.fn();
  const props: ComponentProps<typeof FilterBar> = {
    filters: {} as BoardFilters,
    epics,
    epicsLoading: false,
    total: 10,
    onChange,
    onClear,
    ...overrides,
  };
  const user = userEvent.setup();
  render(<FilterBar {...props} />);
  return { onChange, onClear, user };
}

describe('FilterBar — Wave 1 filters', () => {
  // ---- Priority (F-03) ----

  it('renders the priority select listing All priorities + the four canonical priorities', () => {
    setup();
    const sel = screen.getByRole('combobox', { name: 'Filter by priority' });
    const options = Array.from(sel.querySelectorAll('option')).map((o) => o.textContent);
    expect(options).toEqual(['All priorities', 'Low', 'Medium', 'High', 'Urgent']);
  });

  it('calls onChange with the selected priority', async () => {
    const { onChange, user } = setup();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by priority' }), 'high');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ priority: 'high' }));
  });

  it('a priority filter enables Clear', () => {
    setup({ filters: { priority: 'urgent' } });
    expect(screen.getByRole('button', { name: 'Clear' })).toBeEnabled();
  });

  // ---- Due (F-08) ----

  it('renders the due select listing All due dates + the three due filters', () => {
    setup();
    const sel = screen.getByRole('combobox', { name: 'Filter by due date' });
    const options = Array.from(sel.querySelectorAll('option')).map((o) => o.textContent);
    expect(options).toEqual(['All due dates', 'Overdue', 'Has due date', 'No due date']);
  });

  it('calls onChange with the selected dueFilter', async () => {
    const { onChange, user } = setup();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by due date' }), 'overdue');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ dueFilter: 'overdue' }));
  });

  it('a dueFilter enables Clear', () => {
    setup({ filters: { dueFilter: 'no_due_date' } });
    expect(screen.getByRole('button', { name: 'Clear' })).toBeEnabled();
  });

  // ---- Assignee (F-02) ----

  it('always offers "All assignees" and "Assigned to me" even with no candidate-user source', () => {
    setup(); // no assigneeOptions supplied (the non-admin case)
    const sel = screen.getByRole('combobox', { name: 'Filter by assignee' });
    const options = Array.from(sel.querySelectorAll('option')).map((o) => o.textContent);
    expect(options).toEqual(['All assignees', 'Assigned to me']);
  });

  it('lists admin-sourced candidate users after "Assigned to me" when supplied', () => {
    setup({ assigneeOptions });
    const sel = screen.getByRole('combobox', { name: 'Filter by assignee' });
    const options = Array.from(sel.querySelectorAll('option')).map((o) => o.textContent);
    expect(options).toEqual(['All assignees', 'Assigned to me', 'Alex Doe', 'sam@dataart.com']);
  });

  it('selecting "Assigned to me" emits assignedToMe=true and clears assigneeId', async () => {
    const { onChange, user } = setup({ assigneeOptions, filters: { assigneeId: 'u-alex' } });
    await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by assignee' }), 'me');
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ assignedToMe: true, assigneeId: undefined }),
    );
  });

  it('selecting a specific user emits assigneeId and clears assignedToMe', async () => {
    const { onChange, user } = setup({ assigneeOptions, filters: { assignedToMe: true } });
    await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by assignee' }), 'u-sam');
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ assigneeId: 'u-sam', assignedToMe: undefined }),
    );
  });

  it('selecting "All assignees" clears both assignedToMe and assigneeId', async () => {
    const { onChange, user } = setup({ filters: { assignedToMe: true } });
    await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by assignee' }), '');
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ assignedToMe: undefined, assigneeId: undefined }),
    );
  });

  it('reflects assignedToMe in the encoded assignee select value', () => {
    setup({ filters: { assignedToMe: true } });
    const sel = screen.getByRole('combobox', { name: 'Filter by assignee' }) as HTMLSelectElement;
    expect(sel.value).toBe('me');
  });

  it('an assignedToMe filter enables Clear', () => {
    setup({ filters: { assignedToMe: true } });
    expect(screen.getByRole('button', { name: 'Clear' })).toBeEnabled();
  });
});
