// Wave 1 TicketCard coverage (F-03 priority badge, F-08 due pill / overdue indicator, F-02 assignee
// avatars) — complements the existing TicketCard.test.tsx (type badge, title, epic, nav, a11y).
// Verifies the priority badge renders UPPERCASE, the due pill renders when a due date is present and
// switches to an "Overdue" treatment driven by the backend-computed isOverdue flag, that no due pill
// / no avatars render when absent, and that assignee initials + a "+N" overflow appear with an
// accessible group label.
//
// NOTE (env constraint): authored to the existing Vitest + RTL patterns but NOT executed in this QA
// pass — no Node.js runtime is available on the QA machine (see the QA report's honest gaps).

import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { TicketCard } from './TicketCard';
import type { TicketCard as TicketCardModel } from '@/api/types';
import { renderWithProviders, Route, Routes } from '@/test/renderWithProviders';

const base: TicketCardModel = {
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
  modifiedAt: '2026-06-23T12:40:00Z',
};

function renderCard(model: TicketCardModel = base) {
  return renderWithProviders(
    <Routes>
      <Route path="/" element={<TicketCard ticket={model} />} />
    </Routes>,
    { initialEntries: ['/'] },
  );
}

describe('TicketCard — Wave 1 fields', () => {
  // ---- Priority badge (F-03) ----

  it('renders the priority badge UPPERCASE', () => {
    renderCard({ ...base, priority: 'urgent' });
    expect(screen.getByText('URGENT')).toBeInTheDocument();
  });

  it('renders each priority value as its own badge label', () => {
    renderCard({ ...base, priority: 'low' });
    expect(screen.getByText('LOW')).toBeInTheDocument();
  });

  // ---- Due date pill / overdue (F-08) ----

  it('renders a due-date pill when a due date is present and not overdue', () => {
    renderCard({ ...base, dueDate: '2026-07-05', isOverdue: false });
    // formatDueDate('2026-07-05') -> "Jul 5, 2026"; pill prefixes with "Due".
    expect(screen.getByText(/Due .*Jul 5, 2026/)).toBeInTheDocument();
  });

  it('renders an overdue treatment when isOverdue is true (text conveys status, not color alone)', () => {
    renderCard({ ...base, dueDate: '2026-06-01', isOverdue: true });
    expect(screen.getByText(/Overdue: .*Jun 1, 2026/)).toBeInTheDocument();
  });

  it('renders no due-date pill when there is no due date', () => {
    renderCard({ ...base, dueDate: null });
    expect(screen.queryByText(/Due /)).not.toBeInTheDocument();
    expect(screen.queryByText(/Overdue/)).not.toBeInTheDocument();
  });

  // ---- Assignee avatars (F-02) ----

  it('renders assignee initials with an accessible group label', () => {
    renderCard({
      ...base,
      assignees: [
        { id: 'u1', displayName: 'Alex Doe' },
        { id: 'u2', displayName: 'sam@dataart.com' },
      ],
    });
    // Initials: 'A' and 'S' (first char uppercased).
    expect(screen.getByLabelText('Assignees: Alex Doe, sam@dataart.com')).toBeInTheDocument();
    expect(screen.getByText('A')).toBeInTheDocument();
    expect(screen.getByText('S')).toBeInTheDocument();
  });

  it('shows a "+N" overflow when there are more than the max shown avatars', () => {
    renderCard({
      ...base,
      assignees: [
        { id: 'u1', displayName: 'Ann' },
        { id: 'u2', displayName: 'Bob' },
        { id: 'u3', displayName: 'Cy' },
        { id: 'u4', displayName: 'Dee' },
      ],
    });
    // max=3 shown → one overflow.
    expect(screen.getByText('+1')).toBeInTheDocument();
  });

  it('renders no avatars when there are no assignees', () => {
    renderCard({ ...base, assignees: [] });
    expect(screen.queryByLabelText(/^Assignees:/)).not.toBeInTheDocument();
  });
});
