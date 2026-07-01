// Multi-select label picker for the ticket create/edit form (Wave 2, §9.4, ADR-0016). Mirrors the
// assignee-picker pattern in TicketPage: a group of toggle checkboxes over the team's labels, whose value
// is the full selected set (submitted via PUT /api/tickets/{id}/labels). Each option shows the label's
// colored chip so it is recognizable. Label CREATION lives on the management surface (Teams page), so this
// picker only selects from existing labels; when a team has none it points the user there.

import { useTranslation } from 'react-i18next';
import type { LabelRef } from '@/api/types';
import { LabelChip } from '@/components/Badges';

interface LabelPickerProps {
  labels: LabelRef[];
  selectedIds: string[];
  disabled?: boolean;
  onToggle: (labelId: string) => void;
}

export function LabelPicker({ labels, selectedIds, disabled, onToggle }: LabelPickerProps) {
  const { t } = useTranslation('labels');
  if (labels.length === 0) {
    return <span className="muted">{t('picker.empty')}</span>;
  }

  return (
    <div id="ticket-labels" className="assignee-picker" role="group" aria-label={t('picker.groupLabel')}>
      {labels.map((label) => (
        <label key={label.id} className="assignee-option">
          <input
            type="checkbox"
            checked={selectedIds.includes(label.id)}
            onChange={() => onToggle(label.id)}
            disabled={disabled}
          />
          <LabelChip label={label} />
        </label>
      ))}
    </div>
  );
}
