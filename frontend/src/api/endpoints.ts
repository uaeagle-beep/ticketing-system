// Typed endpoint functions — one per route in docs/API_CONTRACT.md.
// These are the ONLY place that knows API paths/methods/bodies.

import { http } from './client';
import type {
  AuthUser,
  Board,
  BoardFilters,
  Comment,
  CreateCommentRequest,
  CreateEpicRequest,
  CreateTeamRequest,
  CreateTicketRequest,
  Epic,
  LoginRequest,
  LoginResponse,
  MessageResponse,
  PatchTicketStateRequest,
  RenameTeamRequest,
  ResendVerificationRequest,
  SignupRequest,
  Team,
  TicketDetail,
  TicketStatePatchResponse,
  UpdateEpicRequest,
  UpdateTicketRequest,
  VerifyEmailRequest,
} from './types';

// ---- Auth (§3) ----
export const authApi = {
  // POST /api/auth/signup -> 201 { message }
  signup: (body: SignupRequest) => http.post<MessageResponse>('/auth/signup', body),

  // POST /api/auth/login -> 200 { token, user, expiresAt }
  login: (body: LoginRequest) => http.post<LoginResponse>('/auth/login', body),

  // POST /api/auth/logout -> 204
  logout: () => http.post<void>('/auth/logout'),

  // POST /api/auth/verify-email -> 200 { message }
  verifyEmail: (body: VerifyEmailRequest) =>
    http.post<MessageResponse>('/auth/verify-email', body),

  // POST /api/auth/resend-verification -> 202 { message }
  resendVerification: (body: ResendVerificationRequest) =>
    http.post<MessageResponse>('/auth/resend-verification', body),

  // GET /api/auth/me -> 200 AuthUser
  me: (signal?: AbortSignal) => http.get<AuthUser>('/auth/me', undefined, signal),
};

// ---- Teams (§4) ----
export const teamsApi = {
  // GET /api/teams -> 200 Team[]
  list: (signal?: AbortSignal) => http.get<Team[]>('/teams', undefined, signal),

  // POST /api/teams -> 201 Team
  create: (body: CreateTeamRequest) => http.post<Team>('/teams', body),

  // PUT /api/teams/{id} -> 200 Team
  rename: (id: string, body: RenameTeamRequest) => http.put<Team>(`/teams/${id}`, body),

  // DELETE /api/teams/{id} -> 204
  remove: (id: string) => http.delete<void>(`/teams/${id}`),
};

// ---- Epics (§5) ----
export const epicsApi = {
  // GET /api/epics?teamId= -> 200 Epic[]
  list: (teamId: string, signal?: AbortSignal) =>
    http.get<Epic[]>('/epics', { teamId }, signal),

  // POST /api/epics -> 201 Epic
  create: (body: CreateEpicRequest) => http.post<Epic>('/epics', body),

  // PUT /api/epics/{id} -> 200 Epic (team immutable; any teamId in body ignored)
  update: (id: string, body: UpdateEpicRequest) => http.put<Epic>(`/epics/${id}`, body),

  // DELETE /api/epics/{id} -> 204
  remove: (id: string) => http.delete<void>(`/epics/${id}`),
};

// ---- Tickets (§6) ----
export const ticketsApi = {
  // GET /api/tickets?teamId=&type=&epicId=&search= -> 200 Board
  board: (teamId: string, filters: BoardFilters, signal?: AbortSignal) =>
    http.get<Board>(
      '/tickets',
      {
        teamId,
        type: filters.type,
        epicId: filters.epicId,
        search: filters.search,
      },
      signal,
    ),

  // GET /api/tickets/{id} -> 200 TicketDetail
  get: (id: string, signal?: AbortSignal) =>
    http.get<TicketDetail>(`/tickets/${id}`, undefined, signal),

  // POST /api/tickets -> 201 TicketDetail
  create: (body: CreateTicketRequest) => http.post<TicketDetail>('/tickets', body),

  // PUT /api/tickets/{id} -> 200 TicketDetail
  update: (id: string, body: UpdateTicketRequest) =>
    http.put<TicketDetail>(`/tickets/${id}`, body),

  // PATCH /api/tickets/{id}/state -> 200 (state + modifiedAt at minimum)
  patchState: (id: string, body: PatchTicketStateRequest) =>
    http.patch<TicketStatePatchResponse>(`/tickets/${id}/state`, body),

  // DELETE /api/tickets/{id} -> 204 (cascades comments)
  remove: (id: string) => http.delete<void>(`/tickets/${id}`),
};

// ---- Comments (§7) ----
export const commentsApi = {
  // GET /api/tickets/{id}/comments -> 200 Comment[] (oldest-first)
  list: (ticketId: string, signal?: AbortSignal) =>
    http.get<Comment[]>(`/tickets/${ticketId}/comments`, undefined, signal),

  // POST /api/tickets/{id}/comments -> 201 Comment
  create: (ticketId: string, body: CreateCommentRequest) =>
    http.post<Comment>(`/tickets/${ticketId}/comments`, body),
};
