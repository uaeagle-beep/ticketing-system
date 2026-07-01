import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { Label, LabelRef } from '@/api/types';
import { LabelsManager } from './LabelsManager';
import { LabelPicker } from './LabelPicker';
import { renderWithProviders } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleLabels } from '@/test/handlers';

// QA acceptance — labels management + picker (Wave 2 §9.4, ADR-0016). Extends the developer smoke test with
// the edit/recolor flow, the delete-confirm flow (disposable, removes from all tickets), the duplicate-name
// 409 -> mapped toast, and the picker's multi-select toggle/checked state.

const backend: LabelRef = { id: 'lb01-backend', name: 'Backend', color: '#3b82f6' };
const urgent: LabelRef = { id: 'lb02-urgent', name: 'Urgent', color: '#ef4444' };

describe('LabelsManager — edit / recolor (acceptance)', () => {
  it('renames a label inline and reflects the new name after refetch', async () => {
    const state: Label[] = [...sampleLabels];
    let putName: string | null = null;
    server.use(
      http.get(`${API}/labels`, () => HttpResponse.json(state, { status: 200 })),
      http.put(`${API}/labels/:id`, async ({ request, params }) => {
        const b = (await request.json()) as { name: string; color: string };
        putName = b.name;
        const idx = state.findIndex((l) => l.id === String(params.id));
        if (idx >= 0) state[idx] = { ...state[idx]!, name: b.name, color: b.color };
        return HttpResponse.json(state[idx], { status: 200 });
      }),
    );

    const user = userEvent.setup();
    renderWithProviders(<LabelsManager teamId="f1c2-team-platform" />, { initialEntries: ['/teams'] });

    await waitFor(() => expect(screen.getByText('Backend')).toBeInTheDocument());

    // Click Edit on the first label row, change the name, Save.
    const rows = screen.getAllByRole('button', { name: 'Edit' });
    await user.click(rows[0]!);
    const nameInput = screen.getByRole('textbox', { name: 'Label name' });
    await user.clear(nameInput);
    await user.type(nameInput, 'Backend API');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(putName).toBe('Backend API'));
    await waitFor(() => expect(screen.getByText('Backend API')).toBeInTheDocument());
  });
});

describe('LabelsManager — delete (acceptance)', () => {
  it('confirms then deletes a label, removing it from the list', async () => {
    const state: Label[] = [...sampleLabels];
    let deletedId: string | null = null;
    server.use(
      http.get(`${API}/labels`, () => HttpResponse.json(state, { status: 200 })),
      http.delete(`${API}/labels/:id`, ({ params }) => {
        deletedId = String(params.id);
        const idx = state.findIndex((l) => l.id === deletedId);
        if (idx >= 0) state.splice(idx, 1);
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderWithProviders(<LabelsManager teamId="f1c2-team-platform" />, { initialEntries: ['/teams'] });

    await waitFor(() => expect(screen.getByText('Urgent')).toBeInTheDocument());

    // Delete the second label (Urgent). A confirm dialog warns it is removed from all tickets.
    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' });
    await user.click(deleteButtons[1]!);

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/removed from all tickets/i)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deletedId).toBe('lb02-urgent'));
    await waitFor(() => expect(screen.queryByText('Urgent')).not.toBeInTheDocument());
  });
});

describe('LabelsManager — duplicate name 409 (acceptance)', () => {
  it('surfaces the mapped duplicate_label_name message as a toast on create', async () => {
    server.use(
      http.get(`${API}/labels`, () => HttpResponse.json([...sampleLabels], { status: 200 })),
      http.post(`${API}/labels`, () =>
        HttpResponse.json(
          { error: { code: 'duplicate_label_name', message: 'dup' } },
          { status: 409 },
        ),
      ),
    );

    const user = userEvent.setup();
    renderWithProviders(<LabelsManager teamId="f1c2-team-platform" />, { initialEntries: ['/teams'] });

    await waitFor(() => expect(screen.getByText('Backend')).toBeInTheDocument());
    await user.type(screen.getByLabelText('New label name'), 'Backend');
    await user.click(screen.getByRole('button', { name: /Add label/i }));

    const toast = await screen.findByRole('status');
    expect(toast).toHaveTextContent(/A label with this name already exists in this team\./i);
  });
});

describe('LabelPicker — multi-select (acceptance)', () => {
  it('reflects the selected set as aria-selected options and toggles each independently', async () => {
    const onToggle = vi.fn();
    const user = userEvent.setup();
    render(
      <LabelPicker labels={[backend, urgent]} selectedIds={['lb01-backend']} onToggle={onToggle} />,
    );

    // Open the dropdown listbox.
    await user.click(screen.getByRole('button', { name: 'Labels' }));
    const listbox = screen.getByRole('listbox');
    expect(listbox).toHaveAttribute('aria-multiselectable', 'true');

    const options = within(listbox).getAllByRole('option');
    expect(options).toHaveLength(2);
    // Backend is pre-selected; Urgent is not.
    const backendOption = within(listbox).getByRole('option', { name: /Backend/ });
    const urgentOption = within(listbox).getByRole('option', { name: /Urgent/ });
    expect(backendOption).toHaveAttribute('aria-selected', 'true');
    expect(urgentOption).toHaveAttribute('aria-selected', 'false');

    // Toggling Urgent ON fires its id.
    await user.click(urgentOption);
    expect(onToggle).toHaveBeenCalledWith('lb02-urgent');

    // Toggling Backend OFF fires its id too (multi-select: the panel stays open).
    await user.click(backendOption);
    expect(onToggle).toHaveBeenCalledWith('lb01-backend');
  });

  it('disables the trigger when disabled', () => {
    render(
      <LabelPicker labels={[backend, urgent]} selectedIds={[]} onToggle={vi.fn()} disabled />,
    );
    expect(screen.getByRole('button', { name: 'Labels' })).toBeDisabled();
  });
});
