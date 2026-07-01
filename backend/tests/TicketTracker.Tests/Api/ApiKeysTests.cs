using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Developer smoke tests for API keys + the public /api/v1 key-authenticated surface (Wave 3, ADR-0021,
/// API_CONTRACT §5.6). Samples the load-bearing behaviours — create returns the raw ptk_ key once (hash +
/// prefix stored, never the raw); list never returns raw/hash; revoke → immediate 401; a ptk_ bearer reads
/// /api/v1 with tickets:read; a write route with only tickets:read → 403 insufficient_scope; a write with
/// tickets:write → 200 and created_by = the key owner; a ptk_ token on /api/admin/* or any non-v1 path → 401;
/// a session token is rejected on /api/v1; another user's key id on revoke → 404 self-mask. Full acceptance
/// coverage is the Tester's job.
/// </summary>
public sealed class ApiKeysTests : IntegrationTestBase
{
    private HttpClient WithKey(string rawKey)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
        return client;
    }

    private async Task<(HttpClient Session, Guid UserId, Guid TeamId)> SetupUserWithTeamAsync()
    {
        var (token, userId, _) = await RegisterVerifiedUserAsync();
        var session = Authed(token);
        var team = await ReadAsync<TeamDto>(await session.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        // SEC-6: an API key's team access is EXPLICIT MEMBERSHIPS ONLY (the owner's admin breadth does NOT
        // apply to key requests). Creating a team does not add the creator as a member, so grant the owner an
        // explicit membership here — otherwise a key over this team would (correctly) be 403 (see the dedicated
        // membership-only tests in ApiKeysAcceptanceTests).
        await AddMembershipAsync(userId, team.Id);
        return (session, userId, team.Id);
    }

    private async Task<CreateApiKeyResponseDto> CreateKeyAsync(HttpClient session, params string[] scopes)
    {
        var resp = await session.PostAsJsonAsync("/api/me/api-keys",
            new { name = "CI pipeline", scopes });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadAsync<CreateApiKeyResponseDto>(resp);
    }

    // ---- Create: raw once, hash + prefix stored ----

    [Fact]
    public async Task Create_returns_raw_key_once_and_stores_only_hash_and_prefix()
    {
        var (session, userId, _) = await SetupUserWithTeamAsync();

        var created = await CreateKeyAsync(session, "tickets:write");
        created.Secret.Should().StartWith("ptk_", "the raw key is revealed once on create");
        created.Key.Prefix.Should().StartWith("ptk_").And.HaveLength(12);
        // write implies read (§5.6).
        created.Key.Scopes.Should().BeEquivalentTo(new[] { "tickets:read", "tickets:write" });

        await Factory.WithDbAsync(async db =>
        {
            var stored = await db.ApiKeys.SingleAsync(k => k.Id == created.Key.Id);
            stored.UserId.Should().Be(userId);
            stored.TokenHash.Should().NotContain(created.Secret, "only the hash is stored, never the raw key");
            stored.TokenHash.Should().HaveLength(64);
        });

        // The list returns metadata but no raw/hash (the DTO has no such field).
        var list = await ReadAsync<List<ApiKeyDto>>(await session.GetAsync("/api/me/api-keys"));
        list.Should().ContainSingle(k => k.Id == created.Key.Id);
    }

    [Fact]
    public async Task Create_with_unknown_scope_is_400_keyed_scopes()
    {
        var (session, _, _) = await SetupUserWithTeamAsync();
        var resp = await session.PostAsJsonAsync("/api/me/api-keys", new { name = "k", scopes = new[] { "admin:all" } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("scopes");
    }

    // ---- API-key auth on /api/v1: read scope ----

    [Fact]
    public async Task Read_scope_key_can_read_v1_board()
    {
        var (session, _, teamId) = await SetupUserWithTeamAsync();
        var created = await CreateKeyAsync(session, "tickets:read");
        var key = WithKey(created.Secret);

        var resp = await key.GetAsync($"/api/v1/tickets?teamId={teamId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var board = await ReadAsync<BoardDto>(resp);
        board.TeamId.Should().Be(teamId);
    }

    // ---- Scope enforcement: read-only key on a write route → 403 insufficient_scope ----

    [Fact]
    public async Task Read_only_key_on_a_write_route_is_403_insufficient_scope()
    {
        var (session, _, teamId) = await SetupUserWithTeamAsync();
        var created = await CreateKeyAsync(session, "tickets:read");
        var key = WithKey(created.Secret);

        var resp = await key.PostAsJsonAsync("/api/v1/tickets",
            new { teamId, type = "bug", title = "From API", body = "Body" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(resp)).Code.Should().Be("insufficient_scope");
    }

    // ---- Write scope: create a ticket, created_by = the key owner ----

    [Fact]
    public async Task Write_scope_key_creates_a_ticket_owned_by_the_key_owner()
    {
        var (session, userId, teamId) = await SetupUserWithTeamAsync();
        var created = await CreateKeyAsync(session, "tickets:write");
        var key = WithKey(created.Secret);

        var resp = await key.PostAsJsonAsync("/api/v1/tickets",
            new { teamId, type = "feature", title = "From the public API", body = "Created via ptk key" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await ReadAsync<TicketDto>(resp);
        ticket.CreatedBy.Should().Be(userId, "the ticket's created_by is the key owner");

        // A comment via the key (write) also works and is authored by the owner.
        var comment = await ReadAsync<CommentDto>(
            await key.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new { body = "via api" }));
        comment.AuthorId.Should().Be(userId);
    }

    // ---- Revocation is immediate ----

    [Fact]
    public async Task Revoked_key_is_immediately_401_on_next_use()
    {
        var (session, _, teamId) = await SetupUserWithTeamAsync();
        var created = await CreateKeyAsync(session, "tickets:read");
        var key = WithKey(created.Secret);

        (await key.GetAsync($"/api/v1/tickets?teamId={teamId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await session.DeleteAsync($"/api/me/api-keys/{created.Key.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await key.GetAsync($"/api/v1/tickets?teamId={teamId}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "a revoked key never authenticates again");
    }

    // ---- ptk_ token off /api/v1 → 401 (never an admin/session credential) ----

    [Fact]
    public async Task Api_key_is_rejected_off_v1_including_admin_and_session_routes()
    {
        var (session, _, _) = await SetupUserWithTeamAsync();
        var created = await CreateKeyAsync(session, "tickets:write");
        var key = WithKey(created.Secret);

        // A session route (no /api/v1 prefix) → 401 for a ptk_ token.
        (await key.GetAsync("/api/teams")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // The admin zone → 401 (never reachable by a key, even if the owner were admin).
        (await key.GetAsync("/api/admin/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- A session token is not accepted on the public /api/v1 surface ----

    [Fact]
    public async Task Session_token_is_rejected_on_v1()
    {
        var (session, _, teamId) = await SetupUserWithTeamAsync();
        // 'session' carries a session bearer; /api/v1 is the key-only surface.
        (await session.GetAsync($"/api/v1/tickets?teamId={teamId}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Another user's key id on revoke → 404 self-mask ----

    [Fact]
    public async Task Revoking_another_users_key_id_is_404_self_mask()
    {
        var (sessionA, _, _) = await SetupUserWithTeamAsync();
        var createdA = await CreateKeyAsync(sessionA, "tickets:read");

        var (tokenB, _, _) = await RegisterVerifiedUserAsync();
        var sessionB = Authed(tokenB);

        (await sessionB.DeleteAsync($"/api/me/api-keys/{createdA.Key.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "a user can only address their own keys (self-mask)");
    }

    // ---- Live authz: a key whose owner is not a team member cannot read that team ----

    [Fact]
    public async Task Key_reflects_owner_live_team_access_403_for_a_non_member_team()
    {
        // Owner is a plain member (not admin) belonging to team A only.
        var (tokenA, userA, _) = await RegisterMemberAsync();
        var sessionA = Authed(tokenA);

        // A second admin creates team B (owner A is not a member).
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "TeamB" }));

        var created = await CreateKeyAsync(sessionA, "tickets:read");
        var key = WithKey(created.Secret);

        (await key.GetAsync($"/api/v1/tickets?teamId={teamB.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "the key inherits the owner's live team access (owner not in team B)");
    }
}
