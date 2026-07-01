import { describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MultiSelectDropdown } from './MultiSelectDropdown';
// Pulls in initI18nForTest (pins the i18n singleton to en) so the sr-only status string resolves.
import '@/test/renderWithProviders';

// Focused component coverage for the accessible multi-select dropdown used by the ticket form:
//  - trigger opens/closes the listbox (aria-expanded);
//  - Escape closes and returns focus to the trigger (WCAG 2.4.3);
//  - outside click closes;
//  - each option toggles independently via onToggle (multi-select — panel stays open);
//  - a disabled trigger cannot open the panel.

interface Item {
  id: string;
  name: string;
}

const items: Item[] = [
  { id: 'a', name: 'Alice' },
  { id: 'b', name: 'Bob' },
  { id: 'c', name: 'Carol' },
];

// Controlled harness: reflects onToggle back into the selected set so multi-select behavior is real
// (matches how TicketPage/LabelPicker own the id set). An extra "outside" button lets us click away.
function Harness({
  onToggleSpy,
  disabled = false,
  initial = [],
}: {
  onToggleSpy?: (id: string) => void;
  disabled?: boolean;
  initial?: string[];
}) {
  const [selected, setSelected] = useState<string[]>(initial);
  const toggle = (id: string) => {
    onToggleSpy?.(id);
    setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  };
  return (
    <div>
      <button type="button">Outside</button>
      <MultiSelectDropdown<Item>
        id="picker"
        ariaLabel="People"
        options={items}
        selectedIds={selected}
        onToggle={toggle}
        disabled={disabled}
        placeholder="Select people…"
        renderOption={(o) => <span>{o.name}</span>}
        renderSelected={(o) => <span>{o.name}</span>}
      />
    </div>
  );
}

describe('MultiSelectDropdown', () => {
  it('shows the placeholder and is collapsed by default', () => {
    render(<Harness />);
    const trigger = screen.getByRole('button', { name: 'People' });
    expect(trigger).toHaveAttribute('aria-haspopup', 'listbox');
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByText('Select people…')).toBeInTheDocument();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('opens the listbox on trigger click and lists every option', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'People' }));

    const listbox = screen.getByRole('listbox');
    expect(listbox).toHaveAttribute('aria-multiselectable', 'true');
    expect(screen.getByRole('button', { name: 'People' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getAllByRole('option')).toHaveLength(3);
  });

  it('toggles options independently (multi-select) without closing', async () => {
    const onToggleSpy = vi.fn();
    const user = userEvent.setup();
    render(<Harness onToggleSpy={onToggleSpy} />);

    await user.click(screen.getByRole('button', { name: 'People' }));
    await user.click(screen.getByRole('option', { name: 'Alice' }));
    expect(onToggleSpy).toHaveBeenCalledWith('a');
    // Still open after a toggle (multi-select), and the option now reads as selected.
    expect(screen.getByRole('listbox')).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Alice' })).toHaveAttribute('aria-selected', 'true');

    await user.click(screen.getByRole('option', { name: 'Carol' }));
    expect(onToggleSpy).toHaveBeenCalledWith('c');
    expect(screen.getByRole('option', { name: 'Carol' })).toHaveAttribute('aria-selected', 'true');
    // Alice stays selected (independent toggles).
    expect(screen.getByRole('option', { name: 'Alice' })).toHaveAttribute('aria-selected', 'true');
  });

  it('reflects the selected set as chips in the trigger summary', async () => {
    const user = userEvent.setup();
    render(<Harness initial={['b']} />);
    // Bob starts selected → shown as a chip; placeholder is gone.
    expect(screen.queryByText('Select people…')).not.toBeInTheDocument();
    const trigger = screen.getByRole('button', { name: 'People' });
    expect(trigger).toHaveTextContent('Bob');
    // Toggling Alice on adds a second chip.
    await user.click(trigger);
    await user.click(screen.getByRole('option', { name: 'Alice' }));
    expect(trigger).toHaveTextContent('Alice');
    expect(trigger).toHaveTextContent('Bob');
  });

  it('closes on Escape and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    const trigger = screen.getByRole('button', { name: 'People' });
    await user.click(trigger);
    await screen.findByRole('listbox');

    await user.keyboard('{Escape}');

    await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it('closes on an outside click', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'People' }));
    await screen.findByRole('listbox');

    await user.click(screen.getByRole('button', { name: 'Outside' }));
    await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument());
  });

  it('toggles selection with the keyboard (Space on a focused option)', async () => {
    const onToggleSpy = vi.fn();
    const user = userEvent.setup();
    render(<Harness onToggleSpy={onToggleSpy} />);
    await user.click(screen.getByRole('button', { name: 'People' }));
    // On open, focus lands on the first option; Space toggles it.
    await waitFor(() => expect(screen.getByRole('option', { name: 'Alice' })).toHaveFocus());
    await user.keyboard(' ');
    expect(onToggleSpy).toHaveBeenCalledWith('a');
    // Panel stays open for further keyboard selection.
    expect(screen.getByRole('listbox')).toBeInTheDocument();
  });

  it('does not open when disabled', async () => {
    const user = userEvent.setup();
    render(<Harness disabled />);
    const trigger = screen.getByRole('button', { name: 'People' });
    expect(trigger).toBeDisabled();
    await user.click(trigger);
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });
});
