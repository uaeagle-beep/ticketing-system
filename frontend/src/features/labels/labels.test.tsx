// Wave 2 / Phase 3 labels — focused smoke coverage (ADR-0016). Verifies:
//  - LabelChip / LabelChips render the label name with the label's background color.
//  - TicketCard renders its labels[] as chips.
//  - FilterBar exposes a "Filter by label" select (only when labelOptions are supplied) and emits
//    { labelId } via onChange; the label filter counts toward hasActiveFilters (enables Clear).
//  - LabelPicker toggles a label id and points to management when the team has no labels.
//  - LabelsManager lists a team's labels and creates a new one (happy path via the MSW handlers).
// Full feature coverage (edit/recolor/delete flows, error mapping) is the Tester's.

import { describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LabelChip, LabelChips } from '@/components/Badges';
import { TicketCard } from '@/features/board/TicketCard';
import { FilterBar } from '@/features/board/FilterBar';
import { LabelPicker } from './LabelPicker';
import { LabelsManager } from './LabelsManager';
import type { BoardFilters, Label, LabelRef, TicketCard as TicketCardModel } from '@/api/types';
import { renderWithProviders, Route, Routes } from '@/test/renderWithProviders';
import { API, sampleLabels } from '@/test/handlers';
import { server } from '@/test/server';
import { http, HttpResponse } from 'msw';

const backend: LabelRef = { id: 'lb01-backend', name: 'Backend', color: '#3b82f6' };
const urgent: LabelRef = { id: 'lb02-urgent', name: 'Urgent', color: '#ef4444' };

describe('LabelChip / LabelChips', () => {
  it('renders the label name with its background color', () => {
    render(<LabelChip label={backend} />);
    const chip = screen.getByText('Backend');
    // The color conveys grouping; the NAME conveys the value (WCAG 1.4.1).
    expect(chip).toHaveStyle({ backgroundColor: '#3b82f6' });
  });

  it('renders nothing for an empty set', () => {
    const { container } = render(<LabelChips labels={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe('TicketCard labels', () => {
  const cardBase: TicketCardModel = {
    id: 'tk1',
    type: 'bug',
    state: 'new',
    priority: 'medium',
    title: 'Login fails',
    epicId: null,
    epicTitle: null,
    dueDate: null,
    isOverdue: false,
    assignees: [],
    labels: [backend, urgent],
    modifiedAt: '2026-06-23T12:40:00Z',
  };

  it('renders each label as a chip on the card', () => {
    renderWithProviders(
      <Routes>
        <Route path="/" element={<TicketCard ticket={cardBase} />} />
      </Routes>,
      { initialEntries: ['/'] },
    );
    expect(screen.getByText('Backend')).toBeInTheDocument();
    expect(screen.getByText('Urgent')).toBeInTheDocument();
  });
});

describe('FilterBar — label filter', () => {
  // Names chosen to avoid colliding with priority/type option labels in the same bar.
  const labelOptions: Label[] = [
    { id: 'lb01-backend', teamId: 't1', name: 'Backend', color: '#3b82f6' },
    { id: 'lb02-frontend', teamId: 't1', name: 'Frontend', color: '#ef4444' },
  ];

  function setup(overrides: Partial<ComponentProps<typeof FilterBar>> = {}) {
    const onChange = vi.fn();
    const onClear = vi.fn();
    const props: ComponentProps<typeof FilterBar> = {
      filters: {} as BoardFilters,
      epics: [],
      epicsLoading: false,
      total: 3,
      labelOptions,
      onChange,
      onClear,
      ...overrides,
    };
    const user = userEvent.setup();
    render(<FilterBar {...props} />);
    return { onChange, onClear, user };
  }

  it('shows the label select with the team labels', () => {
    setup();
    const select = screen.getByLabelText('Filter by label');
    expect(select).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Backend' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Frontend' })).toBeInTheDocument();
  });

  it('does NOT render the label select when the team has no labels', () => {
    setup({ labelOptions: [] });
    expect(screen.queryByLabelText('Filter by label')).not.toBeInTheDocument();
  });

  it('emits { labelId } on selection', async () => {
    const { onChange, user } = setup();
    await user.selectOptions(screen.getByLabelText('Filter by label'), 'lb02-frontend');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ labelId: 'lb02-frontend' }));
  });

  it('an active label filter enables Clear', () => {
    setup({ filters: { labelId: 'lb01-backend' } });
    expect(screen.getByRole('button', { name: 'Clear' })).toBeEnabled();
  });
});

describe('LabelPicker', () => {
  it('toggles a label id via the dropdown', async () => {
    const onToggle = vi.fn();
    const user = userEvent.setup();
    render(<LabelPicker labels={[backend, urgent]} selectedIds={['lb01-backend']} onToggle={onToggle} />);
    // The picker is now a multi-select dropdown: open it, then toggle an option.
    await user.click(screen.getByRole('button', { name: 'Labels' }));
    await user.click(screen.getByRole('option', { name: /Urgent/ }));
    expect(onToggle).toHaveBeenCalledWith('lb02-urgent');
  });

  it('points to management when the team has no labels', () => {
    render(<LabelPicker labels={[]} selectedIds={[]} onToggle={vi.fn()} />);
    // No labels → no dropdown trigger, just the management hint.
    expect(screen.queryByRole('button', { name: 'Labels' })).not.toBeInTheDocument();
    expect(screen.getByText(/No labels for this team/i)).toBeInTheDocument();
  });
});

describe('LabelsManager', () => {
  it('lists a team labels and creates a new one', async () => {
    // Stateful list handler so the post-create refetch reflects the new label (the default handler is
    // static). The POST is handled by the default handler; here we append on create.
    const state: Label[] = [...sampleLabels];
    server.use(
      http.get(`${API}/labels`, () => HttpResponse.json(state, { status: 200 })),
      http.post(`${API}/labels`, async ({ request }) => {
        const b = (await request.json()) as { teamId: string; name: string; color: string };
        const created: Label = { id: `lb-${b.name}`, teamId: b.teamId, name: b.name, color: b.color };
        state.push(created);
        return HttpResponse.json(created, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderWithProviders(<LabelsManager teamId="f1c2-team-platform" />, { initialEntries: ['/teams'] });

    // Lists the seeded labels.
    await waitFor(() => expect(screen.getByText(sampleLabels[0]!.name)).toBeInTheDocument());

    await user.type(screen.getByLabelText('New label name'), 'Docs');
    await user.click(screen.getByRole('button', { name: /Add label/i }));

    // The created label chip shows up (list invalidated + refetched from the stateful handler).
    await waitFor(() => expect(screen.getByText('Docs')).toBeInTheDocument());
  });
});
