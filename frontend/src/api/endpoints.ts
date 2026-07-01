// Typed endpoint functions — one per route in docs/API_CONTRACT.md.
// These are the ONLY place that knows API paths/methods/bodies.

import { http } from './client';
import type {
  ActivityList,
  AdminUser,
  ApiKey,
  Attachment,
  AuthUser,
  Board,
  BoardFilters,
  ChangePasswordRequest,
  Comment,
  CreateApiKeyRequest,
  CreateApiKeyResponse,
  CreateCommentRequest,
  CreateEpicRequest,
  CreateTeamRequest,
  CreateLabelRequest,
  CreateTicketRequest,
  CreateUserRequest,
  CreateUserResponse,
  CreateWebhookRequest,
  CreateWebhookResponse,
  Dashboard,
  DashboardRange,
  EditCommentRequest,
  Epic,
  Label,
  ForgotPasswordRequest,
  LoginRequest,
  LoginResponse,
  MessageResponse,
  NotificationList,
  NotificationSettings,
  PatchTicketStateRequest,
  RenameTeamRequest,
  ResendVerificationRequest,
  ResetPasswordRequest,
  ResetPasswordResponse,
  SetAssigneesRequest,
  SetLabelsRequest,
  SetNameRequest,
  SetRoleRequest,
  SetTeamsRequest,
  SignupRequest,
  Team,
  TeamMember,
  TicketDetail,
  TicketStatePatchResponse,
  UnreadCount,
  UpdateEpicRequest,
  UpdateLabelRequest,
  UpdateNotificationSettingsRequest,
  UpdateProfileRequest,
  UpdateTicketRequest,
  UpdateWebhookRequest,
  UpdateWebhookResponse,
  UpdateWipLimitsRequest,
  VerifyEmailRequest,
  WebhookDeliveryList,
  WebhookPingResponse,
  WebhookSubscription,
  Watchers,
  WatchStatus,
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

  // POST /api/auth/forgot-password -> 202 { message } (public, non-enumerating, F-01)
  forgotPassword: (body: ForgotPasswordRequest) =>
    http.post<MessageResponse>('/auth/forgot-password', body),

  // POST /api/auth/reset-password -> 200 { message } (public, single-use token, F-01)
  resetPassword: (body: ResetPasswordRequest) =>
    http.post<MessageResponse>('/auth/reset-password', body),

  // GET /api/auth/me -> 200 AuthUser
  me: (signal?: AbortSignal) => http.get<AuthUser>('/auth/me', undefined, signal),
};

// ---- Self-service account (§4.5, F-04) ----
export const meApi = {
  // PUT /api/me/profile -> 200 AuthUser (updated identity)
  updateProfile: (body: UpdateProfileRequest) => http.put<AuthUser>('/me/profile', body),

  // POST /api/me/password -> 204 (current session kept; other sessions purged)
  changePassword: (body: ChangePasswordRequest) => http.post<void>('/me/password', body),

  // GET /api/me/notification-settings -> 200 NotificationSettings (Wave 2 §6.8)
  getNotificationSettings: (signal?: AbortSignal) =>
    http.get<NotificationSettings>('/me/notification-settings', undefined, signal),

  // PUT /api/me/notification-settings -> 200 NotificationSettings
  updateNotificationSettings: (body: UpdateNotificationSettingsRequest) =>
    http.put<NotificationSettings>('/me/notification-settings', body),
};

// ---- Teams (§4) ----
export const teamsApi = {
  // GET /api/teams -> 200 Team[]
  list: (signal?: AbortSignal) => http.get<Team[]>('/teams', undefined, signal),

  // POST /api/teams -> 201 Team
  create: (body: CreateTeamRequest) => http.post<Team>('/teams', body),

  // PUT /api/teams/{id} -> 200 Team
  rename: (id: string, body: RenameTeamRequest) => http.put<Team>(`/teams/${id}`, body),

  // PUT /api/teams/{id}/wip-limits -> 200 Team (with updated wipLimits)
  setWipLimits: (id: string, body: UpdateWipLimitsRequest) =>
    http.put<Team>(`/teams/${id}/wip-limits`, body),

  // GET /api/teams/{id}/members -> 200 TeamMember[] (member-visible picker, Wave 2 §5.8)
  members: (id: string, signal?: AbortSignal) =>
    http.get<TeamMember[]>(`/teams/${id}/members`, undefined, signal),

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
  // GET /api/tickets?teamId=&type=&epicId=&search=&priority=&assigneeId=&assignedToMe=&dueFilter= -> 200 Board
  board: (teamId: string, filters: BoardFilters, signal?: AbortSignal) =>
    http.get<Board>(
      '/tickets',
      {
        teamId,
        type: filters.type,
        epicId: filters.epicId,
        search: filters.search,
        priority: filters.priority,
        // assignedToMe wins over assigneeId (documented precedence, §4.2); omit the redundant id.
        assignedToMe: filters.assignedToMe ? true : undefined,
        assigneeId: filters.assignedToMe ? undefined : filters.assigneeId,
        dueFilter: filters.dueFilter,
        labelId: filters.labelId,
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

  // PUT /api/tickets/{id}/assignees -> 200 TicketDetail (full-set replace, F-02)
  setAssignees: (id: string, body: SetAssigneesRequest) =>
    http.put<TicketDetail>(`/tickets/${id}/assignees`, body),

  // PUT /api/tickets/{id}/labels -> 200 TicketDetail (full-set replace, Wave 2 §5.7)
  setLabels: (id: string, body: SetLabelsRequest) =>
    http.put<TicketDetail>(`/tickets/${id}/labels`, body),

  // DELETE /api/tickets/{id} -> 204 (cascades comments)
  remove: (id: string) => http.delete<void>(`/tickets/${id}`),

  // GET /api/tickets/{id}/watchers -> 200 Watchers (caller flag + list, Wave 2 §5.4)
  watchers: (id: string, signal?: AbortSignal) =>
    http.get<Watchers>(`/tickets/${id}/watchers`, undefined, signal),

  // POST /api/tickets/{id}/watch -> 200 WatchStatus (idempotent)
  watch: (id: string) => http.post<WatchStatus>(`/tickets/${id}/watch`),

  // DELETE /api/tickets/{id}/watch -> 200 WatchStatus (idempotent)
  unwatch: (id: string) => http.delete<WatchStatus>(`/tickets/${id}/watch`),

  // GET /api/tickets/{id}/activity?limit=&cursor= -> 200 ActivityList (Wave 2 §5.5)
  activity: (id: string, cursor?: string, signal?: AbortSignal) =>
    http.get<ActivityList>(`/tickets/${id}/activity`, { cursor }, signal),
};

// ---- Comments (§7) ----
export const commentsApi = {
  // GET /api/tickets/{id}/comments -> 200 Comment[] (oldest-first)
  list: (ticketId: string, signal?: AbortSignal) =>
    http.get<Comment[]>(`/tickets/${ticketId}/comments`, undefined, signal),

  // POST /api/tickets/{id}/comments -> 201 Comment
  create: (ticketId: string, body: CreateCommentRequest) =>
    http.post<Comment>(`/tickets/${ticketId}/comments`, body),

  // PUT /api/comments/{id} -> 200 Comment (edit own comment; author-only, F-12)
  update: (commentId: string, body: EditCommentRequest) =>
    http.put<Comment>(`/comments/${commentId}`, body),

  // DELETE /api/comments/{id} -> 204 (author or admin, F-12)
  remove: (commentId: string) => http.delete<void>(`/comments/${commentId}`),
};

// ---- Attachments (Wave 3, §5.2, ADR-0018; M(team of ticket)) ----
export const attachmentsApi = {
  // GET /api/tickets/{id}/attachments -> 200 Attachment[] (chronological, team-read)
  list: (ticketId: string, signal?: AbortSignal) =>
    http.get<Attachment[]>(`/tickets/${ticketId}/attachments`, undefined, signal),

  // POST /api/tickets/{id}/attachments (multipart, team-write) -> 201 Attachment
  upload: (ticketId: string, file: File) => {
    const form = new FormData();
    form.append('file', file, file.name);
    return http.post<Attachment>(`/tickets/${ticketId}/attachments`, form);
  },

  // GET /api/attachments/{id} -> the blob (authenticated, forced-download; fetched with the bearer token)
  download: (id: string, signal?: AbortSignal) => http.getBlob(`/attachments/${id}`, signal),

  // DELETE /api/attachments/{id} -> 204 (team-write; removes row + blob)
  remove: (id: string) => http.delete<void>(`/attachments/${id}`),
};

// ---- Labels (Wave 2, §5.6, ADR-0016; M(team)) ----
export const labelsApi = {
  // GET /api/labels?teamId= -> 200 Label[] (a team's labels, ordered by name)
  list: (teamId: string, signal?: AbortSignal) =>
    http.get<Label[]>('/labels', { teamId }, signal),

  // POST /api/labels -> 201 Label (409 duplicate_label_name on a per-team collision)
  create: (body: CreateLabelRequest) => http.post<Label>('/labels', body),

  // PUT /api/labels/{id} -> 200 Label (rename / recolor)
  update: (id: string, body: UpdateLabelRequest) => http.put<Label>(`/labels/${id}`, body),

  // DELETE /api/labels/{id} -> 204 (disposable; removes from all tickets)
  remove: (id: string) => http.delete<void>(`/labels/${id}`),
};

// ---- Webhooks (Wave 3, §5.5, ADR-0021; M(team)) ----
export const webhooksApi = {
  // GET /api/teams/{id}/webhooks -> 200 WebhookSubscription[] (a team's subscriptions)
  list: (teamId: string, signal?: AbortSignal) =>
    http.get<WebhookSubscription[]>(`/teams/${teamId}/webhooks`, undefined, signal),

  // POST /api/teams/{id}/webhooks -> 201 { subscription, secret } (secret shown once)
  create: (teamId: string, body: CreateWebhookRequest) =>
    http.post<CreateWebhookResponse>(`/teams/${teamId}/webhooks`, body),

  // PUT /api/webhooks/{id} -> 200 { subscription, secret? } (secret only when rotated)
  update: (id: string, body: UpdateWebhookRequest) =>
    http.put<UpdateWebhookResponse>(`/webhooks/${id}`, body),

  // DELETE /api/webhooks/{id} -> 204 (cascades deliveries)
  remove: (id: string) => http.delete<void>(`/webhooks/${id}`),

  // GET /api/webhooks/{id}/deliveries?limit=&cursor= -> 200 WebhookDeliveryList (audit, newest-first)
  deliveries: (id: string, cursor?: string, signal?: AbortSignal) =>
    http.get<WebhookDeliveryList>(`/webhooks/${id}/deliveries`, { cursor }, signal),

  // POST /api/webhooks/{id}/ping -> 202 { deliveryId } (enqueue a test delivery)
  ping: (id: string) => http.post<WebhookPingResponse>(`/webhooks/${id}/ping`),
};

// ---- API keys (Wave 3, §5.6, ADR-0021; Self) ----
export const apiKeysApi = {
  // GET /api/me/api-keys -> 200 ApiKey[] (the caller's keys, newest-first)
  list: (signal?: AbortSignal) => http.get<ApiKey[]>('/me/api-keys', undefined, signal),

  // POST /api/me/api-keys -> 201 { key, secret } (raw ptk_ key shown once)
  create: (body: CreateApiKeyRequest) => http.post<CreateApiKeyResponse>('/me/api-keys', body),

  // DELETE /api/me/api-keys/{id} -> 204 (revoke; idempotent)
  revoke: (id: string) => http.delete<void>(`/me/api-keys/${id}`),
};

// ---- Analytics (Wave 3, §5.4, ADR-0020; M(team)) ----
export const analyticsApi = {
  // GET /api/analytics/dashboard?teamId=&from=&to= -> 200 Dashboard (composite, read-only, team-scoped)
  dashboard: (teamId: string, range: DashboardRange, signal?: AbortSignal) =>
    http.get<Dashboard>(
      '/analytics/dashboard',
      { teamId, from: range.from, to: range.to },
      signal,
    ),
};

// ---- Notifications (Wave 2, §8, Self) ----
export const notificationsApi = {
  // GET /api/notifications?limit=&cursor= -> 200 NotificationList (newest-first, keyset-paged)
  list: (cursor?: string, limit?: number, signal?: AbortSignal) =>
    http.get<NotificationList>('/notifications', { cursor, limit }, signal),

  // GET /api/notifications/unread-count -> 200 { unreadCount } (cheap poll target)
  unreadCount: (signal?: AbortSignal) =>
    http.get<UnreadCount>('/notifications/unread-count', undefined, signal),

  // POST /api/notifications/{id}/read -> 200 { unreadCount } (idempotent; another user's id -> 404)
  markRead: (id: string) => http.post<UnreadCount>(`/notifications/${id}/read`),

  // POST /api/notifications/read-all -> 200 { unreadCount: 0 }
  markAllRead: () => http.post<UnreadCount>('/notifications/read-all'),
};

// ---- Admin — User Management (§8, admin-only) ----
export const adminUsersApi = {
  // GET /api/admin/users -> 200 AdminUser[]
  list: (signal?: AbortSignal) => http.get<AdminUser[]>('/admin/users', undefined, signal),

  // POST /api/admin/users -> 201 { user, generatedPassword? }
  create: (body: CreateUserRequest) => http.post<CreateUserResponse>('/admin/users', body),

  // PUT /api/admin/users/{id}/role -> 200 AdminUser
  setRole: (id: string, body: SetRoleRequest) =>
    http.put<AdminUser>(`/admin/users/${id}/role`, body),

  // PUT /api/admin/users/{id}/name -> 200 AdminUser
  setName: (id: string, body: SetNameRequest) =>
    http.put<AdminUser>(`/admin/users/${id}/name`, body),

  // PUT /api/admin/users/{id}/teams -> 200 AdminUser
  setTeams: (id: string, body: SetTeamsRequest) =>
    http.put<AdminUser>(`/admin/users/${id}/teams`, body),

  // POST /api/admin/users/{id}/block -> 200 AdminUser
  block: (id: string) => http.post<AdminUser>(`/admin/users/${id}/block`),

  // POST /api/admin/users/{id}/unblock -> 200 AdminUser
  unblock: (id: string) => http.post<AdminUser>(`/admin/users/${id}/unblock`),

  // POST /api/admin/users/{id}/reset-password -> 200 { generatedPassword }
  resetPassword: (id: string) =>
    http.post<ResetPasswordResponse>(`/admin/users/${id}/reset-password`),
};
