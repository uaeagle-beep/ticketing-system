using System.Collections.Concurrent;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Tests.Fakes;

/// <summary>
/// Recording test double for <see cref="IRealtimeNotifier"/> (Wave 3, ADR-0019, §11 B). It captures every
/// thin signal the <c>RealtimeNotifier</c> handler + <c>NotificationFanout</c> push, so integration tests
/// can assert "a board-changed signal for team X was pushed to the right group" / "the actor's watchers got
/// a bell ping" WITHOUT a live WebSocket or <c>IHubContext</c> — push correctness lives in the seam, not the
/// transport (ADR-0019). Registered as a singleton in the test factory (in place of the production
/// SignalRRealtimeNotifier) so captures survive across the scoped lifetimes of a request's event fan-out.
/// Never throws (matches the seam contract).
/// </summary>
public sealed class RecordingRealtimeNotifier : IRealtimeNotifier
{
    private readonly ConcurrentQueue<Guid> _boardChanges = new();
    private readonly ConcurrentQueue<(Guid TicketId, Guid TeamId)> _ticketChanges = new();
    private readonly ConcurrentQueue<Guid> _userNotifies = new();

    /// <summary>Every <c>boardChanged</c> team id pushed, in order (duplicates preserved).</summary>
    public IReadOnlyList<Guid> BoardChanges => _boardChanges.ToArray();

    /// <summary>Every <c>ticketChanged</c> (ticketId, teamId) pushed, in order.</summary>
    public IReadOnlyList<(Guid TicketId, Guid TeamId)> TicketChanges => _ticketChanges.ToArray();

    /// <summary>Every <c>notify</c> user id pushed (bell pings), in order.</summary>
    public IReadOnlyList<Guid> UserNotifies => _userNotifies.ToArray();

    /// <summary>Drop all captured signals (rarely needed — each test gets a fresh factory + fake).</summary>
    public void Reset()
    {
        _boardChanges.Clear();
        _ticketChanges.Clear();
        _userNotifies.Clear();
    }

    public Task BoardChangedAsync(Guid teamId, CancellationToken ct)
    {
        _boardChanges.Enqueue(teamId);
        return Task.CompletedTask;
    }

    public Task TicketChangedAsync(Guid ticketId, Guid teamId, CancellationToken ct)
    {
        _ticketChanges.Enqueue((ticketId, teamId));
        return Task.CompletedTask;
    }

    public Task NotifyUserAsync(Guid userId, CancellationToken ct)
    {
        _userNotifies.Enqueue(userId);
        return Task.CompletedTask;
    }
}
