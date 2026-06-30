// Generic confirmation modal (used for ticket delete, FR-E4-6).
//
// Accessibility (A11Y-2 — WCAG 2.4.3 Focus Order):
//  - On open, focus moves into the dialog (the confirm button).
//  - Focus is trapped: Tab / Shift+Tab cycle through the dialog's focusable
//    elements and never escape to the page behind it.
//  - On close, focus returns to the element that was focused when the dialog
//    opened (the trigger button).
//  - Escape closes the dialog (unless busy), as before.

import { useEffect, useRef, type ReactNode } from 'react';

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

// Tabbable elements inside the dialog. Excludes disabled controls so a focus
// trap never parks focus on the disabled confirm/cancel button while busy.
const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'textarea:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  danger = false,
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);
  // The element focused before the dialog opened, to restore on close.
  const previouslyFocused = useRef<HTMLElement | null>(null);

  // Escape-to-close (preserved). Kept as its own effect so it re-binds when
  // `busy` toggles.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !busy) onCancel();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, busy, onCancel]);

  // Focus management: capture the trigger, move focus into the dialog on open,
  // and restore focus to the trigger on close/unmount.
  useEffect(() => {
    if (!open) return;

    previouslyFocused.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;

    // Move focus into the dialog. Prefer the confirm button; if it's disabled
    // (e.g. opened mid-busy) fall back to the dialog container so focus is never
    // left behind on the trigger.
    const confirm = confirmRef.current;
    const target = confirm && !confirm.disabled ? confirm : dialogRef.current;
    target?.focus();

    return () => {
      // Restore focus to the trigger when the dialog closes or unmounts.
      previouslyFocused.current?.focus();
    };
  }, [open]);

  // Focus trap: keep Tab / Shift+Tab cycling within the dialog.
  useEffect(() => {
    if (!open) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== 'Tab') return;
      const dialog = dialogRef.current;
      if (!dialog) return;

      const focusable = Array.from(
        dialog.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ).filter((el) => el.offsetParent !== null || el === document.activeElement);

      if (focusable.length === 0) {
        // Nothing tabbable (e.g. all buttons disabled while busy): keep focus on
        // the dialog itself.
        e.preventDefault();
        dialog.focus();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;

      if (e.shiftKey) {
        // Shift+Tab from the first element (or from outside) wraps to the last.
        if (active === first || !dialog.contains(active)) {
          e.preventDefault();
          last?.focus();
        }
      } else if (active === last || !dialog.contains(active)) {
        // Tab from the last element (or from outside) wraps to the first.
        e.preventDefault();
        first?.focus();
      }
    };

    document.addEventListener('keydown', onKeyDown, true);
    return () => document.removeEventListener('keydown', onKeyDown, true);
  }, [open]);

  if (!open) return null;

  return (
    <div className="modal-backdrop" onMouseDown={() => !busy && onCancel()}>
      <div
        ref={dialogRef}
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        // Allow the container to receive programmatic focus as a fallback without
        // inserting it into the normal tab order.
        tabIndex={-1}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h3>{title}</h3>
        <div className="modal-body">{message}</div>
        <div className="modal-actions">
          <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </button>
          <button
            ref={confirmRef}
            type="button"
            className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`}
            onClick={onConfirm}
            disabled={busy}
          >
            {busy ? 'Working…' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
