// Multi-select for team membership: a checkbox per team. Used by the Create and Edit user
// dialogs. An admin's memberships are ignored by the backend, but assigning them is harmless.

import type { Team } from '@/api/types';

interface TeamCheckboxListProps {
  teams: Team[];
  selected: Set<string>;
  disabled?: boolean;
  onToggle: (teamId: string) => void;
}

export function TeamCheckboxList({ teams, selected, disabled, onToggle }: TeamCheckboxListProps) {
  if (teams.length === 0) {
    return <p className="field-hint">No teams exist yet. Create a team first to assign membership.</p>;
  }

  return (
    <div className="team-checkbox-list" role="group" aria-label="Teams">
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
