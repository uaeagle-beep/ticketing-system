// Real-time provider (Wave 3, ADR-0019, §10.2). Establishes ONE board-hub connection after the user is
// authenticated and wires the thin server→client signals to React Query cache invalidation so the UI
// refetches through the authorized REST path (push says "refresh"; the read re-checks authz). Auto-reconnect
// is handled by the connection; on logout the connection is stopped and the token cleared. The context
// exposes the connection state (so the polling hooks can throttle when connected — graceful fallback) and
// per-ticket subscribe/unsubscribe for the open ticket-detail page.

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from '@/auth/AuthContext';
import { queryKeys } from '@/lib/queryKeys';
import {
  bindHandlers,
  createBoardConnection,
  HubConnectionState,
  type HubConnection,
} from '@/lib/realtime';

interface RealtimeContextValue {
  /** True while the hub connection is Connected (drives the polling throttle). */
  connected: boolean;
  /** Join the ticket group for the open ticket-detail page; returns a cleanup that leaves it. */
  subscribeTicket: (ticketId: string, teamId: string) => () => void;
  /** Explicitly join a team group (e.g. an admin opening a team not in their memberships). */
  subscribeTeam: (teamId: string) => void;
}

const RealtimeContext = createContext<RealtimeContextValue | null>(null);

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  const queryClient = useQueryClient();
  const connectionRef = useRef<HubConnection | null>(null);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    // Only connect when authenticated; disconnect otherwise (login/logout transitions).
    if (status !== 'authenticated') {
      return;
    }

    const connection = createBoardConnection();
    connectionRef.current = connection;
    let disposed = false;

    bindHandlers(connection, {
      // Board changed → refetch every cached board variant for that team (all filter combos).
      onBoardChanged: ({ teamId }) => {
        queryClient.invalidateQueries({ queryKey: ['board', teamId] });
      },
      // Ticket changed → refetch the ticket detail + its comments/activity/attachments.
      onTicketChanged: ({ ticketId }) => {
        queryClient.invalidateQueries({ queryKey: queryKeys.ticket(ticketId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.comments(ticketId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.activity(ticketId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.attachments(ticketId) });
      },
      // Notification bell ping → refetch the unread count + the list.
      onNotify: () => {
        queryClient.invalidateQueries({ queryKey: queryKeys.notificationsUnread });
        queryClient.invalidateQueries({ queryKey: queryKeys.notifications });
      },
    });

    const syncState = () => {
      if (!disposed) setConnected(connection.state === HubConnectionState.Connected);
    };
    connection.onreconnecting(syncState);
    connection.onreconnected(syncState);
    connection.onclose(syncState);

    connection
      .start()
      .then(syncState)
      .catch(() => {
        // Connection failed to start (e.g. offline, token rejected). The polling fallback keeps the UI
        // fresh; auto-reconnect will retry. Never surface this as a user-facing error.
        syncState();
      });

    return () => {
      disposed = true;
      setConnected(false);
      connectionRef.current = null;
      // stop() is safe to call in any state; ignore rejection during teardown.
      void connection.stop().catch(() => undefined);
    };
  }, [status, queryClient]);

  const subscribeTeam = useCallback((teamId: string) => {
    const connection = connectionRef.current;
    if (connection && connection.state === HubConnectionState.Connected) {
      void connection.invoke('SubscribeTeam', teamId).catch(() => undefined);
    }
  }, []);

  const subscribeTicket = useCallback((ticketId: string, teamId: string) => {
    const connection = connectionRef.current;
    if (connection && connection.state === HubConnectionState.Connected) {
      void connection.invoke('SubscribeTicket', ticketId, teamId).catch(() => undefined);
    }
    // Cleanup leaves the ticket group when the detail page unmounts.
    return () => {
      const active = connectionRef.current;
      if (active && active.state === HubConnectionState.Connected) {
        void active.invoke('UnsubscribeTicket', ticketId).catch(() => undefined);
      }
    };
  }, []);

  const value = useMemo<RealtimeContextValue>(
    () => ({ connected, subscribeTicket, subscribeTeam }),
    [connected, subscribeTicket, subscribeTeam],
  );

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

/**
 * Access the real-time context. Returns a SAFE no-op default when used outside a provider (e.g. isolated
 * component tests that don't mount RealtimeProvider), so consumers never need to guard for null and unit
 * tests need not open a socket.
 */
export function useRealtime(): RealtimeContextValue {
  return (
    useContext(RealtimeContext) ?? {
      connected: false,
      subscribeTicket: () => () => undefined,
      subscribeTeam: () => undefined,
    }
  );
}
