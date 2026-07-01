// Map API errors to user-facing messages. Falls back to the server-provided
// message, then to a generic message, so every error surface is human-readable
// (NFR-USE-3).

import { ApiError } from '@/api/client';

const FRIENDLY: Record<string, string> = {
  duplicate_team_name: 'A team with this name already exists.',
  team_has_children:
    'Cannot delete a team that still has tickets or epics. Remove them first.',
  epic_referenced_by_tickets:
    'Cannot delete an epic that is referenced by tickets. Reassign or remove those tickets first.',
  epic_team_mismatch: 'The selected epic does not belong to the ticket’s team.',
  wip_limit_reached:
    'This status already has the maximum number of tickets — finish existing ones first.',
  invalid_credentials: 'Invalid email or password.',
  account_not_verified:
    'Your account is not verified. Check your email or request a new verification link.',
  invalid_or_expired_token:
    'This link is invalid or has expired. Request a new one.',
  not_found: 'The requested item could not be found.',
  unauthorized: 'Your session has expired. Please log in again.',
  service_unavailable: 'The server is unavailable. Please try again in a moment.',
  // User Management (ADR-0007/0008).
  forbidden: 'You do not have permission to perform this action.',
  account_blocked: 'This account has been blocked. Contact an administrator.',
  last_admin_required: 'The system must keep at least one active administrator.',
  email_in_use: 'A user with this email already exists.',
  // Labels (Wave 2, ADR-0016).
  duplicate_label_name: 'A label with this name already exists in this team.',
};

export function errorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    // Prefer per-field validation text when present (most specific).
    const fieldText = err.fieldErrorText();
    if (err.code === 'validation_error' && fieldText) return fieldText;
    return FRIENDLY[err.code] ?? err.message ?? 'Something went wrong.';
  }
  if (err instanceof Error) return err.message;
  return 'Something went wrong.';
}

export function isApiErrorCode(err: unknown, code: string): boolean {
  return err instanceof ApiError && err.code === code;
}
