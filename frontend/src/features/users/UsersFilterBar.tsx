// Users-list filter bar (USER_MANAGEMENT_DESIGN Feature 2). Client-side filters over the already-
// loaded admin user list (the list is admin-only and small, so no server round-trip — design
// "client filters are sufficient"). Filters combine with AND: a free-text search over name OR email,
// plus role / team / email-verification / status dropdowns. Clear resets everything; the count shows
// how many users match. Mirrors the board FilterBar pattern (labels + aria on every control).

import { useTranslation } from 'react-i18next';
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
  const { t } = useTranslation('users');
  const active = hasActiveUsersFilters(filters);

  return (
    <div className="filter-bar">
      <input
        className="input search"
        type="search"
        placeholder={t('filters.searchPlaceholder')}
        aria-label={t('filters.searchLabel')}
        value={filters.search}
        onChange={(e) => onChange({ ...filters, search: e.target.value })}
      />

      <select
        className="select"
        aria-label={t('filters.byRole')}
        value={filters.role}
        onChange={(e) => onChange({ ...filters, role: e.target.value as RoleFilter })}
      >
        <option value="all">{t('filters.allRoles')}</option>
        <option value="admin">{t('filters.roleAdmin')}</option>
        <option value="member">{t('filters.roleMember')}</option>
      </select>

      <select
        className="select"
        aria-label={t('filters.byTeam')}
        value={filters.teamId}
        onChange={(e) => onChange({ ...filters, teamId: e.target.value })}
      >
        <option value="all">{t('filters.allTeams')}</option>
        {teams.map((team) => (
          <option key={team.id} value={team.id}>
            {team.name}
          </option>
        ))}
      </select>

      <select
        className="select"
        aria-label={t('filters.byVerification')}
        value={filters.verified}
        onChange={(e) => onChange({ ...filters, verified: e.target.value as VerifiedFilter })}
      >
        <option value="all">{t('filters.allEmails')}</option>
        <option value="verified">{t('filters.verified')}</option>
        <option value="unverified">{t('filters.unverified')}</option>
      </select>

      <select
        className="select"
        aria-label={t('filters.byStatus')}
        value={filters.status}
        onChange={(e) => onChange({ ...filters, status: e.target.value as StatusFilter })}
      >
        <option value="all">{t('filters.allStatuses')}</option>
        <option value="active">{t('filters.active')}</option>
        <option value="blocked">{t('filters.blocked')}</option>
      </select>

      <button type="button" className="btn btn-secondary" onClick={onClear} disabled={!active}>
        {t('filters.clear')}
      </button>

      <span className="filter-count">{t('filters.count', { count: matchCount })}</span>
    </div>
  );
}
