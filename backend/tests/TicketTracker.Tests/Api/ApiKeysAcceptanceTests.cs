using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite for API keys + the /api/v1 key surface (Wave 3, ADR-0021, §5.6/§7.3; test-guidance
/// §11 E). Presses the least-privilege + live-authz guarantees the smoke test only samples: a key can NEVER
/// delete (no v1 delete route; a session delete route is 401 for a key); a blocked owner's key stops
/// authenticating immediately; write implies read (a write key can GET); the scope gate fires on every write
/// verb (PUT/PATCH/comment-POST) for a read-only key → 403 insufficient_scope; revoke is idempotent; a bogus
/// ptk_ key is 401; last_used_at is recorded; an empty scopes list is 400. SEC-6 (PO decision: RESTRICT): a
/// key's team access is the owner's EXPLICIT memberships ONLY — an admin owner's key reaches only teams they
/// belong to and is DENIED others (the admin breadth never applies to key requests).
/// </summary>
public sealed class ApiKeysAcceptanceTests : IntegrationTestBase
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
        // SEC-6: a key's team access is EXPLICIT MEMBERSHIPS ONLY (admin breadth does not apply to keys).
        // Grant the owner an explicit membership so keys over this team authorize; the membership-only
        // denial cases are covered by the dedicated SEC-6 tests below.
        await AddMembershipAsync(userId, team.Id);
        return (session, userId, team.Id);
    }

    private async Task<CreateApiKeyResponseDto> CreateKeyAsync(HttpClient session, params string[] scopes)
        => await ReadAsync<CreateApiKeyResponseDto>(
            await session.PostAsJsonAsync("/api/me/api-keys", new { name = "CI pipeline", scopes }));

    private async Task<Guid> CreateTicketAsync(HttpClient session, Guid teamId)
        => (await ReadAsync<TicketDto>(await session.PostAsJsonAsync("/api/tickets",
            new { teamId, type = "bug", title = "Seed", body = "b" }))).Id;

    // ================= A key can NEVER delete (destructive surface excluded) =================

    [Fact]
    public async Task A_key_cannot_delete_a_ticket_via_any_route()
    {
        var (session, _, teamId) = await SetupUserWithTeamAsync();
        var ticketId = await CreateTicketAsync(session, teamId);
        var created = await CreateKeyAsync(session, "tickets:write");
        var key = WithKey(created.Secret);

        // There is no DELETE on the v1 surface — it must not resolve to a working delete (404/405, never 204).
        var v1Delete = await key.DeleteAsync($"/api/v1/tickets/{ticketId}");
        v1Delete.StatusCode.Should().NotBe(HttpStatusCode.NoContent, "delete is not a v1 capability");
        v1Delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        // The session delete route rejects a ptk_ token outright (keys are off-v1 → 401).
        (await key.DeleteAsync($"/api/tickets/{ticketId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The ticket still exists.
        await Factory.WithDbAsync(async db =>
            (await db.Tickets.CountAsync(t => t.Id == ticketId)).Should().Be(1));
    }

    // ================= The scope gate fires on every write verb for a read-only key =================

    [Fact]
    public async Task A_read_only_key_is_403_insufficient_scope_on_put_patch_and_comment_write()
    {
        var (session, _, teamId) = await SetupUserWithTeamAsync();
        var ticketId = await CreateTicketAsync(session, teamId);
        var created = await CreateKeyAsync(session, "tickets:read");
        var key = WithKey(created.Secret);

        var put = await key.PutAsJsonAsync($"/api/v1/tickets/{ticketId}",
            new { type = "bug", title = "Renamed", body = "b" });
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(put)).Code.Should().Be("insufficient_scope");

        var patch = await key.PatchAsJsonAsync($"/api/v1/tickets/{ticketId}/state", new { state = "in_progress" });
        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(patch)).Code.Should().Be("insufficient_scope");

        var comment = await key.PostAsJsonAsync($"/api/v1/tickets/{ticketId}/comments", new { body = "hi" });
        comment.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(comment)).Code.Should().Be("insufficient_scope");
    }

    // ================= write implies read: a write key can GET =================

    [Fact]
    public async Task A_write_scope_key_can_also_read()
    {
        var (session, _, teamId) = await SetupUserWithTeamAsync();
        var ticketId = await CreateTicketAsync(session, teamId);
        var created = await CreateKeyAsync(session, "tickets:write");
        var key = WithKey(created.Secret);

        (await key.GetAsync($"/api/v1/tickets?teamId={teamId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await key.GetAsync($"/api/v1/tickets/{ticketId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await key.GetAsync($"/api/v1/tickets/{ticketId}/comments")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ================= A blocked owner's key stops authenticating =================

    [Fact]
    public async Task A_key_stops_authenticating_when_its_owner_is_blocked()
    {
        var (session, userId, teamId) = await SetupUserWithTeamAsync();
        var created = await CreateKeyAsync(session, "tickets:read");
        var key = WithKey(created.Secret);

        (await key.GetAsync($"/api/v1/tickets?teamId={teamId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        await BlockUserAsync(userId);

        (await key.GetAsync($"/api/v1/tickets?teamId={teamId}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "a blocked owner's key must not authenticate (defence-in-depth §7.3)");
    }

    // ================= A key reflects the owner losing team access mid-life on the write path =================

    [Fact]
    public async Task A_key_loses_team_write_access_when_its_owner_is_removed_from_the_team()
    {
        // Owner is a plain member of team A (not admin).
        var (tokenA, userA, _) = await RegisterMemberAsync();
        var sessionA = Authed(tokenA);
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "TeamA" }));
        await AddMembershipAsync(userA, teamA.Id);

        var created = await CreateKeyAsync(sessionA, "tickets:write");
        var key = WithKey(created.Secret);

        // With membership the key can create a ticket in team A.
        (await key.PostAsJsonAsync("/api/v1/tickets",
            new { teamId = teamA.Id, type = "bug", title = "t", body = "b" })).StatusCode
            .Should().Be(HttpStatusCode.Created);

        // Remove the owner's membership; the key immediately loses access (live authz, no caching).
        await Factory.WithDbAsync(async db =>
        {
            var m = await db.UserTeams.Where(x => x.UserId == userA && x.TeamId == teamA.Id).ToListAsync();
            db.UserTeams.RemoveRange(m);
            await db.SaveChangesAsync();
        });

        (await key.PostAsJsonAsync("/api/v1/tickets",
            new { teamId = teamA.Id, type = "bug", title = "t2", body = "b" })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "the key inherits the owner's live team access");
    }

    // ================= SEC-6: an API key is membership-only, even for an ADMIN owner =================

    [Fact]
    public async Task An_admin_owners_key_reaches_only_explicitly_joined_teams_and_is_denied_others()
    {
        // The owner is an ADMIN (session admin sees all teams), and is an EXPLICIT member of exactly one team.
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var adminSession = Authed(adminToken);
        var joinedTeam = await ReadAsync<TeamDto>(await adminSession.PostAsJsonAsync("/api/teams", new { name = "Joined" }));
        var otherTeam = await ReadAsync<TeamDto>(await adminSession.PostAsJsonAsync("/api/teams", new { name = "NotJoined" }));
        await AddMembershipAsync(adminId, joinedTeam.Id); // explicit membership in ONE team only

        // Sanity: as a SESSION, the admin can read BOTH teams (admin breadth applies to sessions).
        (await adminSession.GetAsync($"/api/tickets?teamId={joinedTeam.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await adminSession.GetAsync($"/api/tickets?teamId={otherTeam.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await CreateKeyAsync(adminSession, "tickets:read");
        var key = WithKey(created.Secret);

        // Via the KEY: the explicitly-joined team is reachable...
        (await key.GetAsync($"/api/v1/tickets?teamId={joinedTeam.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK, "an API key reaches teams the owner explicitly belongs to");

        // ...but a team the owner is NOT an explicit member of is DENIED — the admin breadth does NOT apply to
        // API-key requests (SEC-6, PO decision: RESTRICT / least privilege).
        (await key.GetAsync($"/api/v1/tickets?teamId={otherTeam.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "an admin owner's key must NOT inherit admin breadth (SEC-6)");
    }

    [Fact]
    public async Task A_member_less_admin_owners_key_has_no_team_access()
    {
        // An admin who is a member of NO team: as a session they see everything; via a key they see nothing.
        var (adminToken, _, _) = await RegisterAdminAsync();
        var adminSession = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await adminSession.PostAsJsonAsync("/api/teams", new { name = "Orphan" }));

        var created = await CreateKeyAsync(adminSession, "tickets:read");
        var key = WithKey(created.Secret);

        (await key.GetAsync($"/api/v1/tickets?teamId={team.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden,
                "a member-less admin's key has no team access — the intended least-privilege outcome (SEC-6)");
    }

    // ================= Revoke is idempotent =================

    [Fact]
    public async Task Revoking_a_key_twice_is_idempotent_204()
    {
        var (session, _, _) = await SetupUserWithTeamAsync();
        var created = await CreateKeyAsync(session, "tickets:read");

        (await session.DeleteAsync($"/api/me/api-keys/{created.Key.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await session.DeleteAsync($"/api/me/api-keys/{created.Key.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent, "revoking an already-revoked key is a no-op");

        // The key remains listed (revoked, not deleted) with a revokedAt timestamp.
        var list = await ReadAsync<List<ApiKeyDto>>(await session.GetAsync("/api/me/api-keys"));
        list.Should().ContainSingle(k => k.Id == created.Key.Id && k.RevokedAt != null);
    }

    // ================= A bogus ptk_ key is 401 =================

    [Fact]
    public async Task A_bogus_ptk_key_is_401()
    {
        var (_, _, teamId) = await SetupUserWithTeamAsync();
        var key = WithKey("ptk_this_is_not_a_real_key_0000000000000000");
        (await key.GetAsync($"/api/v1/tickets?teamId={teamId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================= last_used_at is recorded on use =================

    [Fact]
    public async Task Using_a_key_records_last_used_at()
    {
        var (session, _, teamId) = await SetupUserWithTeamAsync();
        var created = await CreateKeyAsync(session, "tickets:read");
        created.Key.LastUsedAt.Should().BeNull("a freshly created key has never been used");

        var key = WithKey(created.Secret);
        (await key.GetAsync($"/api/v1/tickets?teamId={teamId}")).EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
        {
            var stored = await db.ApiKeys.SingleAsync(k => k.Id == created.Key.Id);
            stored.LastUsedAt.Should().NotBeNull("using the key records last_used_at (§7.3)");
            stored.LastUsedAt!.Value.Should().Be(Factory.Clock.UtcNow);
        });
    }

    // ================= Empty scopes → 400 keyed scopes =================

    [Fact]
    public async Task Create_with_empty_scopes_is_400_keyed_scopes()
    {
        var (session, _, _) = await SetupUserWithTeamAsync();
        var resp = await session.PostAsJsonAsync("/api/me/api-keys", new { name = "k", scopes = Array.Empty<string>() });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("scopes");
    }

    [Fact]
    public async Task Create_with_blank_name_is_400_keyed_name()
    {
        var (session, _, _) = await SetupUserWithTeamAsync();
        var resp = await session.PostAsJsonAsync("/api/me/api-keys", new { name = "   ", scopes = new[] { "tickets:read" } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("name");
    }
}
