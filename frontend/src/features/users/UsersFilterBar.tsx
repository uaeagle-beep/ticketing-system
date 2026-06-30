// Users-list filter bar (USER_MANAGEMENT_DESIGN Feature 2). Client-side filters over the already-
// loaded admin user list (the list is admin-only and small, so no server round-trip — design
// "client filters are sufficient"). Filters combine with AND: a free-text search over name OR email,
// plus role / team / email-verification / status dropdowns. Clear resets everything; the count shows
// how many users match. Mirrors the board FilterBar pattern (labels + aria on every control).

import type { Team } from '@/api/types';

export type RoleFilter = 'all' | 'admin' | 'member';
export type VerifiedFilter = 'all' | 'verified' | 'unverified';
export type StatusFilter = 'all' | 'active' | 'blocked';

export interface UsersFilters {
  // Free-text: case-insensitive substring over name OR email.
  search: string;
  role: RoleFilter;
  // 'all' or a specific team id (membership-based).
  teamId: string;
  verified: VerifiedFilter;
  status: StatusFilter;
}

export const EMPTY_USERS_FILTERS: UsersFilters = {
  search: '',
  role: 'all',
  teamId: 'all',
  verified: 'all',
  status: 'all',
};

export function hasActiveUsersFilters(f: UsersFilters): boolean {
  return (
    f.search.trim() !== '' ||
    f.role !== 'all' ||
    f.teamId !== 'all' ||
    f.verified !== 'all' ||
    f.status !== 'all'
  );
}

interface UsersFilterBarProps {
  filters: UsersFilters;
  teams: Team[];
  matchCount: number;
  onChange: (next: UsersFilters) => void;
  onClear: () => void;
}

export function UsersFilterBar({
  filters,
  teams,
  matchCount,
  onChange,
  onClear,
}: UsersFilterBarProps) {
  const active = hasActiveUsersFilters(filters);

  return (
    <div className="filter-bar">
      <input
        className="input search"
        type="search"
        placeholder="Search name or email…"
        aria-label="Search by name or email"
        value={filters.search}
        onChange={(e) => onChange({ ...filters, search: e.target.value })}
      />

      <select
        className="select"
        aria-label="Filter by role"
        value={filters.role}
        onChange={(e) => onChange({ ...filters, role: e.target.value as RoleFilter })}
      >
        <option value="all">All roles</option>
        <option value="admin">Admin</option>
        <option value="member">Member</option>
      </select>

      <select
        className="select"
        aria-label="Filter by team"
        value={filters.teamId}
        onChange={(e) => onChange({ ...filters, teamId: e.target.value })}
      >
        <option value="all">All teams</option>
        {teams.map((team) => (
          <option key={team.id} value={team.id}>
            {team.name}
          </option>
        ))}
      </select>

      <select
        className="select"
        aria-label="Filter by email verification"
        value={filters.verified}
        onChange={(e) => onChange({ ...filters, verified: e.target.value as VerifiedFilter })}
      >
        <option value="all">All emails</option>
        <option value="verified">Verified</option>
        <option value="unverified">Unverified</option>
      </select>

      <select
        className="select"
        aria-label="Filter by status"
        value={filters.status}
        onChange={(e) => onChange({ ...filters, status: e.target.value as StatusFilter })}
      >
        <option value="all">All statuses</option>
        <option value="active">Active</option>
        <option value="blocked">Blocked</option>
      </select>

      <button type="button" className="btn btn-secondary" onClick={onClear} disabled={!active}>
        Clear
      </button>

      <span className="filter-count">
        {matchCount} {matchCount === 1 ? 'user' : 'users'}
      </span>
    </div>
  );
}
