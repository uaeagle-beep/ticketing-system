import { describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FilterBar } from './FilterBar';
import type { BoardFilters, Epic } from '@/api/types';

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
  {
    id: 'ep02-search',
    teamId: 'f1c2-team-platform',
    title: 'Search',
    description: null,
    ticketCount: 0,
    createdAt: '2026-06-21T09:00:00Z',
    modifiedAt: '2026-06-21T09:00:00Z',
  },
];

function setup(overrides: Partial<ComponentProps<typeof FilterBar>> = {}) {
  const onChange = vi.fn();
  const onClear = vi.fn();
  const props: ComponentProps<typeof FilterBar> = {
    filters: {} as BoardFilters,
    epics,
    epicsLoading: false,
    total: 37,
    onChange,
    onClear,
    ...overrides,
  };
  const user = userEvent.setup();
  render(<FilterBar {...props} />);
  return { onChange, onClear, user };
}

describe('FilterBar', () => {
  it('renders the search box, type select, epic select, Clear button and count', () => {
    setup();
    expect(screen.getByRole('searchbox', { name: 'Search by title' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Filter by type' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Filter by epic' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Clear' })).toBeInTheDocument();
    expect(screen.getByText('37 tickets')).toBeInTheDocument();
  });

  it('lists all type options (All types + the three canonical types)', () => {
    setup();
    const typeSelect = screen.getByRole('combobox', { name: 'Filter by type' });
    const options = Array.from(typeSelect.querySelectorAll('option')).map((o) => o.textContent);
    expect(options).toEqual(['All types', 'Bug', 'Feature', 'Fix']);
  });

  it('lists each epic as an option plus the "All epics" default', () => {
    setup();
    const epicSelect = screen.getByRole('combobox', { name: 'Filter by epic' });
    const options = Array.from(epicSelect.querySelectorAll('option')).map((o) => o.textContent);
    expect(options).toEqual(['All epics', 'Billing Revamp', 'Search']);
  });

  it('disables the epic select while epics are loading', () => {
    setup({ epicsLoading: true });
    expect(screen.getByRole('combobox', { name: 'Filter by epic' })).toBeDisabled();
  });

  it('calls onChange with the typed search term', async () => {
    const { onChange, user } = setup();
    await user.type(screen.getByRole('searchbox', { name: 'Search by title' }), 'a');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ search: 'a' }));
  });

  it('calls onChange with the selected type', async () => {
    const { onChange, user } = setup();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by type' }), 'bug');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ type: 'bug' }));
  });

  it('calls onChange with the selected epicId', async () => {
    const { onChange, user } = setup();
    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Filter by epic' }),
      'ep01-billing-revamp',
    );
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ epicId: 'ep01-billing-revamp' }),
    );
  });

  it('disables Clear when no filters are active', () => {
    setup({ filters: {} });
    expect(screen.getByRole('button', { name: 'Clear' })).toBeDisabled();
  });

  it('enables Clear when a filter is active and calls onClear when clicked', async () => {
    const { onClear, user } = setup({ filters: { type: 'bug' } });
    const clear = screen.getByRole('button', { name: 'Clear' });
    expect(clear).toBeEnabled();
    await user.click(clear);
    expect(onClear).toHaveBeenCalledTimes(1);
  });

  it('uses the singular noun when exactly one ticket matches', () => {
    setup({ total: 1 });
    expect(screen.getByText('1 ticket')).toBeInTheDocument();
  });

  it('uses the plural noun for zero tickets', () => {
    setup({ total: 0 });
    expect(screen.getByText('0 tickets')).toBeInTheDocument();
  });
});
