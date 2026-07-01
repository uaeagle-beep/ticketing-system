// Real-time provider tests (Wave 3, ADR-0019). The SignalR connection is MOCKED so no real WebSocket is
// opened in jsdom (unit tests must never open sockets, per the developer's brief). We capture the message
// handlers the provider registers via `connection.on(...)`, then fire thin signals and assert the provider
// invalidates the correct React Query caches — the load-bearing behaviour (push → refetch through REST).

import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ---- Mock @microsoft/signalr with a controllable fake connection (no socket) ----
type Handler = (...args: unknown[]) => void;

const handlers = new Map<string, Handler>();
const fakeConnection = {
  state: 'Connected',
  on: vi.fn((name: string, cb: Handler) => handlers.set(name, cb)),
  onreconnecting: vi.fn(),
  onreconnected: vi.fn(),
  onclose: vi.fn(),
  start: vi.fn(() => Promise.resolve()),
  stop: vi.fn(() => Promise.resolve()),
  invoke: vi.fn(() => Promise.resolve()),
};

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return fakeConnection;
    }
  },
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
  LogLevel: { Warning: 4 },
}));

// The provider connects only when authenticated; stub the auth hook to report authenticated.
vi.mock('@/auth/AuthContext', () => ({
  useAuth: () => ({ status: 'authenticated' }),
  AuthProvider: ({ children }: { children: React.ReactNode }) => children,
}));

import { RealtimeProvider, useRealtime } from './RealtimeProvider';
import { queryKeys } from '@/lib/queryKeys';

function fire(name: string, payload?: unknown) {
  const cb = handlers.get(name);
  if (!cb) throw new Error(`no handler registered for "${name}"`);
  cb(payload);
}

describe('RealtimeProvider', () => {
  beforeEach(() => {
    handlers.clear();
    fakeConnection.state = 'Connected';
    vi.clearAllMocks();
  });

  function setup() {
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    render(
      <QueryClientProvider client={queryClient}>
        <RealtimeProvider>
          <div>child</div>
        </RealtimeProvider>
      </QueryClientProvider>,
    );
    return { invalidate };
  }

  it('starts a connection and registers the three signal handlers', async () => {
    setup();
    await waitFor(() => expect(fakeConnection.start).toHaveBeenCalled());
    expect(handlers.has('boardChanged')).toBe(true);
    expect(handlers.has('ticketChanged')).toBe(true);
    expect(handlers.has('notify')).toBe(true);
  });

  it('boardChanged invalidates the board query for the signalled team', async () => {
    const { invalidate } = setup();
    await waitFor(() => expect(fakeConnection.start).toHaveBeenCalled());

    fire('boardChanged', { teamId: 'team-1' });

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['board', 'team-1'] });
  });

  it('ticketChanged invalidates the ticket detail + its comments/activity/attachments', async () => {
    const { invalidate } = setup();
    await waitFor(() => expect(fakeConnection.start).toHaveBeenCalled());

    fire('ticketChanged', { ticketId: 't-9', teamId: 'team-1' });

    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.ticket('t-9') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.comments('t-9') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.activity('t-9') });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.attachments('t-9') });
  });

  it('notify invalidates the unread count and the notification list', async () => {
    const { invalidate } = setup();
    await waitFor(() => expect(fakeConnection.start).toHaveBeenCalled());

    fire('notify', {});

    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.notificationsUnread });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.notifications });
  });

  it('exposes connected=true and subscribeTeam invokes the hub method', async () => {
    let contextValue: ReturnType<typeof useRealtime> | null = null;
    function Probe() {
      contextValue = useRealtime();
      return null;
    }
    const queryClient = new QueryClient();
    render(
      <QueryClientProvider client={queryClient}>
        <RealtimeProvider>
          <Probe />
        </RealtimeProvider>
      </QueryClientProvider>,
    );
    await waitFor(() => expect(fakeConnection.start).toHaveBeenCalled());
    await waitFor(() => expect(contextValue?.connected).toBe(true));

    contextValue!.subscribeTeam('team-42');
    expect(fakeConnection.invoke).toHaveBeenCalledWith('SubscribeTeam', 'team-42');
  });
});
