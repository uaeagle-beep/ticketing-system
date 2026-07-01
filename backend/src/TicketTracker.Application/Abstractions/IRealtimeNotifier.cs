namespace TicketTracker.Application.Abstractions;

/// <summary>
/// The testable real-time push seam (Wave 3, ADR-0019, §6.4). The <c>RealtimeNotifier</c> event-backbone
/// handler and <c>NotificationFanout</c> depend ONLY on this interface — never on <c>IHubContext</c> — so
/// push CORRECTNESS lives in code that can be asserted against a recording fake, while the SignalR
/// transport stays a near-empty shell ("correctness in a testable seam, transport in a thin shell", the
/// same principle the email/webhook workers use).
///
/// Every method pushes a THIN "something-changed" signal (no entity payloads, [ASSUMPTION W3-RT-PAYLOAD]):
/// the client reacts by invalidating the relevant React Query key and refetching through the authorized
/// REST endpoint, which re-runs server-side authz. The signal only says "refresh"; it never carries data.
///
/// Two production/transport implementations plus a test double:
///  - <c>SignalRRealtimeNotifier</c> (API layer) wraps <c>IHubContext&lt;BoardHub&gt;</c> and sends to groups;
///  - <c>NullRealtimeNotifier</c> (below) is a no-op default so the app boots even if SignalR is not wired;
///  - a recording fake in tests asserts "a board-changed signal for team X was pushed" with no live socket.
///
/// Implementations MUST NOT throw: a push is a best-effort side effect that must never roll back the user's
/// mutation (at-most-once, mirrors the handler contract). Polling backstops any dropped signal (ADR-0019).
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>Signal the <c>team:{teamId}</c> group that the board changed (SPA invalidates the board query).</summary>
    Task BoardChangedAsync(Guid teamId, CancellationToken ct);

    /// <summary>
    /// Signal the <c>ticket:{ticketId}</c> group that a ticket changed (SPA invalidates the ticket + its
    /// comments/activity/attachments keys). <paramref name="teamId"/> is carried so a client that only
    /// subscribes at the team level can still react without a second lookup.
    /// </summary>
    Task TicketChangedAsync(Guid ticketId, Guid teamId, CancellationToken ct);

    /// <summary>Signal the <c>user:{userId}</c> group that their notifications changed (the bell refetches).</summary>
    Task NotifyUserAsync(Guid userId, CancellationToken ct);
}
