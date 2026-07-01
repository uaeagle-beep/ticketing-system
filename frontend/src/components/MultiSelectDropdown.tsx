// Accessible multi-select dropdown (used by the ticket create/edit form for Assignees and Labels).
//
// Pattern (WAI-ARIA listbox, multi-selectable):
//  - The trigger is a `type="button"` with `aria-haspopup="listbox"` + `aria-expanded`; it is the
//    control the form <label htmlFor> points at (pass `id`). It shows the current selection as custom
//    chips, or a placeholder when empty.
//  - The panel is `role="listbox"` `aria-multiselectable="true"`; each row is `role="option"` with
//    `aria-selected`. Clicking (or Space/Enter on) a row calls `onToggle(id)` — the selection stays the
//    full set of ids (multi-select, unchanged from the old checkbox lists). A visual checkbox mirrors
//    `aria-selected` for sighted users.
//  - Closes on outside click and Escape, returning focus to the trigger (mirrors ConfirmDialog /
//    AppLayout's user menu). Arrow keys / Home / End move option focus; Tab moves through the options
//    and then out of the panel, which also closes it.
//
// Rendering of each option and of the selected chips is delegated to the caller (`renderOption` /
// `renderSelected`) so the same component serves assignee names and colored label chips.

import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

export interface MultiSelectOption {
  id: string;
}

interface MultiSelectDropdownProps<T extends MultiSelectOption> {
  /** Id for the trigger button, so a form <label htmlFor> can point at it. */
  id?: string;
  /** Accessible name for the trigger + listbox (e.g. the field label). */
  ariaLabel: string;
  options: T[];
  /** The full set of selected ids (multi-select value). */
  selectedIds: string[];
  onToggle: (id: string) => void;
  /** Renders one option's content (right of the checkbox), e.g. a name or a colored chip. */
  renderOption: (option: T) => ReactNode;
  /** Renders one selected item as a compact chip in the trigger summary. */
  renderSelected: (option: T) => ReactNode;
  /** Trigger text shown when nothing is selected. */
  placeholder: string;
  disabled?: boolean;
}

export function MultiSelectDropdown<T extends MultiSelectOption>({
  id,
  ariaLabel,
  options,
  selectedIds,
  onToggle,
  renderOption,
  renderSelected,
  placeholder,
  disabled = false,
}: MultiSelectDropdownProps<T>) {
  const { t } = useTranslation('common');
  const [open, setOpen] = useState(false);
  // Index of the option that currently has roving focus inside the open panel (-1 = none yet).
  const [activeIndex, setActiveIndex] = useState(-1);
  const rootRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const optionRefs = useRef<Array<HTMLDivElement | null>>([]);
  const listboxId = useId();

  const selected = options.filter((o) => selectedIds.includes(o.id));

  // Outside-click closes the panel (mirrors AppLayout's user menu).
  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, [open]);

  // When the panel opens, move focus to the first selected option (or the first option), so keyboard
  // users land inside the list. When it closes, return focus to the trigger (WCAG 2.4.3).
  useEffect(() => {
    if (!open) return;
    const firstSelected = options.findIndex((o) => selectedIds.includes(o.id));
    const start = firstSelected >= 0 ? firstSelected : options.length > 0 ? 0 : -1;
    setActiveIndex(start);
    if (start >= 0) {
      // Focus after paint so the option nodes exist.
      requestAnimationFrame(() => optionRefs.current[start]?.focus());
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const close = (returnFocus = true) => {
    setOpen(false);
    setActiveIndex(-1);
    if (returnFocus) triggerRef.current?.focus();
  };

  const focusOption = (index: number) => {
    if (index < 0 || index >= options.length) return;
    setActiveIndex(index);
    optionRefs.current[index]?.focus();
  };

  const onTriggerKeyDown = (e: React.KeyboardEvent<HTMLButtonElement>) => {
    if (disabled) return;
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ')) {
      e.preventDefault();
      setOpen(true);
    }
  };

  const onOptionKeyDown = (e: React.KeyboardEvent<HTMLDivElement>, index: number) => {
    switch (e.key) {
      case 'Escape':
        e.preventDefault();
        close();
        break;
      case 'ArrowDown':
        e.preventDefault();
        focusOption(Math.min(index + 1, options.length - 1));
        break;
      case 'ArrowUp':
        e.preventDefault();
        focusOption(Math.max(index - 1, 0));
        break;
      case 'Home':
        e.preventDefault();
        focusOption(0);
        break;
      case 'End':
        e.preventDefault();
        focusOption(options.length - 1);
        break;
      case ' ':
      case 'Enter':
        // Space/Enter toggles selection without closing (multi-select).
        e.preventDefault();
        onToggle(options[index]!.id);
        break;
      default:
        break;
    }
  };

  // Escape from the trigger (panel open) closes without moving option focus first.
  const onRootKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape' && open) {
      e.preventDefault();
      close();
    }
  };

  return (
    <div className="multiselect" ref={rootRef} onKeyDown={onRootKeyDown}>
      <button
        id={id}
        ref={triggerRef}
        type="button"
        className="multiselect-trigger"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? listboxId : undefined}
        aria-label={ariaLabel}
        disabled={disabled}
        onClick={() => setOpen((v) => !v)}
        onKeyDown={onTriggerKeyDown}
      >
        <span className="multiselect-summary">
          {selected.length > 0 ? (
            selected.map((o) => (
              <span key={o.id} className="multiselect-chip">
                {renderSelected(o)}
              </span>
            ))
          ) : (
            <span className="multiselect-placeholder">{placeholder}</span>
          )}
        </span>
        <span className="multiselect-caret" aria-hidden="true">
          ▾
        </span>
      </button>

      {open ? (
        <div
          id={listboxId}
          className="multiselect-panel"
          role="listbox"
          aria-multiselectable="true"
          aria-label={ariaLabel}
        >
          {options.map((option, index) => {
            const isSelected = selectedIds.includes(option.id);
            return (
              <div
                key={option.id}
                ref={(el) => {
                  optionRefs.current[index] = el;
                }}
                role="option"
                aria-selected={isSelected}
                tabIndex={index === activeIndex ? 0 : -1}
                className="multiselect-option"
                onClick={() => onToggle(option.id)}
                onKeyDown={(e) => onOptionKeyDown(e, index)}
              >
                <span
                  className={`multiselect-check${isSelected ? ' is-checked' : ''}`}
                  aria-hidden="true"
                >
                  {isSelected ? '✓' : ''}
                </span>
                <span className="multiselect-option-content">{renderOption(option)}</span>
              </div>
            );
          })}
          <span className="sr-only" role="status">
            {t('multiSelect.selectedCount', { count: selected.length })}
          </span>
        </div>
      ) : null}
    </div>
  );
}
