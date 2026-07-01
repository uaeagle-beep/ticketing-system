using Microsoft.AspNetCore.SignalR;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Api.Realtime;

/// <summary>
/// Production <see cref="IRealtimeNotifier"/> (Wave 3, ADR-0019, §6.4): the thin transport shell that turns
/// the seam's method calls into SignalR group sends over <see cref="IHubContext{BoardHub}"/>. It carries the
/// server→client message names/shapes ([ASSUMPTION W3-RT-PAYLOAD]):
///  - <c>boardChanged { teamId }</c>   → <c>team:{teamId}</c>   (SPA invalidates the board query)
///  - <c>ticketChanged { ticketId, teamId }</c> → <c>ticket:{ticketId}</c> (SPA invalidates ticket keys)
///  - <c>notify {}</c>                 → <c>user:{userId}</c>   (the notification bell refetches)
///
/// A board mutation reaches both the team's board (via boardChanged) and an open ticket page (via
/// ticketChanged). Sends are best-effort and NEVER throw — a push is a side effect that must not roll back
/// the user's mutation, and polling backstops any dropped signal (the handler already runs inside the
/// publisher's swallow-and-log, but this belt-and-suspenders keeps a transport hiccup from surfacing).
/// Bound in Program.cs; the Application layer's <c>NullRealtimeNotifier</c> is the fallback default.
/// </summary>
public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<BoardHub> _hub;
    private readonly ILogger<SignalRRealtimeNotifier> _logger;

    public SignalRRealtimeNotifier(IHubContext<BoardHub> hub, ILogger<SignalRRealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task BoardChangedAsync(Guid teamId, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(BoardHub.TeamGroup(teamId))
                .SendAsync("boardChanged", new { teamId }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push boardChanged for team {TeamId}.", teamId);
        }
    }

    public async Task TicketChangedAsync(Guid ticketId, Guid teamId, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(BoardHub.TicketGroup(ticketId))
                .SendAsync("ticketChanged", new { ticketId, teamId }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push ticketChanged for ticket {TicketId}.", ticketId);
        }
    }

    public async Task NotifyUserAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(BoardHub.UserGroup(userId))
                .SendAsync("notify", new { }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push notify for user {UserId}.", userId);
        }
    }
}
