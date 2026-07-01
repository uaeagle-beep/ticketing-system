using TicketTracker.Application.Abstractions;

namespace TicketTracker.Application.Events;

/// <summary>
/// No-op <see cref="IRealtimeNotifier"/> (Wave 3, ADR-0019). It is the DEFAULT binding registered in the
/// Application layer so the event backbone always has a notifier to talk to even if the SignalR transport
/// is not wired (e.g. a host that does not call <c>AddSignalR</c> / map the hub). The API host REPLACES it
/// with <c>SignalRRealtimeNotifier</c>; the test factory replaces it with a recording fake. Keeping a safe
/// default here means the <c>RealtimeNotifier</c> handler and <c>NotificationFanout</c> never need a null
/// check and real-time flips on/off purely by which implementation the host binds.
/// </summary>
public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task BoardChangedAsync(Guid teamId, CancellationToken ct) => Task.CompletedTask;

    public Task TicketChangedAsync(Guid ticketId, Guid teamId, CancellationToken ct) => Task.CompletedTask;

    public Task NotifyUserAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
}
