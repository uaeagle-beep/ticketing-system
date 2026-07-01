import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { useLocation } from 'react-router-dom';
import { TicketCard } from './TicketCard';
import type { TicketCard as TicketCardModel } from '@/api/types';
import { renderWithProviders, Route, Routes } from '@/test/renderWithProviders';

const ticket: TicketCardModel = {
  id: 'tk1042-login-fails',
  type: 'bug',
  state: 'in_progress',
  priority: 'high',
  title: 'Login fails',
  epicId: 'ep01-billing-revamp',
  epicTitle: 'Billing Revamp',
  dueDate: null,
  isOverdue: false,
  assignees: [],
  labels: [],
  // ISO-8601 UTC; relativeTime renders something containing "ago" or a date.
  modifiedAt: '2026-06-23T12:40:00Z',
};

// Surfaces the current pathname so navigation assertions are explicit.
function LocationProbe() {
  const location = useLocation();
  return <div data-testid="pathname">{location.pathname}</div>;
}

function renderCard(model = ticket) {
  return renderWithProviders(
    <Routes>
      <Route
        path="/"
        element={
          <>
            <TicketCard ticket={model} />
            <LocationProbe />
          </>
        }
      />
      <Route path="/tickets/:id" element={<LocationProbe />} />
    </Routes>,
    { initialEntries: ['/'] },
  );
}

describe('TicketCard', () => {
  it('renders the type badge (UPPERCASE), title, epic name, and relative time', () => {
    renderCard();
    // Type badge text is uppercased at the edge (Wireframe 1).
    expect(screen.getByText('BUG')).toBeInTheDocument();
    expect(screen.getByText('Login fails')).toBeInTheDocument();
    expect(screen.getByText('Billing Revamp')).toBeInTheDocument();
    // relativeTime against a fixed ISO date renders a non-empty string.
    expect(screen.getByText(/ago$|2026/)).toBeInTheDocument();
  });

  it('shows "No epic" when the ticket has no epic', () => {
    renderCard({ ...ticket, epicId: null, epicTitle: null });
    expect(screen.getByText('No epic')).toBeInTheDocument();
  });

  it('exposes the card body as a button with an "Open ticket" accessible name', () => {
    renderCard();
    expect(
      screen.getByRole('button', { name: 'Open ticket: Login fails' }),
    ).toBeInTheDocument();
  });

  it('the drag handle has an aria-label starting with "Move ticket" (A11Y-3)', () => {
    renderCard();
    const handle = screen.getByRole('button', {
      name: /^Move ticket: Login fails/,
    });
    expect(handle).toBeInTheDocument();
    // Source-state context is included for AT users.
    expect(handle).toHaveAttribute('aria-label', expect.stringContaining('In progress'));
  });

  it('opens the ticket on click', async () => {
    const { user } = renderCard();
    await user.click(screen.getByRole('button', { name: 'Open ticket: Login fails' }));
    await waitFor(() =>
      expect(screen.getByTestId('pathname')).toHaveTextContent('/tickets/tk1042-login-fails'),
    );
  });

  it('opens the ticket on Enter', async () => {
    const { user } = renderCard();
    const card = screen.getByRole('button', { name: 'Open ticket: Login fails' });
    card.focus();
    await user.keyboard('{Enter}');
    await waitFor(() =>
      expect(screen.getByTestId('pathname')).toHaveTextContent('/tickets/tk1042-login-fails'),
    );
  });

  it('does NOT open the ticket on Space (A11Y-3 — Space is reserved for the drag handle)', async () => {
    const { user } = renderCard();
    const card = screen.getByRole('button', { name: 'Open ticket: Login fails' });
    card.focus();
    await user.keyboard(' ');
    // Still on the board route; no navigation occurred.
    expect(screen.getByTestId('pathname')).toHaveTextContent('/');
    expect(screen.getByTestId('pathname')).not.toHaveTextContent('/tickets/');
  });
});
