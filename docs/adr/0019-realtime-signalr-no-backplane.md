# ADR 0019 — Real-time board via SignalR as a third event-backbone handler over a testable notifier, no backplane

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 3 approved scope (real-time board via websockets); [`WAVE3_DESIGN.md`](../WAVE3_DESIGN.md) §5.3/§6.4/§7.5/§9.2
- **Related ADRs:** 0012 (application event backbone — after-commit publisher + `ITicketEventHandler`), 0013 (notification fan-out), 0016/0014 (Wave-2 polling + email worker), 0001 (opaque bearer sessions), 0005 (single-server Docker), 0007 (team-scoped authz)

## Context

Wave 2 refreshes the board and notification bell by **polling + refetch-on-focus** ([ADR-0016]). Wave 3 wants live updates. The constraints: a **single server** ([ADR-0005]) so no backplane is strictly needed; the existing auth is an **opaque bearer token** (not JWT); SignalR/WebSockets are notoriously hard to unit-test; and the Wave-2 event backbone ([ADR-0012]) already publishes a `TicketEvent` list after every mutation to `ITicketEventHandler` implementations. A browser WebSocket handshake **cannot set an `Authorization` header**, so the token must travel another way.

## Decision

- **ASP.NET Core SignalR over WebSockets** (first-party, DI-integrated), exposed at `/hubs/board`.
- **Real-time is a third `ITicketEventHandler`, not a new emission path.** `RealtimeNotifier : ITicketEventHandler` is registered alongside `ActivityRecorder` and `NotificationFanout` (`services.AddScoped<ITicketEventHandler, RealtimeNotifier>()`). It consumes the **same** after-commit event batch and pushes group signals. `TicketService`/`CommentService` stay transport-agnostic (no `IHubContext` in Application).
- **Correctness lives behind a testable seam, not in the hub.** `RealtimeNotifier` depends only on `IRealtimeNotifier { BoardChangedAsync(teamId); TicketChangedAsync(ticketId, teamId); NotifyUserAsync(userId); }`. Production binds `SignalRRealtimeNotifier` (wraps `IHubContext<BoardHub>`); tests bind a recording fake and assert the handler emits the right signals. The `BoardHub` itself has near-zero logic (connect-auth + group join/leave) and gets only a thin smoke test.
- **Connection auth reuses the existing session token via `access_token`.** The SPA connects with `accessTokenFactory: () => getToken()`; SignalR sends the opaque token as `?access_token=` on the WS handshake. The hub's connect gate resolves it with **`AuthService.ResolveSessionUserAsync`** — the exact method `BearerAuthMiddleware` uses — aborting the connection on a null/blocked/expired principal. **No JWT scheme, no new credential type.** Over TLS the query string is inside the encrypted tunnel (per Microsoft SignalR security guidance); nginx must `access_log off` the `/hubs/` path so the token is never logged.
- **Group model:** `user:{userId}` (bell), `team:{teamId}` (board), `ticket:{ticketId}` (open detail). Joining any group runs a server-side `CanAccessTeam` check in the hub method (a client cannot subscribe to a team it can't see).
- **Thin signals, not entities.** Messages say "board/ticket/notifications changed"; the SPA reacts by invalidating the relevant React Query key and refetching through the authorized REST endpoint (which re-checks authz). No DTO shaping or authorization duplicated in the push path.
- **No backplane now; documented if scaled.** A single api process needs no Redis backplane. **If the api is ever horizontally scaled, a Redis (or Azure SignalR) backplane becomes mandatory** — recorded here and in the design (R-A8), not built.
- **Polling stays as a throttled fallback.** While connected, the bell's poll interval is raised (30s→120s) and reads rely on push-invalidation; on disconnect/reconnect the SPA reverts to Wave-2 30s polling. Push is primary; polling is the safety net so a dropped socket never leaves the UI stale.
- **nginx:** a dedicated `/hubs/` location sets `Upgrade`/`Connection` headers (the existing `/api/` block does not), a long `proxy_read_timeout`, and `access_log off`. Compose topology is unchanged (WS rides the existing web→api service and port).

## Consequences

- **Positive:** live updates with minimal new code (one handler + one thin hub); zero new auth type (reuses the session token + resolver); correctness fully unit-testable via `IRealtimeNotifier` despite SignalR being hard to test; polling remains a resilient fallback; flips on/off by registering/not-registering one handler.
- **Negative (accepted):** single-server only — horizontal scale needs a backplane (documented). Token-in-query-string requires the nginx log-suppression control (R-A5). At-most-once push (a crash between commit and push drops a signal) is harmless because polling backstops it and the next event re-syncs.
- **Testing:** the `CustomWebApplicationFactory` binds a null/recording `IRealtimeNotifier` (no real sockets in tests); handler behaviour is asserted against the fake; the hub gets a minimal connect/authorize smoke test.
