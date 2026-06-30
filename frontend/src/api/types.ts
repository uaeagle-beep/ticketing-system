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
  | 'invalid_or_expired_token'
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
export interface AuthUser {
  id: string;
  email: string;
  emailVerified: boolean;
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
export interface Team {
  id: string;
  name: string;
  ticketCount: number;
  epicCount: number;
  createdAt: string; // ISO-8601 UTC
  modifiedAt: string; // ISO-8601 UTC
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
  count: number;
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
