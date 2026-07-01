using Microsoft.AspNetCore.SignalR;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Realtime;

/// <summary>
/// The real-time transport shell (Wave 3, ADR-0019, §5.3). Deliberately NEAR-EMPTY: it does connect-time
/// auth and group join/leave and NOTHING else — all push correctness lives in <c>RealtimeNotifier</c> over
/// the testable <see cref="IRealtimeNotifier"/> seam, so this class needs only a thin smoke test.
///
/// Connection auth ([ASSUMPTION W3-RT-TOKEN]): a browser WebSocket handshake cannot set an
/// <c>Authorization</c> header, so the SPA sends the EXISTING opaque session token as the
/// <c>?access_token=</c> query-string parameter; SignalR surfaces it on the negotiate/connect request.
/// <see cref="OnConnectedAsync"/> resolves it with the SAME <see cref="AuthService.ResolveSessionUserAsync"/>
/// the bearer middleware uses — a null/blocked/expired/unknown principal ABORTS the connection (no group
/// joins). No JWT scheme, no new credential type; over TLS the query string rides inside the encrypted
/// tunnel and nginx must <c>access_log off</c> the hub path so the token is never logged (§7.5).
///
/// Groups: on connect the caller joins <c>user:{userId}</c> (the notification bell) and <c>team:{teamId}</c>
/// for every team it can access. <see cref="SubscribeTeam"/> / <see cref="SubscribeTicket"/> /
/// <see cref="UnsubscribeTicket"/> let the SPA (re)join a team (admins whose membership list is empty open
/// teams explicitly) and the open ticket-detail group — each re-checks <c>CanAccessTeam</c> SERVER-SIDE
/// before adding to a group, so a client can never subscribe to a team it cannot see (§7.5). Messages are
/// thin signals only, so even a mis-joined group would leak nothing but "something changed" and the
/// follow-up REST refetch re-checks authz.
/// </summary>
public sealed class BoardHub : Hub
{
    /// <summary>The hub route (mapped in Program.cs; proxied by the nginx <c>/hubs/</c> location).</summary>
    public const string Path = "/hubs/board";

    private const string AccessTokenQueryKey = "access_token";

    private readonly AuthService _auth;
    private readonly ILogger<BoardHub> _logger;

    public BoardHub(AuthService auth, ILogger<BoardHub> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    public static string UserGroup(Guid userId) => $"user:{userId}";
    public static string TeamGroup(Guid teamId) => $"team:{teamId}";
    public static string TicketGroup(Guid ticketId) => $"ticket:{ticketId}";

    public override async Task OnConnectedAsync()
    {
        var principal = await ResolveCallerAsync();
        if (principal is null)
        {
            // Unauthenticated / blocked / expired: abort the connection with no group joins (§7.5).
            Context.Abort();
            return;
        }

        // Stash the resolved principal for the lifetime of the connection so the client→server hub methods
        // do not re-hit the DB for the token on every call.
        Context.Items[nameof(CurrentPrincipal)] = principal;

        // The bell group + every team the caller can access. Admins with an empty team list join nothing
        // here and open specific teams via SubscribeTeam (still access-checked).
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(principal.UserId));
        foreach (var teamId in principal.TeamIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Join <c>team:{teamId}</c> after a SERVER-SIDE access check (the board subscribes to its team; an admin
    /// opens a team not in their membership list). A caller without access is silently ignored — no group join.
    /// </summary>
    public async Task SubscribeTeam(Guid teamId)
    {
        var principal = CurrentPrincipal;
        if (principal is null || !CanAccessTeam(principal, teamId))
            return;
        await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));
    }

    /// <summary>Join <c>ticket:{ticketId}</c> for the open ticket-detail page, gated by the ticket's team access.</summary>
    public async Task SubscribeTicket(Guid ticketId, Guid teamId)
    {
        var principal = CurrentPrincipal;
        if (principal is null || !CanAccessTeam(principal, teamId))
            return;
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(ticketId));
    }

    /// <summary>Leave <c>ticket:{ticketId}</c> when the ticket-detail page unmounts. Always safe to call.</summary>
    public Task UnsubscribeTicket(Guid ticketId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketGroup(ticketId));

    private CurrentPrincipal? CurrentPrincipal
        => Context.Items.TryGetValue(nameof(CurrentPrincipal), out var value)
            ? value as CurrentPrincipal
            : null;

    private static bool CanAccessTeam(CurrentPrincipal principal, Guid teamId)
        => principal.IsAdmin || principal.TeamIds.Contains(teamId);

    /// <summary>
    /// Extract the token (query string first — the WS handshake path; Authorization header as a fallback for
    /// negotiate over HTTP) and resolve it to a principal via the shared session-resolution path.
    /// </summary>
    private async Task<CurrentPrincipal?> ResolveCallerAsync()
    {
        var http = Context.GetHttpContext();
        if (http is null)
            return null;

        var token = http.Request.Query[AccessTokenQueryKey].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            var header = http.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                token = header[prefix.Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            return await _auth.ResolveSessionUserAsync(token, Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            // A resolution failure must abort the connection, not crash the hub pipeline.
            _logger.LogWarning(ex, "Hub connection token resolution failed; aborting the connection.");
            return null;
        }
    }
}
