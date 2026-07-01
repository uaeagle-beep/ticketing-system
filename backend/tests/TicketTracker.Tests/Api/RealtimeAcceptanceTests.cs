using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using IHubHttpContextFeature = Microsoft.AspNetCore.Http.Connections.Features.IHttpContextFeature;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketTracker.Api.Realtime;
using TicketTracker.Application.Services;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite for real-time push (Wave 3, ADR-0019, §11 B). Presses gaps beyond the developer smoke
/// tests: an EXPIRED session token aborts the hub connection; SubscribeTicket joins the ticket group only
/// after a server-side team-access check (a non-member joins nothing); UnsubscribeTicket leaves; the notify
/// bell fan-out pings EVERY watcher (never the actor); and an attachment_added event drives board + ticket
/// signals + a per-watcher bell ping over the same seam. Push correctness is asserted over the recording fake
/// (no live socket); the hub-shell behaviour uses hand-rolled SignalR doubles.
/// </summary>
public sealed class RealtimeAcceptanceTests : IntegrationTestBase
{
    // ================= Push correctness over the recording notifier (real HTTP events) =================

    private async Task<(HttpClient Admin, Guid AdminId, Guid TeamId, Guid TicketId)> SetupAsync()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));
        return (admin, adminId, team.Id, ticket.Id);
    }

    private async Task<(Guid UserId, HttpClient Client)> AddWatcherAsync(Guid teamId, Guid ticketId)
    {
        var (token, userId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(userId, teamId);
        var client = Authed(token);
        (await client.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();
        return (userId, client);
    }

    [Fact]
    public async Task Comment_bell_ping_reaches_every_watcher_but_never_the_actor()
    {
        var (admin, adminId, teamId, ticketId) = await SetupAsync();
        var (w1, _) = await AddWatcherAsync(teamId, ticketId);
        var (w2, _) = await AddWatcherAsync(teamId, ticketId);
        Factory.Realtime.Reset();

        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "note" }))
            .EnsureSuccessStatusCode();

        Factory.Realtime.UserNotifies.Should().Contain(w1).And.Contain(w2,
            "every watcher who got a notification row gets a live bell ping");
        Factory.Realtime.UserNotifies.Should().NotContain(adminId,
            "the actor is excluded from fan-out and so from the bell ping");
    }

    [Fact]
    public async Task Attachment_added_pushes_board_and_ticket_signals_and_a_watcher_bell_ping()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (watcherId, _) = await AddWatcherAsync(teamId, ticketId);
        Factory.Realtime.Reset();

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
        var content = new ByteArrayContent(png);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { content, "file", "s.png" } };
        (await admin.PostAsync($"/api/tickets/{ticketId}/attachments", form)).EnsureSuccessStatusCode();

        Factory.Realtime.BoardChanges.Should().Contain(teamId, "attachment_added is an after-commit ticket event");
        Factory.Realtime.TicketChanges.Should().Contain(c => c.TicketId == ticketId && c.TeamId == teamId,
            "the ticket detail (attachments list) changed");
        Factory.Realtime.UserNotifies.Should().Contain(watcherId, "attachment_added notifies watchers → bell ping");
    }

    // ================= Hub shell: expired token aborts + ticket-group access gate =================

    private BoardHub CreateHub(string? accessToken, RecordingGroupManager groups, out FakeHubCallerContext ctx)
    {
        var scope = Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var hub = new BoardHub(auth, NullLogger<BoardHub>.Instance) { Groups = groups };
        ctx = new FakeHubCallerContext(accessToken);
        hub.Context = ctx;
        return hub;
    }

    private async Task<Guid> CreateTeamAndAddMemberAsync(Guid userId)
    {
        var teamId = Guid.NewGuid();
        await Factory.WithDbAsync(async db =>
        {
            db.Teams.Add(new TicketTracker.Domain.Entities.Team
            {
                Id = teamId,
                Name = "Platform",
                NameNormalized = "platform",
                CreatedAt = Factory.Clock.UtcNow,
                ModifiedAt = Factory.Clock.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        await AddMembershipAsync(userId, teamId);
        return teamId;
    }

    [Fact]
    public async Task Expired_session_token_aborts_the_hub_connection()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        await CreateTeamAndAddMemberAsync(userId);

        // Session TTL is 72h; advance past it so the token is expired at hub-connect time.
        Factory.Clock.Advance(TimeSpan.FromHours(73));

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out var ctx);
        await hub.OnConnectedAsync();

        ctx.Aborted.Should().BeTrue("an expired session token resolves to no principal → abort (§7.5)");
        groups.Joined.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeTicket_joins_the_ticket_group_for_an_accessible_team()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        var teamId = await CreateTeamAndAddMemberAsync(userId);
        var ticketId = Guid.NewGuid();

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out _);
        await hub.OnConnectedAsync();
        groups.Joined.Clear();

        await hub.SubscribeTicket(ticketId, teamId);
        groups.Joined.Should().Contain(BoardHub.TicketGroup(ticketId));
    }

    [Fact]
    public async Task SubscribeTicket_for_a_team_the_caller_cannot_see_joins_nothing()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        await CreateTeamAndAddMemberAsync(userId);
        var foreignTeam = Guid.NewGuid(); // a team the caller is NOT a member of
        var ticketId = Guid.NewGuid();

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out _);
        await hub.OnConnectedAsync();
        groups.Joined.Clear();

        await hub.SubscribeTicket(ticketId, foreignTeam);
        groups.Joined.Should().BeEmpty("the ticket group join is gated by the ticket's team access (§7.5)");
    }

    [Fact]
    public async Task UnsubscribeTicket_leaves_the_ticket_group()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        var teamId = await CreateTeamAndAddMemberAsync(userId);
        var ticketId = Guid.NewGuid();

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out _);
        await hub.OnConnectedAsync();
        await hub.SubscribeTicket(ticketId, teamId);
        groups.Joined.Should().Contain(BoardHub.TicketGroup(ticketId));

        await hub.UnsubscribeTicket(ticketId);
        groups.Joined.Should().NotContain(BoardHub.TicketGroup(ticketId), "unsubscribe leaves the group");
    }

    // ---- hand-rolled SignalR doubles (mirror BoardHubTests; no mocking library, no sockets) ----

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<string> Joined { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Joined.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Joined.Remove(groupName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly IFeatureCollection _features = new FeatureCollection();
        private readonly CancellationTokenSource _cts = new();

        public bool Aborted { get; private set; }

        public FakeHubCallerContext(string? accessToken)
        {
            var http = new DefaultHttpContext();
            if (accessToken is not null)
                http.Request.QueryString = new QueryString($"?access_token={Uri.EscapeDataString(accessToken)}");
            _features.Set<IHubHttpContextFeature>(new TestHttpContextFeature(http));
        }

        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => _features;
        public override CancellationToken ConnectionAborted => _cts.Token;

        public override void Abort()
        {
            Aborted = true;
            _cts.Cancel();
        }

        private sealed class TestHttpContextFeature : IHubHttpContextFeature
        {
            public TestHttpContextFeature(HttpContext httpContext) => HttpContext = httpContext;
            public HttpContext? HttpContext { get; set; }
        }
    }
}
