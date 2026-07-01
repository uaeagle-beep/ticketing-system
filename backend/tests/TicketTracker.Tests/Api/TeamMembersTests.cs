using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// GET /api/teams/{id}/members — the member-visible picker endpoint (Wave-1 debt, WAVE2 §5.8 /
/// ADR-0017). Smoke coverage; the full suite is the Tester's. A member of team T (or an admin) gets
/// T's members (displayName + isAdmin), ordered by display name. A member of another team → 403;
/// an unknown team → 404; anonymous → 401.
/// </summary>
public sealed class TeamMembersTests : IntegrationTestBase
{
    private async Task<(HttpClient Admin, Guid AdminId, Guid TeamId)> SetupAsync(string teamName = "Platform")
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = teamName }));
        return (admin, adminId, team.Id);
    }

    private async Task<(Guid UserId, string Email, HttpClient Client)> AddMemberToTeamAsync(Guid teamId, string? email = null)
    {
        var (token, userId, resolvedEmail) = await RegisterMemberAsync(email);
        await AddMembershipAsync(userId, teamId);
        return (userId, resolvedEmail, Authed(token));
    }

    [Fact]
    public async Task Member_lists_team_members_with_displayName_and_isAdmin()
    {
        var (admin, _, teamId) = await SetupAsync();
        var (aliceId, aliceEmail, _) = await AddMemberToTeamAsync(teamId, "alice@dataart.com");
        var (bobId, bobEmail, bob) = await AddMemberToTeamAsync(teamId, "bob@dataart.com");

        // A plain member of the team can now list — the Wave-1 gap is closed.
        var members = await ReadAsync<List<TeamMemberDto>>(await bob.GetAsync($"/api/teams/{teamId}/members"));

        members.Select(m => m.Id).Should().BeEquivalentTo(new[] { aliceId, bobId });
        members.Should().OnlyContain(m => m.IsAdmin == false, "the two added users are plain members");
        members.Single(m => m.Id == aliceId).DisplayName.Should().Be(aliceEmail,
            "displayName falls back to email when name is null");
        members.Single(m => m.Id == bobId).DisplayName.Should().Be(bobEmail);
    }

    [Fact]
    public async Task Members_are_ordered_by_display_name()
    {
        var (admin, _, teamId) = await SetupAsync();
        await AddMemberToTeamAsync(teamId, "charlie@dataart.com");
        await AddMemberToTeamAsync(teamId, "alice@dataart.com");
        await AddMemberToTeamAsync(teamId, "bob@dataart.com");

        var members = await ReadAsync<List<TeamMemberDto>>(await admin.GetAsync($"/api/teams/{teamId}/members"));
        members.Select(m => m.DisplayName).Should().ContainInOrder(
            "alice@dataart.com", "bob@dataart.com", "charlie@dataart.com");
    }

    [Fact]
    public async Task Member_of_another_team_gets_403()
    {
        var (admin, _, teamId) = await SetupAsync();
        var otherTeam = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        var (_, _, outsider) = await AddMemberToTeamAsync(otherTeam.Id);

        var resp = await outsider.GetAsync($"/api/teams/{teamId}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_team_is_404()
    {
        var (admin, _, _) = await SetupAsync();
        var resp = await admin.GetAsync($"/api/teams/{Guid.NewGuid()}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        var (_, _, teamId) = await SetupAsync();
        var resp = await Client.GetAsync($"/api/teams/{teamId}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
