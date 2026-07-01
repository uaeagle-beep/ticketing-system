using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
// Alias the SignalR HttpContext feature (there are two IHttpContextFeature types; GetHttpContext() on a
// HubCallerContext reads the Connections one, so the test double must implement exactly that interface).
using IHubHttpContextFeature = Microsoft.AspNetCore.Http.Connections.Features.IHttpContextFeature;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketTracker.Api.Realtime;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Services;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Thin connect-auth + group-authorization smoke test for <see cref="BoardHub"/> (Wave 3, ADR-0019, §11 B).
/// The hub is a near-empty shell, so this asserts only the load-bearing transport behaviour WITHOUT a live
/// WebSocket: a valid <c>?access_token=</c> joins <c>user:{id}</c> + each accessible team group; an invalid /
/// blocked / missing token ABORTS the connection with no group joins; and <c>SubscribeTeam</c> for a team the
/// caller cannot see joins nothing (server-side <c>CanAccessTeam</c> gate). The token is resolved by the SAME
/// <see cref="AuthService.ResolveSessionUserAsync"/> the bearer middleware uses (resolved from the real DI
/// container over the in-memory SQLite factory, so blocked/expired semantics match the rest of the app).
/// Group joins are captured by a hand-rolled <see cref="IGroupManager"/> double — no mocking library, no
/// sockets, fully deterministic. Push correctness lives in <see cref="RealtimeNotifierTests"/> over the seam.
/// </summary>
public sealed class BoardHubTests : IntegrationTestBase
{
    private BoardHub CreateHub(string? accessToken, RecordingGroupManager groups, out FakeHubCallerContext ctx)
    {
        var scope = Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var hub = new BoardHub(auth, db, NullLogger<BoardHub>.Instance)
        {
            Groups = groups,
        };
        ctx = new FakeHubCallerContext(accessToken);
        hub.Context = ctx;
        return hub;
    }

    [Fact]
    public async Task Valid_token_joins_the_user_group_and_each_accessible_team_group()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        var team = await CreateTeamAndAddMemberAsync(userId);

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out var ctx);

        await hub.OnConnectedAsync();

        ctx.Aborted.Should().BeFalse("a valid, non-blocked token is accepted");
        groups.Joined.Should().Contain(BoardHub.UserGroup(userId), "the caller joins their notification-bell group");
        groups.Joined.Should().Contain(BoardHub.TeamGroup(team), "the caller joins each team they can access");
    }

    [Fact]
    public async Task Missing_token_aborts_the_connection_with_no_group_joins()
    {
        var groups = new RecordingGroupManager();
        var hub = CreateHub(accessToken: null, groups, out var ctx);

        await hub.OnConnectedAsync();

        ctx.Aborted.Should().BeTrue("a connection without an access_token is unauthenticated");
        groups.Joined.Should().BeEmpty("an aborted connection joins no groups");
    }

    [Fact]
    public async Task Invalid_token_aborts_the_connection()
    {
        var groups = new RecordingGroupManager();
        var hub = CreateHub(accessToken: "not-a-real-token", groups, out var ctx);

        await hub.OnConnectedAsync();

        ctx.Aborted.Should().BeTrue("an unknown token resolves to no principal → abort");
        groups.Joined.Should().BeEmpty();
    }

    [Fact]
    public async Task Blocked_user_token_aborts_the_connection()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        await CreateTeamAndAddMemberAsync(userId);
        await BlockUserAsync(userId); // blocking also purges the session, matching mid-session revocation

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out var ctx);

        await hub.OnConnectedAsync();

        ctx.Aborted.Should().BeTrue("a blocked user is treated as not authenticated (ASR-2)");
        groups.Joined.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeTeam_for_a_team_the_caller_cannot_see_joins_nothing()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        await CreateTeamAndAddMemberAsync(userId); // the caller's own team

        // A DIFFERENT team the caller is NOT a member of.
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var otherTeamId = Guid.Empty;
        await Factory.WithDbAsync(async db =>
        {
            var team = new TicketTracker.Domain.Entities.Team
            {
                Id = Guid.NewGuid(),
                Name = "Secret",
                NameNormalized = "secret",
                CreatedAt = Factory.Clock.UtcNow,
                ModifiedAt = Factory.Clock.UtcNow,
            };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            otherTeamId = team.Id;
        });

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out _);
        await hub.OnConnectedAsync();
        groups.Joined.Clear(); // ignore the connect-time joins; assert only the SubscribeTeam behaviour

        await hub.SubscribeTeam(otherTeamId);

        groups.Joined.Should().NotContain(BoardHub.TeamGroup(otherTeamId),
            "a client cannot subscribe to a team it cannot access (server-side CanAccessTeam gate, §7.5)");
    }

    [Fact]
    public async Task SubscribeTeam_for_an_accessible_team_joins_the_group()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        var teamId = await CreateTeamAndAddMemberAsync(userId);

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out _);
        await hub.OnConnectedAsync();
        groups.Joined.Clear();

        await hub.SubscribeTeam(teamId);

        groups.Joined.Should().Contain(BoardHub.TeamGroup(teamId));
    }

    [Fact]
    public async Task SubscribeTicket_resolves_the_tickets_real_team_and_denies_a_foreign_ticket()
    {
        // The caller is a member of their OWN team only.
        var (token, userId, _) = await RegisterMemberAsync();
        var ownTeamId = await CreateTeamAndAddMemberAsync(userId);

        // A DIFFERENT team the caller is NOT a member of, holding a ticket.
        var foreignTicketId = await SeedTicketInNewTeamAsync();

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out _);
        await hub.OnConnectedAsync();
        groups.Joined.Clear(); // ignore connect-time joins; assert only SubscribeTicket behaviour

        // The client LIES: it passes its OWN accessible team id for a ticket that belongs to the foreign team.
        // The server ignores the supplied team id, resolves the ticket's REAL team, and refuses the join (SEC-5).
        await hub.SubscribeTicket(foreignTicketId, ownTeamId);

        groups.Joined.Should().NotContain(BoardHub.TicketGroup(foreignTicketId),
            "a member cannot subscribe to a ticket in a team they don't belong to; the real team is resolved server-side (SEC-5)");
    }

    [Fact]
    public async Task SubscribeTicket_joins_a_ticket_in_a_team_the_caller_can_access()
    {
        var (token, userId, _) = await RegisterMemberAsync();
        var teamId = await CreateTeamAndAddMemberAsync(userId);
        var ticketId = await SeedTicketInTeamAsync(teamId, userId);

        var groups = new RecordingGroupManager();
        var hub = CreateHub(token, groups, out _);
        await hub.OnConnectedAsync();
        groups.Joined.Clear();

        // A stale/incorrect client team id must not matter either way — the server resolves the real team.
        await hub.SubscribeTicket(ticketId, Guid.NewGuid());

        groups.Joined.Should().Contain(BoardHub.TicketGroup(ticketId),
            "the caller can subscribe to a ticket whose real team they belong to (server-resolved, SEC-5)");
    }

    // ---- helpers ----

    /// <summary>Create a brand-new team NOBODY in the test is a member of, seed one ticket in it, return the ticket id.</summary>
    private async Task<Guid> SeedTicketInNewTeamAsync()
    {
        var teamId = Guid.NewGuid();
        await Factory.WithDbAsync(async db =>
        {
            db.Teams.Add(new TicketTracker.Domain.Entities.Team
            {
                Id = teamId,
                Name = "Foreign",
                NameNormalized = "foreign",
                CreatedAt = Factory.Clock.UtcNow,
                ModifiedAt = Factory.Clock.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        // The ticket's creator is irrelevant to the access check; use a fresh admin so a valid FK exists.
        var (_, adminId, _) = await RegisterAdminAsync();
        return await SeedTicketInTeamAsync(teamId, adminId);
    }

    /// <summary>Seed one ticket in the given team created by the given user; return the ticket id.</summary>
    private async Task<Guid> SeedTicketInTeamAsync(Guid teamId, Guid createdBy)
    {
        var ticketId = Guid.NewGuid();
        await Factory.WithDbAsync(async db =>
        {
            db.Tickets.Add(new TicketTracker.Domain.Entities.Ticket
            {
                Id = ticketId,
                TeamId = teamId,
                Type = TicketTracker.Domain.Enums.TicketType.Bug,
                State = TicketTracker.Domain.Enums.TicketState.New,
                Priority = TicketTracker.Domain.Enums.TicketPriority.Medium,
                Title = "Seed",
                Body = "b",
                CreatedBy = createdBy,
                CreatedAt = Factory.Clock.UtcNow,
                ModifiedAt = Factory.Clock.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        return ticketId;
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

    // ---- hand-rolled SignalR doubles (no mocking library, no sockets) ----

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

    /// <summary>
    /// Minimal <see cref="HubCallerContext"/> that carries an <see cref="HttpContext"/> (with the token on the
    /// <c>access_token</c> query string) via the <see cref="IHttpContextFeature"/> so the hub's
    /// <c>Context.GetHttpContext()</c> works, and records whether <see cref="Abort"/> was called.
    /// </summary>
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

        /// <summary>Minimal SignalR HttpContext feature (the framework's impl is internal).</summary>
        private sealed class TestHttpContextFeature : IHubHttpContextFeature
        {
            public TestHttpContextFeature(HttpContext httpContext) => HttpContext = httpContext;
            public HttpContext? HttpContext { get; set; }
        }
    }
}
