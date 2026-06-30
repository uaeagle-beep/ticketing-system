import { describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { UsersFilterBar, EMPTY_USERS_FILTERS } from './UsersFilterBar';
import type { Team } from '@/api/types';

const teams: Team[] = [
  {
    id: 'team-platform',
    name: 'Platform',
    ticketCount: 0,
    epicCount: 0,
    createdAt: '2026-06-20T08:00:00Z',
    modifiedAt: '2026-06-20T08:00:00Z',
    wipLimits: {
      new: null,
      ready_for_implementation: null,
      in_progress: null,
      ready_for_acceptance: null,
      done: null,
    },
  },
];

function setup(overrides: Partial<ComponentProps<typeof UsersFilterBar>> = {}) {
  const onChange = vi.fn();
  const onClear = vi.fn();
  const props: ComponentProps<typeof UsersFilterBar> = {
    filters: EMPTY_USERS_FILTERS,
    teams,
    matchCount: 5,
    onChange,
    onClear,
    ...overrides,
  };
  const user = userEvent.setup();
  render(<UsersFilterBar {...props} />);
  return { onChange, onClear, user };
}

describe('UsersFilterBar', () => {
  it('renders an accessible label/aria control for every filter plus Clear and the count', () => {
    setup();
    expect(screen.getByRole('searchbox', { name: 'Search by name or email' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Filter by role' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Filter by team' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Filter by email verification' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Filter by status' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Clear' })).toBeInTheDocument();
    expect(screen.getByText('5 users')).toBeInTheDocument();
  });

  it('lists each team as a team-filter option plus the "All teams" default', () => {
    setup();
    const teamSelect = screen.getByRole('combobox', { name: 'Filter by team' });
    const options = Array.from(teamSelect.querySelectorAll('option')).map((o) => o.textContent);
    expect(options).toEqual(['All teams', 'Platform']);
  });

  it('calls onChange with the typed search term', async () => {
    const { onChange, user } = setup();
    await user.type(screen.getByRole('searchbox', { name: 'Search by name or email' }), 'a');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ search: 'a' }));
  });

  it('calls onChange with the selected role / verified / status', async () => {
    const { onChange, user } = setup();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by role' }), 'admin');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ role: 'admin' }));

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Filter by email verification' }),
      'unverified',
    );
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ verified: 'unverified' }));

    await user.selectOptions(screen.getByRole('combobox', { name: 'Filter by status' }), 'blocked');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ status: 'blocked' }));
  });

  it('disables Clear when no filters are active', () => {
    setup();
    expect(screen.getByRole('button', { name: 'Clear' })).toBeDisabled();
  });

  it('enables Clear when a filter is active and calls onClear when clicked', async () => {
    const { onClear, user } = setup({ filters: { ...EMPTY_USERS_FILTERS, role: 'admin' } });
    const clear = screen.getByRole('button', { name: 'Clear' });
    expect(clear).toBeEnabled();
    await user.click(clear);
    expect(onClear).toHaveBeenCalledTimes(1);
  });

  it('uses the singular noun when exactly one user matches', () => {
    setup({ matchCount: 1 });
    expect(screen.getByText('1 user')).toBeInTheDocument();
  });
});
