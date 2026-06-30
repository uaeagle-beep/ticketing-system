// Pure client-side filtering for the admin Users list (USER_MANAGEMENT_DESIGN Feature 2). Kept
// separate from the component so the AND-combination logic is unit-testable in isolation. Filters
// the already-loaded list; the list is admin-only and small, so this is purely in-memory.

import type { AdminUser } from '@/api/types';
import { displayName } from '@/lib/displayName';
import type { UsersFilters } from './UsersFilterBar';

/**
 * Apply all active filters with AND semantics. Returns the matching subset in input order.
 * - search: case-insensitive substring over the display name OR the email.
 * - role: admin vs member (isAdmin).
 * - teamId: a specific membership (by team id); admins are matched on their actual memberships.
 * - verified: emailVerified true/false.
 * - status: derived status (active/blocked) — 'active' here means "not blocked".
 */
export function filterUsers(users: AdminUser[], filters: UsersFilters): AdminUser[] {
  const term = filters.search.trim().toLowerCase();

  return users.filter((u) => {
    // Free-text over name OR email (case-insensitive substring).
    if (term) {
      const haystack = `${displayName(u.name, u.email)} ${u.email}`.toLowerCase();
      if (!haystack.includes(term)) return false;
    }

    // Role.
    if (filters.role === 'admin' && !u.isAdmin) return false;
    if (filters.role === 'member' && u.isAdmin) return false;

    // Team (by membership).
    if (filters.teamId !== 'all' && !u.teams.some((t) => t.id === filters.teamId)) return false;

    // Email verification.
    if (filters.verified === 'verified' && !u.emailVerified) return false;
    if (filters.verified === 'unverified' && u.emailVerified) return false;

    // Status (active = not blocked; blocked = blocked).
    if (filters.status === 'active' && u.isBlocked) return false;
    if (filters.status === 'blocked' && !u.isBlocked) return false;

    return true;
  });
}
