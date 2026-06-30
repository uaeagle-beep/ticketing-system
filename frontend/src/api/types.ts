// API contract types — kept in exact sync with docs/API_CONTRACT.md.
// Enums are canonical lowercase strings exactly as the API serializes/accepts them.

export const TICKET_TYPES = ['bug', 'feature', 'fix'] as const;
export type TicketType = (typeof TICKET_TYPES)[number];

// Workflow order is significant: this is the column order on the board (FR-E6-2).
export const TICKET_STATES = [
  'new',
  'ready_for_implementation',
  'in_progress',
  'ready_for_acceptance',
  'done',
] as const;
export type TicketState = (typeof TICKET_STATES)[number];

// ---- Error envelope (API_CONTRACT §2) ----
export type ApiErrorCode =
  | 'validation_error'
  | 'epic_team_mismatch'
  | 'unauthorized'
  | 'invalid_credentials'
  | 'account_not_verified'
  | 'not_found'
  | 'duplicate_team_name'
  | 'team_has_children'
  | 'epic_referenced_by_tickets'
  | 'wip_limit_reached'
  | 'invalid_or_expired_token'
  // User Management (ADR-0007/0008).
  | 'forbidden'
  | 'account_blocked'
  | 'last_admin_required'
  | 'email_in_use'
  // Defensive fallback for any code not enumerated above.
  | (string & {});

export interface ApiErrorBody {
  error: {
    code: ApiErrorCode;
    message: string;
    errors?: Record<string, string[]>;
  };
}

// ---- Auth (API_CONTRACT §3) ----

// A lightweight team reference (id + name) carried inside user payloads (ADR-0007).
export interface TeamRef {
  id: string;
  name: string;
}

export interface AuthUser {
  id: string;
  email: string;
  // Optional display name (Feature 1). null/absent => the UI shows the email.
  name: string | null;
  emailVerified: boolean;
  // User Management (ADR-0007): admin flag + memberships drive nav/team-scoping.
  isAdmin: boolean;
  isBlocked: boolean;
  teams: TeamRef[];
}

export interface LoginResponse {
  token: string;
  user: AuthUser;
  expiresAt: string; // ISO-8601 UTC
}

export interface MessageResponse {
  message: string;
}

// ---- Teams (API_CONTRACT §4) ----

// Per-state WIP caps: one entry per state, null = unlimited (API_CONTRACT §4).
// All five states are always present in a Team response.
export type WipLimits = Record<TicketState, number | null>;

export interface Team {
  id: string;
  name: string;
  ticketCount: number;
  epicCount: number;
  createdAt: string; // ISO-8601 UTC
  modifiedAt: string; // ISO-8601 UTC
  wipLimits: WipLimits;
}

// ---- Epics (API_CONTRACT §5) ----
export interface Epic {
  id: string;
  teamId: string;
  title: string;
  description: string | null;
  ticketCount: number;
  createdAt: string;
  modifiedAt: string;
}

// ---- Tickets (API_CONTRACT §6) ----
export interface TicketDetail {
  id: string;
  teamId: string;
  epicId: string | null;
  epicTitle: string | null;
  type: TicketType;
  state: TicketState;
  title: string;
  body: string;
  createdAt: string;
  modifiedAt: string;
  createdBy: string;
  createdByEmail: string;
  // Creator's optional display name (Feature 1). null => the UI shows createdByEmail.
  createdByName: string | null;
}

// Card payload as returned inside the board columns (subset of the detail).
export interface TicketCard {
  id: string;
  type: TicketType;
  state: TicketState;
  title: string;
  epicId: string | null;
  epicTitle: string | null;
  modifiedAt: string;
}

export interface BoardColumn {
  state: TicketState;
  // Post-filter card count for `tickets` (A23).
  count: number;
  // Unfiltered per-state total for the team — the WIP badge "N / max" numerator and
  // the full/over comparison use this so a filter can't make a full column look not-full (UX §3.1).
  total: number;
  // The cap for this state; null = unlimited.
  wipLimit: number | null;
  tickets: TicketCard[];
}

export interface Board {
  teamId: string;
  total: number;
  columns: BoardColumn[];
}

// Response of PATCH /api/tickets/{id}/state — minimally state + modifiedAt,
// though the API may return the full card/detail. We type the guaranteed fields.
export interface TicketStatePatchResponse {
  id: string;
  state: TicketState;
  modifiedAt: string;
}

// ---- Comments (API_CONTRACT §7) ----
export interface Comment {
  id: string;
  ticketId: string;
  authorId: string;
  authorEmail: string;
  // Author's optional display name (Feature 1). null => the UI shows authorEmail.
  authorName: string | null;
  body: string;
  createdAt: string;
}

// ---- Request bodies ----
export interface SignupRequest {
  email: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface VerifyEmailRequest {
  token: string;
}

export interface ResendVerificationRequest {
  email: string;
}

export interface CreateTeamRequest {
  name: string;
}

export interface RenameTeamRequest {
  name: string;
}

// PUT /api/teams/{id}/wip-limits — a map of state -> cap. null (or omitted) = unlimited.
export interface UpdateWipLimitsRequest {
  wipLimits: Partial<Record<TicketState, number | null>>;
}

export interface CreateEpicRequest {
  teamId: string;
  title: string;
  description?: string | null;
}

export interface UpdateEpicRequest {
  title: string;
  description?: string | null;
}

export interface CreateTicketRequest {
  teamId: string;
  type: TicketType;
  title: string;
  body: string;
  epicId?: string | null;
  state?: TicketState;
}

export interface UpdateTicketRequest {
  teamId: string;
  type: TicketType;
  epicId: string | null;
  title: string;
  body: string;
  state: TicketState;
}

export interface PatchTicketStateRequest {
  state: TicketState;
}

export interface CreateCommentRequest {
  body: string;
}

// ---- Board filters (client-facing) ----
export interface BoardFilters {
  type?: TicketType;
  epicId?: string;
  search?: string;
}

// ---- Admin — User Management (API_CONTRACT §8, ADR-0007) ----

// Derived status string shown in the Users list (also conveyed by the booleans).
export type AdminUserStatus = 'active' | 'unverified' | 'blocked';

export interface AdminUser {
  id: string;
  email: string;
  // Optional display name (Feature 1). null => the UI shows the email.
  name: string | null;
  isAdmin: boolean;
  isBlocked: boolean;
  emailVerified: boolean;
  status: AdminUserStatus;
  createdAt: string; // ISO-8601 UTC
  teams: TeamRef[];
}

export interface CreateUserRequest {
  email: string;
  // null/blank => the server generates a strong password and returns it once.
  password?: string | null;
  // Optional display name. null/blank => stored as null (UI shows the email).
  name?: string | null;
  isAdmin: boolean;
  teamIds?: string[] | null;
}

export interface SetNameRequest {
  // null/blank => clears the display name (UI shows the email).
  name: string | null;
}

export interface CreateUserResponse {
  user: AdminUser;
  // Present (shown once) only when the server generated the password.
  generatedPassword: string | null;
}

export interface SetRoleRequest {
  isAdmin: boolean;
}

export interface SetTeamsRequest {
  teamIds: string[] | null;
}

export interface ResetPasswordResponse {
  generatedPassword: string;
}
