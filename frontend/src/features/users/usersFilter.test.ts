import { describe, expect, it } from 'vitest';
import type { AdminUser } from '@/api/types';
import { filterUsers } from './usersFilter';
import { EMPTY_USERS_FILTERS, type UsersFilters } from './UsersFilterBar';

const platform = { id: 'team-platform', name: 'Platform' };
const payments = { id: 'team-payments', name: 'Payments' };

function user(overrides: Partial<AdminUser>): AdminUser {
  return {
    id: crypto.randomUUID(),
    email: 'someone@dataart.com',
    name: null,
    isAdmin: false,
    isBlocked: false,
    emailVerified: true,
    status: 'active',
    createdAt: '2026-06-20T08:00:00Z',
    teams: [],
    ...overrides,
  };
}

// A representative roster covering every filterable dimension.
const ada = user({ email: 'ada@dataart.com', name: 'Ada Lovelace', isAdmin: true, teams: [platform] });
const grace = user({ email: 'grace@dataart.com', name: 'Grace Hopper', teams: [payments] });
const linus = user({ email: 'linus@dataart.com', name: null, teams: [platform] });
const blocked = user({ email: 'blocked@dataart.com', isBlocked: true, status: 'blocked', teams: [payments] });
const unverified = user({ email: 'pending@dataart.com', emailVerified: false, status: 'unverified', teams: [] });

const roster = [ada, grace, linus, blocked, unverified];

function withFilters(partial: Partial<UsersFilters>): UsersFilters {
  return { ...EMPTY_USERS_FILTERS, ...partial };
}

describe('filterUsers', () => {
  it('returns everything when no filters are active', () => {
    expect(filterUsers(roster, EMPTY_USERS_FILTERS)).toEqual(roster);
  });

  // ---- search (name OR email, case-insensitive substring) ----

  it('matches the search term against the display name', () => {
    const result = filterUsers(roster, withFilters({ search: 'grace' }));
    expect(result).toEqual([grace]);
  });

  it('matches the search term against the email', () => {
    const result = filterUsers(roster, withFilters({ search: 'linus@data' }));
    expect(result).toEqual([linus]);
  });

  it('matching by email works even when only the email is set (no name)', () => {
    const result = filterUsers(roster, withFilters({ search: 'PENDING' }));
    expect(result).toEqual([unverified]);
  });

  it('is case-insensitive', () => {
    expect(filterUsers(roster, withFilters({ search: 'ADA LOVELACE' }))).toEqual([ada]);
  });

  // ---- role ----

  it('filters by admin role', () => {
    expect(filterUsers(roster, withFilters({ role: 'admin' }))).toEqual([ada]);
  });

  it('filters by member role', () => {
    expect(filterUsers(roster, withFilters({ role: 'member' }))).toEqual([
      grace,
      linus,
      blocked,
      unverified,
    ]);
  });

  // ---- team (membership) ----

  it('filters by a specific team membership', () => {
    expect(filterUsers(roster, withFilters({ teamId: platform.id }))).toEqual([ada, linus]);
  });

  // ---- email verification ----

  it('filters to verified emails', () => {
    expect(filterUsers(roster, withFilters({ verified: 'verified' }))).toEqual([
      ada,
      grace,
      linus,
      blocked,
    ]);
  });

  it('filters to unverified emails', () => {
    expect(filterUsers(roster, withFilters({ verified: 'unverified' }))).toEqual([unverified]);
  });

  // ---- status ----

  it('filters to active (not blocked) users', () => {
    expect(filterUsers(roster, withFilters({ status: 'active' }))).toEqual([
      ada,
      grace,
      linus,
      unverified,
    ]);
  });

  it('filters to blocked users', () => {
    expect(filterUsers(roster, withFilters({ status: 'blocked' }))).toEqual([blocked]);
  });

  // ---- AND combination ----

  it('combines filters with AND logic', () => {
    // member + Platform team + verified + active => only linus (ada is admin).
    const result = filterUsers(
      roster,
      withFilters({ role: 'member', teamId: platform.id, verified: 'verified', status: 'active' }),
    );
    expect(result).toEqual([linus]);
  });

  it('returns an empty list when filters intersect to nothing', () => {
    // admin role AND Payments team — ada (admin) is on Platform, not Payments.
    expect(filterUsers(roster, withFilters({ role: 'admin', teamId: payments.id }))).toEqual([]);
  });
});
