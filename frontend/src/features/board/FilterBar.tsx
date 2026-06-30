// Board filter bar (Wireframe 1): title search, Type dropdown, Epic dropdown,
// Clear button, and the total ticket count (post-filter, A23). Filters combine
// with AND logic, applied server-side via the board query.

import type { BoardFilters, Epic, TicketType } from '@/api/types';
import { typeOptions } from '@/lib/labels';

interface FilterBarProps {
  filters: BoardFilters;
  epics: Epic[];
  epicsLoading: boolean;
  total: number;
  onChange: (next: BoardFilters) => void;
  onClear: () => void;
}

export function FilterBar({
  filters,
  epics,
  epicsLoading,
  total,
  onChange,
  onClear,
}: FilterBarProps) {
  const hasActiveFilters = Boolean(filters.type || filters.epicId || filters.search);

  return (
    <div className="filter-bar">
      <input
        className="input search"
        type="search"
        placeholder="Search title…"
        aria-label="Search by title"
        value={filters.search ?? ''}
        onChange={(e) => onChange({ ...filters, search: e.target.value || undefined })}
      />

      <select
        className="select"
        aria-label="Filter by type"
        value={filters.type ?? ''}
        onChange={(e) =>
          onChange({ ...filters, type: (e.target.value || undefined) as TicketType | undefined })
        }
      >
        <option value="">All types</option>
        {typeOptions.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>

      <select
        className="select"
        aria-label="Filter by epic"
        value={filters.epicId ?? ''}
        onChange={(e) => onChange({ ...filters, epicId: e.target.value || undefined })}
        disabled={epicsLoading}
      >
        <option value="">All epics</option>
        {epics.map((epic) => (
          <option key={epic.id} value={epic.id}>
            {epic.title}
          </option>
        ))}
      </select>

      <button
        type="button"
        className="btn btn-secondary"
        onClick={onClear}
        disabled={!hasActiveFilters}
      >
        Clear
      </button>

      <span className="filter-count">
        {total} {total === 1 ? 'ticket' : 'tickets'}
      </span>
    </div>
  );
}
