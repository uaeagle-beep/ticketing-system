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

// Priority dictionary (F-03, ADR-0009), ascending severity. Canonical lowercase; default 'medium'.
export const TICKET_PRIORITIES = ['low', 'medium', 'high', 'urgent'] as const;
export type TicketPriority = (typeof TICKET_PRIORITIES)[number];

// Board "due" filter values (F-08). A single enum param keeps mutually-exclusive states unambiguous.
export const DUE_FILTERS = ['overdue', 'has_due_date', 'no_due_date'] as const;
export type DueFilter = (typeof DUE_FILTERS)[number];

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
  // Labels (Wave 2, ADR-0016).
  | 'duplicate_label_name'
  // Attachments (Wave 3, ADR-0018).
  | 'payload_too_large'
  | 'unsupported_media_type'
  // Webhooks + API keys (Wave 3, ADR-0021).
  | 'insufficient_scope'
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
  // Wave 3 i18n (§5.7, ADR-0022): persisted preferred UI language ('uk'|'en'), or null when unset.
  // The SPA reads this on bootstrap to set the active language across devices (localStorage wins).
  locale?: string | null;
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

// A lightweight assignee reference (id + display name) on tickets (F-02, API_CONTRACT §4.2).
// displayName is computed server-side (name?.trim() || email); the SPA never recomputes it.
export interface AssigneeRef {
  id: string;
  displayName: string;
}

// ---- Labels (Wave 2, §5.6/§5.7, ADR-0016) ----
// A full label as returned by the label CRUD endpoints.
export interface Label {
  id: string;
  teamId: string;
  name: string;
  color: string; // "#rrggbb"
}

// A lightweight label reference (id + name + color) carried on tickets for the chip (§8.5).
export interface LabelRef {
  id: string;
  name: string;
  color: string;
}

// ---- Tickets (API_CONTRACT §6) ----
export interface TicketDetail {
  id: string;
  teamId: string;
  epicId: string | null;
  epicTitle: string | null;
  type: TicketType;
  state: TicketState;
  // Priority (F-03). Always present; defaults to 'medium'.
  priority: TicketPriority;
  title: string;
  body: string;
  // Optional calendar-day due date "YYYY-MM-DD" (F-08); null => no due date.
  dueDate: string | null;
  // Backend-computed: dueDate != null && dueDate < today(UTC) && state != done (F-08).
  isOverdue: boolean;
  // Assignees (F-02). Empty array => unassigned.
  assignees: AssigneeRef[];
  createdAt: string;
  modifiedAt: string;
  createdBy: string;
  createdByEmail: string;
  // Creator's optional display name (Feature 1). null => the UI shows createdByEmail.
  createdByName: string | null;
  // Wave 2 (§6.7): whether the current user watches this ticket. Drives the detail watch toggle.
  isWatching: boolean;
  // Wave 2 (§8.5, ADR-0016): the ticket's labels (id + name + color) for chips.
  labels: LabelRef[];
}

// Card payload as returned inside the board columns (subset of the detail).
export interface TicketCard {
  id: string;
  type: TicketType;
  state: TicketState;
  priority: TicketPriority;
  title: string;
  epicId: string | null;
  epicTitle: string | null;
  dueDate: string | null;
  isOverdue: boolean;
  assignees: AssigneeRef[];
  modifiedAt: string;
  // Wave 2 (§8.5, ADR-0016): the card's labels.
  labels: LabelRef[];
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
  // F-12 (Wave 2, §5.2): true once the body has been edited; editedAt is the edit time (null otherwise).
  edited: boolean;
  editedAt: string | null;
}

// ---- Team members (Wave 2, §5.8 / ADR-0017) ----
// GET /api/teams/{id}/members — the member-visible picker. displayName is computed server-side
// (name?.trim() || email). isAdmin is the user's global admin flag.
export interface TeamMember {
  id: string;
  displayName: string;
  isAdmin: boolean;
}

// ---- Notifications subsystem (Wave 2, §8, ADR-0013) ----

// Canonical application-event codes (WAVE2 §6.1). Also used by activity entries.
export const EVENT_TYPES = [
  'ticket_created',
  'ticket_field_changed',
  'ticket_moved',
  'ticket_assignees_changed',
  'comment_added',
  'comment_edited',
  'comment_deleted',
  'ticket_deleted',
] as const;
export type EventType = (typeof EVENT_TYPES)[number] | (string & {});

// One in-app notification. readAt === null => unread. ticketId === null => deleted-ticket tombstone
// (non-navigable in the SPA, §6.6).
export interface Notification {
  id: string;
  eventType: EventType;
  summary: string;
  ticketId: string | null;
  commentId: string | null;
  actorId: string;
  actorDisplayName: string;
  createdAt: string; // ISO-8601 UTC
  readAt: string | null;
}

// A page of notifications newest-first + the caller's unread count (so the bell updates from the
// same call). Keyset pagination via an opaque cursor.
export interface NotificationList {
  items: Notification[];
  unreadCount: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface UnreadCount {
  unreadCount: number;
}

// GET/PUT /api/me/notification-settings — the email toggle (§6.8).
export interface NotificationSettings {
  emailNotificationsEnabled: boolean;
}

export interface UpdateNotificationSettingsRequest {
  emailNotificationsEnabled: boolean;
}

// ---- Activity timeline (Wave 2, §5.5, ADR-0012) ----
export interface ActivityEntry {
  id: string;
  eventType: EventType;
  summary: string;
  actorId: string;
  actorDisplayName: string;
  createdAt: string; // ISO-8601 UTC
}

export interface ActivityList {
  items: ActivityEntry[];
  hasMore: boolean;
  nextCursor: string | null;
}

// ---- Watchers (Wave 2, §5.4, ADR-0013) ----
export interface WatcherRef {
  id: string;
  displayName: string;
}

// Response to POST/DELETE /api/tickets/{id}/watch.
export interface WatchStatus {
  watching: boolean;
}

// Response to GET /api/tickets/{id}/watchers.
export interface Watchers {
  watching: boolean;
  watchers: WatcherRef[];
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

// Self-service password reset (F-01) and profile (F-04).
export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  token: string;
  password: string;
}

export interface UpdateProfileRequest {
  // null/blank => clears the display name (UI shows the email). NOTE: the backend re-derives the
  // name from this field on every call, so a caller that only wants to change the locale MUST still
  // pass the current `name` (else it would be cleared). The language switcher does exactly that.
  name: string | null;
  // Wave 3 i18n (§5.7, ADR-0022): optional preferred language ('uk'|'en'), or null to clear.
  locale?: string | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
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
  // Priority (F-03): optional; server defaults to 'medium' when omitted.
  priority?: TicketPriority;
  // Due date (F-08): "YYYY-MM-DD" or null/omitted for no due date.
  dueDate?: string | null;
  // Initial assignee set (F-02): omitted => none.
  assigneeIds?: string[];
}

export interface UpdateTicketRequest {
  teamId: string;
  type: TicketType;
  epicId: string | null;
  title: string;
  body: string;
  state: TicketState;
  // Priority (F-03): REQUIRED in the edit body (like type/state).
  priority: TicketPriority;
  // Due date (F-08): "YYYY-MM-DD" or null to clear.
  dueDate: string | null;
  // assigneeIds omitted on the main edit; assignment goes through PUT /tickets/{id}/assignees.
}

export interface PatchTicketStateRequest {
  state: TicketState;
}

// PUT /api/tickets/{id}/assignees — authoritative full assignee set (F-02, §4.2).
export interface SetAssigneesRequest {
  userIds: string[];
}

// ---- Label management (Wave 2, §5.6, ADR-0016) ----
export interface CreateLabelRequest {
  teamId: string;
  name: string;
  color: string; // "#rrggbb"
}

export interface UpdateLabelRequest {
  name: string;
  color: string;
}

// PUT /api/tickets/{id}/labels — authoritative full label set (§5.7).
export interface SetLabelsRequest {
  labelIds: string[];
}

export interface CreateCommentRequest {
  body: string;
}

// PUT /api/comments/{id} — edit own comment (F-12, §5.2).
export interface EditCommentRequest {
  body: string;
}

// ---- Attachments (Wave 3, §5.2, ADR-0018) ----
// Metadata only; the on-disk storage key is server-internal and never returned.
export interface Attachment {
  id: string;
  ticketId: string;
  filename: string;
  contentType: string;
  sizeBytes: number;
  uploadedBy: string;
  uploadedByDisplayName: string;
  createdAt: string;
}

// ---- Webhooks (Wave 3, §5.5, ADR-0021; M(team)) ----

// A webhook subscription. eventTypes is the list of subscribed canonical codes, or ['*'] for all.
// The signing secret is NEVER returned on read — only once on create/rotate (see CreateWebhookResponse).
export interface WebhookSubscription {
  id: string;
  teamId: string;
  url: string;
  eventTypes: string[];
  active: boolean;
  createdAt: string;
  modifiedAt: string;
}

export interface CreateWebhookRequest {
  url: string;
  eventTypes: string[];
  active?: boolean;
}

export interface UpdateWebhookRequest {
  url?: string;
  eventTypes?: string[];
  active?: boolean;
  rotateSecret?: boolean;
}

// Create/rotate reveal the signing secret ONCE.
export interface CreateWebhookResponse {
  subscription: WebhookSubscription;
  secret: string;
}

export interface UpdateWebhookResponse {
  subscription: WebhookSubscription;
  secret: string | null;
}

// A delivery audit row (excludes the payload body by default).
export interface WebhookDelivery {
  id: string;
  eventType: string;
  status: 'pending' | 'delivered' | 'failed' | (string & {});
  attempts: number;
  lastStatusCode: number | null;
  lastError: string | null;
  createdAt: string;
  deliveredAt: string | null;
}

export interface WebhookDeliveryList {
  items: WebhookDelivery[];
  hasMore: boolean;
  nextCursor: string | null;
}

export interface WebhookPingResponse {
  deliveryId: string;
}

// The subscribable event types offered in the create UI (the canonical set + wildcard). Kept here so the
// UI checkbox list and the API stay in sync (the backend validates each against EventType or '*').
export const WEBHOOK_EVENT_TYPES = [
  'ticket_created',
  'ticket_field_changed',
  'ticket_moved',
  'ticket_assignees_changed',
  'comment_added',
  'comment_edited',
  'comment_deleted',
  'ticket_deleted',
  'attachment_added',
  'attachment_deleted',
] as const;

// ---- API keys (Wave 3, §5.6, ADR-0021; Self) ----

// Coarse scopes an API key can carry (write implies read). Canonical wire codes.
export const API_KEY_SCOPES = ['tickets:read', 'tickets:write'] as const;
export type ApiKeyScope = (typeof API_KEY_SCOPES)[number];

// An API key as returned by list/create — never the raw key or hash.
export interface ApiKey {
  id: string;
  name: string;
  prefix: string;
  scopes: string[];
  createdAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
}

export interface CreateApiKeyRequest {
  name: string;
  scopes: string[];
}

// Create reveals the raw ptk_ key ONCE.
export interface CreateApiKeyResponse {
  key: ApiKey;
  secret: string;
}

// ---- Analytics dashboard (Wave 3, §5.4, ADR-0020) ----
// One composite, read-only, team-scoped payload of the ~nine Kanban-health metrics, aggregated
// server-side (pre-aggregated counts/buckets — the SPA plots a small fixed number of points). Enum
// keys (state/priority/type) are the canonical lowercase codes used everywhere else.

export interface LabelCount {
  labelId: string;
  name: string;
  color: string;
  count: number;
}

export interface OpenVsDone {
  open: number;
  done: number;
}

export interface ThroughputBucket {
  weekStart: string; // ISO date (YYYY-MM-DD), Monday of the ISO week
  doneCount: number;
}

export interface CycleTime {
  avgDays: number | null;
  medianDays: number | null;
  sampleSize: number;
}

export interface WipState {
  state: TicketState;
  count: number;
  limit: number | null;
  overLimit: boolean;
}

export interface Dashboard {
  teamId: string;
  from: string; // ISO date (YYYY-MM-DD)
  to: string; // ISO date (YYYY-MM-DD)
  byState: Record<TicketState, number>;
  byPriority: Record<TicketPriority, number>;
  byType: Record<TicketType, number>;
  byLabel: LabelCount[];
  openVsDone: OpenVsDone;
  throughput: ThroughputBucket[];
  cycleTime: CycleTime;
  overdueCount: number;
  wip: WipState[];
}

// Optional UTC calendar-day range for the dashboard query (both YYYY-MM-DD; omit for the default 12 weeks).
export interface DashboardRange {
  from?: string;
  to?: string;
}

// ---- Board filters (client-facing) ----
export interface BoardFilters {
  type?: TicketType;
  epicId?: string;
  search?: string;
  // Wave 1 filters (all AND-combined server-side).
  priority?: TicketPriority;
  assigneeId?: string; // filter to a specific assignee
  assignedToMe?: boolean; // sugar for the current user; wins over assigneeId if both set
  dueFilter?: DueFilter; // overdue | has_due_date | no_due_date
  // Wave 2 (§8.4): filter to tickets carrying a specific label (AND-combined with the others).
  labelId?: string;
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
