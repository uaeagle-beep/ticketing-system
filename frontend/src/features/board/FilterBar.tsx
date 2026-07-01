// Board filter bar (Wireframe 1, extended for Wave 1): title search, Type, Epic, Priority, Due, and
// Assignee filters, a Clear button, and the total ticket count (post-filter, A23). Filters combine
// with AND logic, applied server-side via the board query.
//
// Assignee filtering: "Assigned to me" (universal — uses the current user id, no member listing) plus,
// when a candidate-user source is available (`assigneeOptions`, populated for admins from the admin
// users list — see the contract-gap note in useTeamMembers), a by-specific-user option. When no source
// is available (a non-admin member, since there is no member-listing endpoint), only "Assigned to me"
// is offered. `assignedToMe` wins over `assigneeId` (documented precedence, §4.2).

import { useTranslation } from 'react-i18next';
import type { AssigneeRef, BoardFilters, DueFilter, Epic, Label, TicketPriority, TicketType } from '@/api/types';
import { dueFilterOptions, priorityOptions, typeOptions } from '@/lib/labels';

interface FilterBarProps {
  filters: BoardFilters;
  epics: Epic[];
  epicsLoading: boolean;
  total: number;
  // Candidate users for the by-user assignee filter (may be empty when no source is available).
  assigneeOptions?: AssigneeRef[];
  // The team's labels for the by-label filter (Wave 2, §8.4). Empty when the team has none.
  labelOptions?: Label[];
  onChange: (next: BoardFilters) => void;
  onClear: () => void;
}

export function FilterBar({
  filters,
  epics,
  epicsLoading,
  total,
  assigneeOptions = [],
  labelOptions = [],
  onChange,
  onClear,
}: FilterBarProps) {
  const { t } = useTranslation('board');
  const hasActiveFilters = Boolean(
    filters.type ||
      filters.epicId ||
      filters.search ||
      filters.priority ||
      filters.assignedToMe ||
      filters.assigneeId ||
      filters.dueFilter ||
      filters.labelId,
  );

  // The assignee <select> encodes three cases in one control: '' (all), 'me' (assignedToMe), or a
  // specific user id.
  const assigneeValue = filters.assignedToMe ? 'me' : (filters.assigneeId ?? '');
  const onAssigneeChange = (value: string) => {
    if (value === '') onChange({ ...filters, assignedToMe: undefined, assigneeId: undefined });
    else if (value === 'me') onChange({ ...filters, assignedToMe: true, assigneeId: undefined });
    else onChange({ ...filters, assignedToMe: undefined, assigneeId: value });
  };

  return (
    <div className="filter-bar">
      <input
        className="input search"
        type="search"
        placeholder={t('filters.searchPlaceholder')}
        aria-label={t('filters.searchLabel')}
        value={filters.search ?? ''}
        onChange={(e) => onChange({ ...filters, search: e.target.value || undefined })}
      />

      <select
        className="select"
        aria-label={t('filters.byType')}
        value={filters.type ?? ''}
        onChange={(e) =>
          onChange({ ...filters, type: (e.target.value || undefined) as TicketType | undefined })
        }
      >
        <option value="">{t('filters.allTypes')}</option>
        {typeOptions().map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>

      <select
        className="select"
        aria-label={t('filters.byPriority')}
        value={filters.priority ?? ''}
        onChange={(e) =>
          onChange({
            ...filters,
            priority: (e.target.value || undefined) as TicketPriority | undefined,
          })
        }
      >
        <option value="">{t('filters.allPriorities')}</option>
        {priorityOptions().map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>

      <select
        className="select"
        aria-label={t('filters.byEpic')}
        value={filters.epicId ?? ''}
        onChange={(e) => onChange({ ...filters, epicId: e.target.value || undefined })}
        disabled={epicsLoading}
      >
        <option value="">{t('filters.allEpics')}</option>
        {epics.map((epic) => (
          <option key={epic.id} value={epic.id}>
            {epic.title}
          </option>
        ))}
      </select>

      <select
        className="select"
        aria-label={t('filters.byAssignee')}
        value={assigneeValue}
        onChange={(e) => onAssigneeChange(e.target.value)}
      >
        <option value="">{t('filters.allAssignees')}</option>
        <option value="me">{t('filters.assignedToMe')}</option>
        {assigneeOptions.map((u) => (
          <option key={u.id} value={u.id}>
            {u.displayName}
          </option>
        ))}
      </select>

      {labelOptions.length > 0 ? (
        <select
          className="select"
          aria-label={t('filters.byLabel')}
          value={filters.labelId ?? ''}
          onChange={(e) => onChange({ ...filters, labelId: e.target.value || undefined })}
        >
          <option value="">{t('filters.allLabels')}</option>
          {labelOptions.map((label) => (
            <option key={label.id} value={label.id}>
              {label.name}
            </option>
          ))}
        </select>
      ) : null}

      <select
        className="select"
        aria-label={t('filters.byDueDate')}
        value={filters.dueFilter ?? ''}
        onChange={(e) =>
          onChange({ ...filters, dueFilter: (e.target.value || undefined) as DueFilter | undefined })
        }
      >
        <option value="">{t('filters.allDueDates')}</option>
        {dueFilterOptions().map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>

      <button
        type="button"
        className="btn btn-secondary"
        onClick={onClear}
        disabled={!hasActiveFilters}
      >
        {t('filters.clear')}
      </button>

      <span className="filter-count">{t('filters.count', { count: total })}</span>
    </div>
  );
}
