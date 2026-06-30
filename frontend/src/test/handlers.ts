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
  AuthUser,
  Board,
  Comment,
  Epic,
  LoginResponse,
  MessageResponse,
  Team,
  TicketDetail,
} from '@/api/types';

// Same base path the real client uses (API_BASE = '/api').
export const API = '/api';

// ---- Canonical sample data (contract-shaped) -------------------------------

export const sampleUser: AuthUser = {
  id: '8e29c1b4-0000-4000-8000-000000000001',
  email: 'alex@dataart.com',
  emailVerified: true,
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
  title: 'Login fails',
  body: 'Steps to reproduce...',
  createdAt: '2026-06-22T09:15:00Z',
  modifiedAt: '2026-06-23T12:40:00Z',
  createdBy: sampleUser.id,
  createdByEmail: sampleUser.email,
};

export const sampleComment: Comment = {
  id: 'cm01-looks-fixed',
  ticketId: sampleTicketDetail.id,
  authorId: sampleUser.id,
  authorEmail: sampleUser.email,
  body: 'Looks fixed.',
  createdAt: '2026-06-23T13:00:00Z',
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
            title: 'Login fails',
            epicId: sampleEpic.id,
            epicTitle: sampleEpic.title,
            modifiedAt: '2026-06-23T12:40:00Z',
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

  http.get(`${API}/auth/me`, () => ok<AuthUser>(sampleUser, 200)),

  // Teams (§4)
  http.get(`${API}/teams`, () => ok<Team[]>([sampleTeam], 200)),
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
  http.delete(`${API}/tickets/:id`, () => new HttpResponse(null, { status: 204 })),

  // Comments (§7)
  http.get(`${API}/tickets/:id/comments`, () => ok<Comment[]>([sampleComment], 200)),
  http.post(`${API}/tickets/:id/comments`, () => ok<Comment>(sampleComment, 201)),
];
