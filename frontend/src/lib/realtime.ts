// Real-time transport wrapper around @microsoft/signalr (Wave 3, ADR-0019, §10.2). Kept thin and
// framework-agnostic so the React hook (useRealtime) stays simple and tests can mock the connection
// without opening a real socket. The hub at /hubs/board authenticates with the EXISTING opaque session
// token, which SignalR sends as `?access_token=` on the WS handshake (a browser cannot set an
// Authorization header on a WebSocket) — resolved server-side by the same session path as REST.
//
// The connection carries THIN signals only ([ASSUMPTION W3-RT-PAYLOAD]); the payloads below say WHAT
// changed (ids), and the hook reacts by invalidating the relevant React Query keys and refetching through
// the authorized REST endpoint. No entity data ever rides the socket.

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { getToken } from '@/api/tokenStore';

/** The hub URL (single-origin via nginx, which proxies /hubs/ with the WebSocket upgrade headers). */
export const BOARD_HUB_URL = '/hubs/board';

// ---- Server→client message payloads (mirror SignalRRealtimeNotifier) ----
export interface BoardChangedSignal {
  teamId: string;
}
export interface TicketChangedSignal {
  ticketId: string;
  teamId: string;
}
// `notify` is a bare ping — the SPA refetches the unread count + list.

export interface RealtimeHandlers {
  onBoardChanged: (signal: BoardChangedSignal) => void;
  onTicketChanged: (signal: TicketChangedSignal) => void;
  onNotify: () => void;
}

/**
 * Build (but do not start) a board hub connection. `accessTokenFactory` is called on every
 * (re)negotiate, so an auto-reconnect always re-reads the CURRENT token from the store — a token that
 * was cleared on logout yields an empty string and the connection fails closed. Auto-reconnect is on so
 * a dropped socket recovers without a page reload; while it is down the SPA's polling fallback keeps the
 * UI fresh (ADR-0019).
 */
export function createBoardConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(BOARD_HUB_URL, {
      accessTokenFactory: () => getToken() ?? '',
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

/** Attach the message handlers to a connection (idempotent per handler name). */
export function bindHandlers(connection: HubConnection, handlers: RealtimeHandlers): void {
  connection.on('boardChanged', (signal: BoardChangedSignal) => handlers.onBoardChanged(signal));
  connection.on('ticketChanged', (signal: TicketChangedSignal) => handlers.onTicketChanged(signal));
  connection.on('notify', () => handlers.onNotify());
}

export { HubConnectionState };
export type { HubConnection };
