// Multi-select for team membership: a checkbox per team. Used by the Create and Edit user
// dialogs. An admin's memberships are ignored by the backend, but assigning them is harmless.

import { useTranslation } from 'react-i18next';
import type { Team } from '@/api/types';

interface TeamCheckboxListProps {
  teams: Team[];
  selected: Set<string>;
  disabled?: boolean;
  onToggle: (teamId: string) => void;
}

export function TeamCheckboxList({ teams, selected, disabled, onToggle }: TeamCheckboxListProps) {
  const { t } = useTranslation('users');

  if (teams.length === 0) {
    return <p className="field-hint">{t('teamCheckboxList.empty')}</p>;
  }

  return (
    <div className="team-checkbox-list" role="group" aria-label={t('teamCheckboxList.groupLabel')}>
      {teams.map((team) => (
        <label key={team.id} className="team-checkbox">
          <input
            type="checkbox"
            checked={selected.has(team.id)}
            disabled={disabled}
            onChange={() => onToggle(team.id)}
          />
          <span>{team.name}</span>
        </label>
      ))}
    </div>
  );
}
