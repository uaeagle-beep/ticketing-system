// MSW request handlers for the Ticket Tracker API.
//
// These mirror docs/API_CONTRACT.md and src/api/types.ts exactly: paths under
// `/api/*`, the uniform error envelope ({ error: { code, message, errors? } }),
// canonical lowercase enums, and the documented status codes. The client
// (src/api/client.ts) hits relative `/api` paths; MSW intercepts at the fetch
// layer in jsdom so no backend is required.
//
// Individual tests override specific routes with `server.use(...)` to exercise
// error paths (401/403/409/validation) without touching these defaults.

import { http, HttpResponse } from 'msw';
import type {
  ActivityList,
  AdminUser,
  ApiKey,
  Attachment,
  AuthUser,
  Board,
  Comment,
  CreateApiKeyResponse,
  CreateWebhookResponse,
  Dashboard,
  Epic,
  Label,
  LoginResponse,
  MessageResponse,
  NotificationList,
  NotificationSettings,
  Team,
  TeamMember,
  TicketDetail,
  UnreadCount,
  UpdateWebhookResponse,
  Watchers,
  WatchStatus,
  WebhookDeliveryList,
  WebhookSubscription,
} from '@/api/types';

// Same base path the real client uses (API_BASE = '/api').
export const API = '/api';

// ---- Canonical sample data (contract-shaped) -------------------------------

// Default principal is an ADMIN (mirrors the backend default test principal, design §6.3) so the
// existing happy-path UI tests retain full access. Tests that need a member override /auth/me.
export const sampleUser: AuthUser = {
  id: '8e29c1b4-0000-4000-8000-000000000001',
  email: 'alex@dataart.com',
  name: null,
  emailVerified: true,
  isAdmin: true,
  isBlocked: false,
  teams: [],
  // Wave 3 i18n (§5.7): unset by default so the SPA falls back to localStorage/uk in the app
  // (tests pin i18n to 'en' via initI18nForTest regardless of this value).
  locale: null,
};

export const sampleLogin: LoginResponse = {
  token: '9f2b-test-opaque-token',
  user: sampleUser,
  expiresAt: '2026-07-03T11:26:00Z',
};

export const sampleTeam: Team = {
  id: 'f1c2-team-platform',
  name: 'Platform',
  ticketCount: 12,
  epicCount: 3,
  createdAt: '2026-06-20T08:00:00Z',
  modifiedAt: '2026-06-22T10:15:00Z',
  wipLimits: {
    new: null,
    ready_for_implementation: 5,
    in_progress: 3,
    ready_for_acceptance: null,
    done: null,
  },
};

export const sampleEpic: Epic = {
  id: 'ep01-billing-revamp',
  teamId: sampleTeam.id,
  title: 'Billing Revamp',
  description: 'Optional text or null',
  ticketCount: 5,
  createdAt: '2026-06-20T09:00:00Z',
  modifiedAt: '2026-06-23T12:00:00Z',
};

export const sampleTicketDetail: TicketDetail = {
  id: 'tk1042-login-fails',
  teamId: sampleTeam.id,
  epicId: sampleEpic.id,
  epicTitle: sampleEpic.title,
  type: 'bug',
  state: 'in_progress',
  priority: 'high',
  title: 'Login fails',
  body: 'Steps to reproduce...',
  dueDate: '2026-07-05',
  isOverdue: false,
  assignees: [{ id: sampleUser.id, displayName: sampleUser.email }],
  createdAt: '2026-06-22T09:15:00Z',
  modifiedAt: '2026-06-23T12:40:00Z',
  createdBy: sampleUser.id,
  createdByEmail: sampleUser.email,
  createdByName: null,
  isWatching: false,
  labels: [],
};

// Team labels for the picker / management surface / board filter (Wave 2, §5.6, ADR-0016).
export const sampleLabels: Label[] = [
  { id: 'lb01-backend', teamId: sampleTeam.id, name: 'Backend', color: '#3b82f6' },
  { id: 'lb02-urgent', teamId: sampleTeam.id, name: 'Urgent', color: '#ef4444' },
];

// Webhook subscription + delivery fixtures (Wave 3, §5.5, ADR-0021).
export const sampleWebhook: WebhookSubscription = {
  id: 'wh01-ci-hook',
  teamId: sampleTeam.id,
  url: 'https://example.com/hooks/tickets',
  eventTypes: ['ticket_moved', 'comment_added'],
  active: true,
  createdAt: '2026-06-24T09:00:00Z',
  modifiedAt: '2026-06-24T09:00:00Z',
};

export const sampleWebhookDeliveries: WebhookDeliveryList = {
  items: [
    {
      id: 'wd01-delivered',
      eventType: 'ticket_moved',
      status: 'delivered',
      attempts: 1,
      lastStatusCode: 200,
      lastError: null,
      createdAt: '2026-06-24T09:05:00Z',
      deliveredAt: '2026-06-24T09:05:01Z',
    },
    {
      id: 'wd02-failed',
      eventType: 'comment_added',
      status: 'failed',
      attempts: 5,
      lastStatusCode: 500,
      lastError: 'HTTP 500',
      createdAt: '2026-06-24T09:06:00Z',
      deliveredAt: null,
    },
  ],
  hasMore: false,
  nextCursor: null,
};

// API-key fixture (Wave 3, §5.6, ADR-0021).
export const sampleApiKey: ApiKey = {
  id: 'ak01-ci',
  name: 'CI pipeline',
  prefix: 'ptk_ab12cd34',
  scopes: ['tickets:read', 'tickets:write'],
  createdAt: '2026-06-24T10:00:00Z',
  lastUsedAt: null,
  revokedAt: null,
};

// Analytics dashboard fixture (Wave 3, §5.4, ADR-0020). Contract-shaped, pre-aggregated counts/buckets.
export const sampleDashboard: Dashboard = {
  teamId: sampleTeam.id,
  from: '2026-04-08',
  to: '2026-07-01',
  byState: {
    new: 10,
    ready_for_implementation: 6,
    in_progress: 8,
    ready_for_acceptance: 5,
    done: 8,
  },
  byPriority: { low: 4, medium: 20, high: 10, urgent: 3 },
  byType: { bug: 12, feature: 20, fix: 5 },
  byLabel: [{ labelId: 'lb01-backend', name: 'Backend', color: '#3b82f6', count: 9 }],
  openVsDone: { open: 29, done: 8 },
  throughput: [
    { weekStart: '2026-06-15', doneCount: 3 },
    { weekStart: '2026-06-22', doneCount: 4 },
  ],
  cycleTime: { avgDays: 6.4, medianDays: 5.0, sampleSize: 8 },
  overdueCount: 3,
  wip: [
    { state: 'new', count: 10, limit: null, overLimit: false },
    { state: 'ready_for_implementation', count: 6, limit: 5, overLimit: true },
    { state: 'in_progress', count: 8, limit: 3, overLimit: true },
    { state: 'ready_for_acceptance', count: 5, limit: null, overLimit: false },
    { state: 'done', count: 8, limit: null, overLimit: false },
  ],
};

// An empty-team dashboard (all-zero) for the empty-state test.
export const emptyDashboard: Dashboard = {
  teamId: sampleTeam.id,
  from: '2026-04-08',
  to: '2026-07-01',
  byState: {
    new: 0,
    ready_for_implementation: 0,
    in_progress: 0,
    ready_for_acceptance: 0,
    done: 0,
  },
  byPriority: { low: 0, medium: 0, high: 0, urgent: 0 },
  byType: { bug: 0, feature: 0, fix: 0 },
  byLabel: [],
  openVsDone: { open: 0, done: 0 },
  throughput: [],
  cycleTime: { avgDays: null, medianDays: null, sampleSize: 0 },
  overdueCount: 0,
  wip: [
    { state: 'new', count: 0, limit: null, overLimit: false },
    { state: 'ready_for_implementation', count: 0, limit: null, overLimit: false },
    { state: 'in_progress', count: 0, limit: null, overLimit: false },
    { state: 'ready_for_acceptance', count: 0, limit: null, overLimit: false },
    { state: 'done', count: 0, limit: null, overLimit: false },
  ],
};

export const sampleComment: Comment = {
  id: 'cm01-looks-fixed',
  ticketId: sampleTicketDetail.id,
  authorId: sampleUser.id,
  authorEmail: sampleUser.email,
  authorName: null,
  body: 'Looks fixed.',
  createdAt: '2026-06-23T13:00:00Z',
  edited: false,
  editedAt: null,
};

// Attachment fixture (Wave 3, §5.2, ADR-0018).
export const sampleAttachment: Attachment = {
  id: 'at01-screenshot',
  ticketId: sampleTicketDetail.id,
  filename: 'screenshot.png',
  contentType: 'image/png',
  sizeBytes: 20480,
  uploadedBy: sampleUser.id,
  uploadedByDisplayName: sampleUser.email,
  createdAt: '2026-06-23T13:30:00Z',
};

// Team members for the member-visible picker (Wave 2 §5.8 / ADR-0017).
export const sampleTeamMembers: TeamMember[] = [
  { id: sampleUser.id, displayName: sampleUser.email, isAdmin: true },
  { id: '8e29c1b4-0000-4000-8000-000000000002', displayName: 'dev@dataart.com', isAdmin: false },
];

// ---- Wave 2 notifications subsystem fixtures (§8) --------------------------

export const sampleNotificationList: NotificationList = {
  items: [
    {
      id: 'nt01-moved',
      eventType: 'ticket_moved',
      summary: 'Alex Doe moved this from New to In progress',
      ticketId: sampleTicketDetail.id,
      commentId: null,
      actorId: '8e29c1b4-0000-4000-8000-000000000002',
      actorDisplayName: 'Alex Doe',
      createdAt: '2026-06-23T14:00:00Z',
      readAt: null, // unread
    },
    {
      id: 'nt02-tombstone',
      eventType: 'ticket_deleted',
      summary: "Alex Doe deleted ticket 'Old bug'",
      ticketId: null, // deleted-ticket tombstone (non-navigable, §6.6)
      commentId: null,
      actorId: '8e29c1b4-0000-4000-8000-000000000002',
      actorDisplayName: 'Alex Doe',
      createdAt: '2026-06-23T13:00:00Z',
      readAt: '2026-06-23T13:10:00Z', // read
    },
  ],
  unreadCount: 1,
  hasMore: false,
  nextCursor: null,
};

export const sampleActivityList: ActivityList = {
  items: [
    {
      id: 'ac01-moved',
      eventType: 'ticket_moved',
      summary: 'Alex Doe moved this from New to In progress',
      actorId: sampleUser.id,
      actorDisplayName: sampleUser.email,
      createdAt: '2026-06-23T14:00:00Z',
    },
    {
      id: 'ac00-created',
      eventType: 'ticket_created',
      summary: 'Alex Doe created this ticket',
      actorId: sampleUser.id,
      actorDisplayName: sampleUser.email,
      createdAt: '2026-06-22T09:15:00Z',
    },
  ],
  hasMore: false,
  nextCursor: null,
};

export const sampleWatchers: Watchers = {
  watching: false,
  watchers: [{ id: sampleUser.id, displayName: sampleUser.email }],
};

// ---- Admin user-management sample data (API_CONTRACT §8) --------------------

export const sampleAdminUser: AdminUser = {
  id: sampleUser.id,
  email: sampleUser.email,
  name: null,
  isAdmin: true,
  isBlocked: false,
  emailVerified: true,
  status: 'active',
  createdAt: '2026-06-20T08:00:00Z',
  teams: [],
};

export const sampleMemberUser: AdminUser = {
  id: '8e29c1b4-0000-4000-8000-000000000002',
  email: 'dev@dataart.com',
  name: null,
  isAdmin: false,
  isBlocked: false,
  emailVerified: true,
  status: 'active',
  createdAt: '2026-06-21T09:00:00Z',
  teams: [{ id: sampleTeam.id, name: sampleTeam.name }],
};

// A board with all five columns in workflow order (FR-E6-2), one card in `new`.
export function makeBoard(overrides: Partial<Board> = {}): Board {
  return {
    teamId: sampleTeam.id,
    total: 1,
    columns: [
      {
        state: 'new',
        count: 1,
        total: 1,
        wipLimit: null,
        tickets: [
          {
            id: sampleTicketDetail.id,
            type: 'bug',
            state: 'new',
            priority: 'high',
            title: 'Login fails',
            epicId: sampleEpic.id,
            epicTitle: sampleEpic.title,
            dueDate: '2026-07-05',
            isOverdue: false,
            assignees: [{ id: sampleUser.id, displayName: sampleUser.email }],
            modifiedAt: '2026-06-23T12:40:00Z',
            labels: [],
          },
        ],
      },
      { state: 'ready_for_implementation', count: 0, total: 0, wipLimit: null, tickets: [] },
      { state: 'in_progress', count: 0, total: 0, wipLimit: null, tickets: [] },
      { state: 'ready_for_acceptance', count: 0, total: 0, wipLimit: null, tickets: [] },
      { state: 'done', count: 0, total: 0, wipLimit: null, tickets: [] },
    ],
    ...overrides,
  };
}

// ---- Error-envelope helper (API_CONTRACT §2) -------------------------------

export function errorEnvelope(
  status: number,
  code: string,
  message: string,
  errors?: Record<string, string[]>,
) {
  return HttpResponse.json({ error: { code, message, errors } }, { status });
}

const ok = <T>(body: T, status = 200) => HttpResponse.json(body, { status });

// ---- Default happy-path handlers -------------------------------------------

export const handlers = [
  // Auth (§3)
  http.post(`${API}/auth/signup`, () =>
    ok<MessageResponse>(
      {
        message:
          'Account created. Please check your email to verify your account before logging in.',
      },
      201,
    ),
  ),

  http.post(`${API}/auth/login`, () => ok<LoginResponse>(sampleLogin, 200)),

  http.post(`${API}/auth/logout`, () => new HttpResponse(null, { status: 204 })),

  http.post(`${API}/auth/verify-email`, () =>
    ok<MessageResponse>({ message: 'Email verified — your account is ready to use.' }, 200),
  ),

  http.post(`${API}/auth/resend-verification`, () =>
    ok<MessageResponse>(
      { message: 'If an account needs verification, a new email has been sent.' },
      202,
    ),
  ),

  http.post(`${API}/auth/forgot-password`, () =>
    ok<MessageResponse>(
      { message: 'If an account exists for that address, a password reset link has been sent.' },
      202,
    ),
  ),

  http.post(`${API}/auth/reset-password`, () =>
    ok<MessageResponse>(
      { message: 'Your password has been reset. Please log in with your new password.' },
      200,
    ),
  ),

  http.get(`${API}/auth/me`, () => ok<AuthUser>(sampleUser, 200)),

  // Self-service account (§4.5, F-04)
  http.put(`${API}/me/profile`, async ({ request }) => {
    const body = (await request.json()) as { name: string | null; locale?: string | null };
    return ok<AuthUser>(
      { ...sampleUser, name: body.name, locale: body.locale ?? sampleUser.locale },
      200,
    );
  }),
  http.post(`${API}/me/password`, () => new HttpResponse(null, { status: 204 })),

  // Notification settings (Wave 2 §6.8)
  http.get(`${API}/me/notification-settings`, () =>
    ok<NotificationSettings>({ emailNotificationsEnabled: true }, 200),
  ),
  http.put(`${API}/me/notification-settings`, async ({ request }) => {
    const b = (await request.json()) as { emailNotificationsEnabled: boolean };
    return ok<NotificationSettings>({ emailNotificationsEnabled: b.emailNotificationsEnabled }, 200);
  }),

  // Teams (§4)
  http.get(`${API}/teams`, () => ok<Team[]>([sampleTeam], 200)),
  http.get(`${API}/teams/:id/members`, () => ok<TeamMember[]>(sampleTeamMembers, 200)),
  http.post(`${API}/teams`, () => ok<Team>(sampleTeam, 201)),
  http.put(`${API}/teams/:id`, () => ok<Team>(sampleTeam, 200)),
  http.delete(`${API}/teams/:id`, () => new HttpResponse(null, { status: 204 })),

  // Epics (§5)
  http.get(`${API}/epics`, () => ok<Epic[]>([sampleEpic], 200)),
  http.post(`${API}/epics`, () => ok<Epic>(sampleEpic, 201)),
  http.put(`${API}/epics/:id`, () => ok<Epic>(sampleEpic, 200)),
  http.delete(`${API}/epics/:id`, () => new HttpResponse(null, { status: 204 })),

  // Tickets (§6)
  http.get(`${API}/tickets`, () => ok<Board>(makeBoard(), 200)),
  http.get(`${API}/tickets/:id`, () => ok<TicketDetail>(sampleTicketDetail, 200)),
  http.post(`${API}/tickets`, () => ok<TicketDetail>(sampleTicketDetail, 201)),
  http.put(`${API}/tickets/:id`, () => ok<TicketDetail>(sampleTicketDetail, 200)),
  http.patch(`${API}/tickets/:id/state`, async ({ request }) => {
    const body = (await request.json()) as { state: TicketDetail['state'] };
    return ok(
      { id: sampleTicketDetail.id, state: body.state, modifiedAt: '2026-06-24T08:00:00Z' },
      200,
    );
  }),
  http.put(`${API}/tickets/:id/assignees`, async ({ request }) => {
    const body = (await request.json()) as { userIds: string[] };
    // Echo the requested set back as the ticket's assignees (display name = email in the fixture).
    const assignees = body.userIds.map((uid) => ({
      id: uid,
      displayName: uid === sampleUser.id ? sampleUser.email : uid,
    }));
    return ok<TicketDetail>({ ...sampleTicketDetail, assignees }, 200);
  }),
  // PUT /api/tickets/{id}/labels -> 200 TicketDetail (full-set replace, Wave 2 §5.7): echo the set back.
  http.put(`${API}/tickets/:id/labels`, async ({ request }) => {
    const body = (await request.json()) as { labelIds: string[] };
    const labels = body.labelIds.map(
      (lid) => sampleLabels.find((l) => l.id === lid) ?? { id: lid, name: lid, color: '#64748b' },
    );
    return ok<TicketDetail>({ ...sampleTicketDetail, labels }, 200);
  }),
  http.delete(`${API}/tickets/:id`, () => new HttpResponse(null, { status: 204 })),

  // Watchers + activity (Wave 2 §5.4/§5.5)
  http.get(`${API}/tickets/:id/watchers`, () => ok<Watchers>(sampleWatchers, 200)),
  http.post(`${API}/tickets/:id/watch`, () => ok<WatchStatus>({ watching: true }, 200)),
  http.delete(`${API}/tickets/:id/watch`, () => ok<WatchStatus>({ watching: false }, 200)),
  http.get(`${API}/tickets/:id/activity`, () => ok<ActivityList>(sampleActivityList, 200)),

  // Comments (§7)
  http.get(`${API}/tickets/:id/comments`, () => ok<Comment[]>([sampleComment], 200)),
  http.post(`${API}/tickets/:id/comments`, () => ok<Comment>(sampleComment, 201)),
  // PUT /api/comments/{id} -> 200 Comment (edit own, F-12): echo the new body + edited flag.
  http.put(`${API}/comments/:id`, async ({ request, params }) => {
    const b = (await request.json()) as { body: string };
    return ok<Comment>(
      {
        ...sampleComment,
        id: String(params.id),
        body: b.body,
        edited: true,
        editedAt: '2026-06-23T13:05:00Z',
      },
      200,
    );
  }),
  // DELETE /api/comments/{id} -> 204 (author or admin, F-12).
  http.delete(`${API}/comments/:id`, () => new HttpResponse(null, { status: 204 })),

  // Attachments (Wave 3 §5.2, ADR-0018)
  http.get(`${API}/tickets/:id/attachments`, () => ok<Attachment[]>([sampleAttachment], 200)),
  http.post(`${API}/tickets/:id/attachments`, ({ params }) =>
    ok<Attachment>({ ...sampleAttachment, id: 'at-new', ticketId: String(params.id) }, 201),
  ),
  http.get(`${API}/attachments/:id`, () =>
    HttpResponse.arrayBuffer(new Uint8Array([0x89, 0x50, 0x4e, 0x47]).buffer, {
      status: 200,
      headers: {
        'Content-Type': 'image/png',
        'Content-Disposition': 'attachment; filename="screenshot.png"',
        'X-Content-Type-Options': 'nosniff',
      },
    }),
  ),
  http.delete(`${API}/attachments/:id`, () => new HttpResponse(null, { status: 204 })),

  // Labels (Wave 2 §5.6, ADR-0016)
  http.get(`${API}/labels`, () => ok<Label[]>(sampleLabels, 200)),
  http.post(`${API}/labels`, async ({ request }) => {
    const b = (await request.json()) as { teamId: string; name: string; color: string };
    return ok<Label>(
      { id: `lb-${b.name.toLowerCase()}`, teamId: b.teamId, name: b.name, color: b.color.toLowerCase() },
      201,
    );
  }),
  http.put(`${API}/labels/:id`, async ({ request, params }) => {
    const b = (await request.json()) as { name: string; color: string };
    return ok<Label>(
      { id: String(params.id), teamId: sampleTeam.id, name: b.name, color: b.color.toLowerCase() },
      200,
    );
  }),
  http.delete(`${API}/labels/:id`, () => new HttpResponse(null, { status: 204 })),

  // Webhooks (Wave 3 §5.5, ADR-0021; M(team))
  http.get(`${API}/teams/:id/webhooks`, () => ok<WebhookSubscription[]>([sampleWebhook], 200)),
  http.post(`${API}/teams/:id/webhooks`, async ({ request, params }) => {
    const b = (await request.json()) as { url: string; eventTypes: string[]; active?: boolean };
    const subscription: WebhookSubscription = {
      ...sampleWebhook,
      id: 'wh-new',
      teamId: String(params.id),
      url: b.url,
      eventTypes: b.eventTypes,
      active: b.active ?? true,
    };
    return ok<CreateWebhookResponse>({ subscription, secret: 'whsec_revealed-once-abc123' }, 201);
  }),
  http.put(`${API}/webhooks/:id`, async ({ request, params }) => {
    const b = (await request.json()) as {
      url?: string;
      eventTypes?: string[];
      active?: boolean;
      rotateSecret?: boolean;
    };
    const subscription: WebhookSubscription = {
      ...sampleWebhook,
      id: String(params.id),
      url: b.url ?? sampleWebhook.url,
      eventTypes: b.eventTypes ?? sampleWebhook.eventTypes,
      active: b.active ?? sampleWebhook.active,
    };
    return ok<UpdateWebhookResponse>(
      { subscription, secret: b.rotateSecret ? 'whsec_rotated-xyz789' : null },
      200,
    );
  }),
  http.delete(`${API}/webhooks/:id`, () => new HttpResponse(null, { status: 204 })),
  http.get(`${API}/webhooks/:id/deliveries`, () =>
    ok<WebhookDeliveryList>(sampleWebhookDeliveries, 200),
  ),
  http.post(`${API}/webhooks/:id/ping`, () => ok({ deliveryId: 'wd-ping-1' }, 202)),

  // API keys (Wave 3 §5.6, ADR-0021; Self)
  http.get(`${API}/me/api-keys`, () => ok<ApiKey[]>([sampleApiKey], 200)),
  http.post(`${API}/me/api-keys`, async ({ request }) => {
    const b = (await request.json()) as { name: string; scopes: string[] };
    const scopes = b.scopes.includes('tickets:write')
      ? ['tickets:read', 'tickets:write']
      : b.scopes;
    const key: ApiKey = { ...sampleApiKey, id: 'ak-new', name: b.name, scopes };
    return ok<CreateApiKeyResponse>({ key, secret: 'ptk_revealed-once-key-value' }, 201);
  }),
  http.delete(`${API}/me/api-keys/:id`, () => new HttpResponse(null, { status: 204 })),

  // Analytics (Wave 3 §5.4, ADR-0020; M(team))
  http.get(`${API}/analytics/dashboard`, () => ok<Dashboard>(sampleDashboard, 200)),

  // Notifications (Wave 2 §8, Self)
  http.get(`${API}/notifications/unread-count`, () =>
    ok<UnreadCount>({ unreadCount: sampleNotificationList.unreadCount }, 200),
  ),
  http.get(`${API}/notifications`, () => ok<NotificationList>(sampleNotificationList, 200)),
  http.post(`${API}/notifications/read-all`, () => ok<UnreadCount>({ unreadCount: 0 }, 200)),
  http.post(`${API}/notifications/:id/read`, () => ok<UnreadCount>({ unreadCount: 0 }, 200)),

  // Admin — User Management (§8, admin-only)
  http.get(`${API}/admin/users`, () => ok<AdminUser[]>([sampleAdminUser, sampleMemberUser], 200)),
  http.post(`${API}/admin/users`, () =>
    ok({ user: sampleMemberUser, generatedPassword: 'Xk9$mPq2vLr7Wn4t' }, 201),
  ),
  http.put(`${API}/admin/users/:id/role`, () => ok<AdminUser>(sampleMemberUser, 200)),
  http.put(`${API}/admin/users/:id/teams`, () => ok<AdminUser>(sampleMemberUser, 200)),
  http.post(`${API}/admin/users/:id/block`, () =>
    ok<AdminUser>({ ...sampleMemberUser, isBlocked: true, status: 'blocked' }, 200),
  ),
  http.post(`${API}/admin/users/:id/unblock`, () => ok<AdminUser>(sampleMemberUser, 200)),
  http.post(`${API}/admin/users/:id/reset-password`, () =>
    ok({ generatedPassword: 'Nw7&pQz3xKr9Vm2t' }, 200),
  ),
];
