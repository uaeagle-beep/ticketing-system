import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { BoardColumn } from './BoardColumn';
import type { BoardColumn as BoardColumnModel, TicketCard } from '@/api/types';
import { renderWithProviders } from '@/test/renderWithProviders';
import { TICKET_STATES } from '@/api/types';
import { stateLabel } from '@/lib/labels';

const card: TicketCard = {
  id: 'tk1042-login-fails',
  type: 'bug',
  state: 'new',
  title: 'Login fails',
  epicId: null,
  epicTitle: null,
  modifiedAt: '2026-06-23T12:40:00Z',
};

function column(overrides: Partial<BoardColumnModel> = {}): BoardColumnModel {
  return { state: 'new', count: 0, tickets: [], ...overrides };
}

describe('BoardColumn', () => {
  it('renders the UPPERCASE state label in the header with the count badge', () => {
    renderWithProviders(<BoardColumn column={column({ state: 'in_progress', count: 8 })} />);
    // Header text is uppercased (Wireframe 1).
    expect(screen.getByText('IN PROGRESS')).toBeInTheDocument();
    // Count badge.
    expect(screen.getByText('8')).toBeInTheDocument();
  });

  it('labels the section with the human (non-uppercased) state label for AT', () => {
    renderWithProviders(<BoardColumn column={column({ state: 'ready_for_acceptance' })} />);
    expect(
      screen.getByRole('region', { name: 'Ready for acceptance' }),
    ).toBeInTheDocument();
  });

  it('shows a per-column empty state when there are no tickets', () => {
    renderWithProviders(<BoardColumn column={column({ count: 0, tickets: [] })} />);
    expect(screen.getByText('No tickets')).toBeInTheDocument();
  });

  it('renders a card for each ticket and hides the empty state', () => {
    renderWithProviders(
      <BoardColumn column={column({ count: 1, tickets: [card] })} />,
    );
    expect(screen.getByText('Login fails')).toBeInTheDocument();
    expect(screen.queryByText('No tickets')).not.toBeInTheDocument();
  });

  it('uppercases the header label for every canonical state', () => {
    for (const state of TICKET_STATES) {
      const { unmount } = renderWithProviders(<BoardColumn column={column({ state })} />);
      expect(screen.getByText(stateLabel(state).toUpperCase())).toBeInTheDocument();
      unmount();
    }
  });
});
