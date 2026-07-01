// Multi-select label picker for the ticket create/edit form (Wave 2, §9.4, ADR-0016). Renders via the
// shared accessible MultiSelectDropdown: a trigger button showing the selected labels as colored chips,
// and a popover listbox of the team's labels (each row shows its colored chip so it is recognizable).
// The value is the full selected set (submitted via PUT /api/tickets/{id}/labels). Label CREATION lives
// on the management surface (Teams page), so this picker only selects from existing labels; when a team
// has none it points the user there.

import { useTranslation } from 'react-i18next';
import type { LabelRef } from '@/api/types';
import { LabelChip } from '@/components/Badges';
import { MultiSelectDropdown } from '@/components/MultiSelectDropdown';

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
    <MultiSelectDropdown
      id="ticket-labels"
      ariaLabel={t('picker.groupLabel')}
      options={labels}
      selectedIds={selectedIds}
      onToggle={onToggle}
      disabled={disabled}
      placeholder={t('picker.placeholder')}
      renderOption={(label) => <LabelChip label={label} />}
      renderSelected={(label) => <LabelChip label={label} />}
    />
  );
}
