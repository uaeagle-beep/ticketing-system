import { describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfirmDialog } from './ConfirmDialog';

// A11Y-2 (WCAG 2.4.3 Focus Order) acceptance for the confirmation modal:
//  - on open, focus moves to the confirm button;
//  - Escape closes (unless busy);
//  - Tab / Shift+Tab cycle within the dialog (focus trap);
//  - on close, focus returns to the trigger that opened it.

// Harness: a trigger button that opens the dialog and keeps it mounted while
// closed (so we can observe focus restoration on close).
function Harness({
  onConfirm = vi.fn(),
  busy = false,
}: {
  onConfirm?: () => void;
  busy?: boolean;
}) {
  const [open, setOpen] = useState(false);
  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>
        Delete ticket
      </button>
      <ConfirmDialog
        open={open}
        title="Delete ticket?"
        message="This cannot be undone."
        confirmLabel="Delete"
        cancelLabel="Cancel"
        danger
        busy={busy}
        onConfirm={onConfirm}
        onCancel={() => setOpen(false)}
      />
    </div>
  );
}

describe('ConfirmDialog', () => {
  it('does not render when closed', () => {
    render(
      <ConfirmDialog
        open={false}
        title="Delete ticket?"
        message="x"
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('moves focus to the confirm button when opened', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole('button', { name: 'Delete ticket' }));

    const confirm = await screen.findByRole('button', { name: 'Delete' });
    await waitFor(() => expect(confirm).toHaveFocus());
  });

  it('renders as an accessible modal dialog labelled by its title', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'Delete ticket' }));

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAttribute('aria-label', 'Delete ticket?');
  });

  it('closes on Escape and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    const trigger = screen.getByRole('button', { name: 'Delete ticket' });
    await user.click(trigger);
    await screen.findByRole('dialog');

    await user.keyboard('{Escape}');

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // Focus restored to the element that opened the dialog (A11Y-2).
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it('does not close on Escape while busy', async () => {
    const user = userEvent.setup();
    render(<Harness busy />);

    // When busy the confirm/cancel are disabled; open via the trigger.
    await user.click(screen.getByRole('button', { name: 'Delete ticket' }));
    await screen.findByRole('dialog');

    await user.keyboard('{Escape}');

    // Still open (busy guards the Escape handler).
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('traps Tab focus within the dialog (Tab from confirm wraps to cancel)', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'Delete ticket' }));

    const confirm = await screen.findByRole('button', { name: 'Delete' });
    const cancel = screen.getByRole('button', { name: 'Cancel' });

    await waitFor(() => expect(confirm).toHaveFocus());

    // Confirm is the last focusable; Tab wraps to the first (Cancel).
    await user.tab();
    await waitFor(() => expect(cancel).toHaveFocus());
  });

  it('traps Shift+Tab focus (Shift+Tab from the first wraps to the last)', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'Delete ticket' }));

    const confirm = await screen.findByRole('button', { name: 'Delete' });
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    await waitFor(() => expect(confirm).toHaveFocus());

    // Move to the first focusable (Cancel), then Shift+Tab wraps to the last.
    await user.tab(); // -> Cancel (first)
    await waitFor(() => expect(cancel).toHaveFocus());
    await user.tab({ shift: true }); // wraps back to Confirm (last)
    await waitFor(() => expect(confirm).toHaveFocus());
  });

  it('invokes onConfirm when the confirm button is activated', async () => {
    const onConfirm = vi.fn();
    const user = userEvent.setup();
    render(<Harness onConfirm={onConfirm} />);
    await user.click(screen.getByRole('button', { name: 'Delete ticket' }));

    await user.click(await screen.findByRole('button', { name: 'Delete' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });
});
