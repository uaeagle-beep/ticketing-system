using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Exhaustive authorization matrix for the User Management feature (ADR-0007/0008,
/// USER_MANAGEMENT_DESIGN §3.4 endpoint table + §6 test strategy). This complements — and does NOT
/// duplicate — the developer's foundation <see cref="UserManagementTests"/>: it closes the gaps the
/// foundation set leaves open, with broken-access-control / IDOR (OWASP A01, R-1/R-2/R-3) as the
/// primary risk. Every assertion exercises real HTTP through the SQLite WebApplicationFactory;
/// expectations are taken from the SPECIFICATION, never reverse-engineered from the implementation.
///
/// Groups (mirroring the QA brief):
///   1) Admin-zone double gate over ALL 7 endpoints — member + anonymous.
///   2) Create-user: auto-gen vs chosen password, pre-verified/active, no email on chosen.
///   3) Last-admin guard: two-admin happy paths for demote AND block.
///   4) Block / unblock / reset interplay (the parts not already covered).
///   5) Team-scope / IDOR — the full read+write surface across tickets, epics, comments,
///      wip-limits, team rename/delete, plus 404-then-403 ordering and move-into-foreign-team.
///   6) Member team-list filtering vs admin-sees-all.
///   7) Anonymous (no token) on the protected surface → 401.
/// </summary>
public sealed class AuthorizationMatrixTests : IntegrationTestBase
{
    // The complete admin-only surface (§3.4 "New endpoints"). Method + path template; {id} is
    // substituted with a real user id at call time so the request reaches the service layer.
    public static IEnumerable<object[]> AdminEndpoints()
    {
        yield return new object[] { "GET", "/api/admin/users" };
        yield return new object[] { "POST", "/api/admin/users" };
        yield return new object[] { "PUT", "/api/admin/users/{id}/role" };
        yield return new object[] { "PUT", "/api/admin/users/{id}/teams" };
        yield return new object[] { "POST", "/api/admin/users/{id}/block" };
        yield return new object[] { "POST", "/api/admin/users/{id}/unblock" };
        yield return new object[] { "POST", "/api/admin/users/{id}/reset-password" };
    }

    private static HttpRequestMessage BuildAdminRequest(string method, string path, Guid targetId)
    {
        var resolved = path.Replace("{id}", targetId.ToString());
        var request = new HttpRequestMessage(new HttpMethod(method), resolved);
        // Provide a syntactically valid body for the mutating endpoints so a 403/401 cannot be a
        // 400/415 in disguise — the gate must reject BEFORE any body validation runs.
        if (method == "POST" && resolved == "/api/admin/users")
            request.Content = JsonContent.Create(new { email = "gate-probe@dataart.com", isAdmin = false });
        else if (resolved.EndsWith("/role"))
            request.Content = JsonContent.Create(new { isAdmin = true });
        else if (resolved.EndsWith("/teams"))
            request.Content = JsonContent.Create(new { teamIds = Array.Empty<Guid>() });
        else if (method == "POST")
            request.Content = JsonContent.Create(new { });
        return request;
    }

    // =========================================================== 1) Admin zone — double gate (R-3)

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task Member_on_every_admin_endpoint_is_403_forbidden(string method, string path)
    {
        var (memberToken, _, _) = await RegisterMemberAsync();
        // A real, existing target id so {id} routes resolve — the authz gate, not a 404, must win.
        var (_, targetId, _) = await RegisterMemberAsync();
        var member = Authed(memberToken);

        var resp = await member.SendAsync(BuildAdminRequest(method, path, targetId));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an authenticated non-admin must be denied every /api/admin/* endpoint (ADR-0007 §3.4)");
        (await ReadErrorAsync(resp)).Code.Should().Be("forbidden");
    }

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task Anonymous_on_every_admin_endpoint_is_401_unauthorized(string method, string path)
    {
        // No token at all on the shared (un-authed) client.
        var resp = await Client.SendAsync(BuildAdminRequest(method, path, Guid.NewGuid()));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an unauthenticated caller is rejected by the bearer middleware before the admin gate");
        (await ReadErrorAsync(resp)).Code.Should().Be("unauthorized");
    }

    [Fact]
    public async Task Admin_can_reach_every_admin_endpoint_successfully()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        // A non-admin target so block/role guards do not trip the last-admin invariant.
        var (_, targetId, _) = await RegisterMemberAsync();

        (await admin.GetAsync("/api/admin/users")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsJsonAsync("/api/admin/users",
                new { email = "fresh-admin-created@dataart.com", isAdmin = false }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.PutAsJsonAsync($"/api/admin/users/{targetId}/role", new { isAdmin = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/admin/users/{targetId}/teams", new { teamIds = Array.Empty<Guid>() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"/api/admin/users/{targetId}/block", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"/api/admin/users/{targetId}/unblock", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"/api/admin/users/{targetId}/reset-password", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =========================================================== 2) Create user (§4.3, UM-3/UM-5)

    [Fact]
    public async Task Create_with_chosen_password_returns_null_generated_password_and_sends_no_email()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var emailsBefore = Factory.Email.Sent.Count;

        var resp = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "chosen-only@dataart.com", password = "a-chosen-password-1", isAdmin = false });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ReadAsync<CreateUserResponseDto>(resp);
        body.GeneratedPassword.Should().BeNull("generatedPassword is null when the admin supplied the password (UM-5)");
        Factory.Email.Sent.Count.Should().Be(emailsBefore, "admin-created users never receive a verification email (UM-3)");
    }

    [Fact]
    public async Task Created_user_is_active_pre_verified_and_can_log_in_immediately_with_chosen_password()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        const string email = "immediate-login@dataart.com";
        const string password = "chosen-immediate-1";

        var resp = await admin.PostAsJsonAsync("/api/admin/users",
            new { email, password, isAdmin = false });
        var body = await ReadAsync<CreateUserResponseDto>(resp);
        body.User.EmailVerified.Should().BeTrue("admin-created accounts are pre-verified (UM-3)");
        body.User.IsBlocked.Should().BeFalse();
        body.User.Status.Should().Be("active");

        // No verify step required: the account logs in at once with the chosen password.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_with_generated_password_returns_it_once_and_it_is_a_working_credential()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        const string email = "gen-pass@dataart.com";

        var resp = await admin.PostAsJsonAsync("/api/admin/users",
            new { email, password = (string?)null, isAdmin = false });
        var body = await ReadAsync<CreateUserResponseDto>(resp);
        body.GeneratedPassword.Should().NotBeNullOrWhiteSpace("a server-generated password is returned exactly once (UM-5)");

        // The returned plaintext is the real credential.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password = body.GeneratedPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        // A different/empty password is rejected — proving the generated value is what was stored.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password = "not-the-generated-one" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================== 3) Last-admin guard happy paths

    [Fact]
    public async Task With_two_admins_blocking_one_succeeds()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var (_, secondAdminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        // Two active admins exist, so blocking one leaves one — allowed (INV-2).
        var resp = await admin.PostAsJsonAsync($"/api/admin/users/{secondAdminId}/block", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(resp)).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Blocking_the_other_admin_after_demoting_one_to_member_hits_the_last_admin_guard()
    {
        // Start with two admins; demote the second to member, leaving exactly one active admin; then
        // attempting to block the remaining admin must be rejected — guard counts ACTIVE admins (UM-1).
        var (firstToken, firstAdminId, _) = await RegisterAdminAsync();
        var (_, secondAdminId, _) = await RegisterAdminAsync();
        var admin = Authed(firstToken);

        (await admin.PutAsJsonAsync($"/api/admin/users/{secondAdminId}/role", new { isAdmin = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await admin.PostAsJsonAsync($"/api/admin/users/{firstAdminId}/block", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("last_admin_required");
    }

    // =========================================================== 4) Block / reset interplay extras

    [Fact]
    public async Task Block_is_idempotent_and_unblock_then_login_restores_access()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, memberEmail) = await RegisterMemberAsync();

        (await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/block", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        // Blocking again is a no-op success (idempotent, §4.6).
        var second = await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/block", new { });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(second)).IsBlocked.Should().BeTrue();

        (await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/unblock", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsJsonAsync("/api/auth/login", new { email = memberEmail, password = DefaultPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =========================================================== 5) Team-scope / IDOR (core risk)

    /// <summary>
    /// Builds two teams with content in team B, and a member belonging only to team A. Returns the
    /// member's authed client plus the ids the IDOR probes target.
    /// </summary>
    private async Task<(HttpClient Member, TeamDto TeamA, TeamDto TeamB, TicketDto TicketB, EpicDto EpicB)>
        SetupCrossTeamAsync()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Team Alpha" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Team Bravo" }));
        var epicB = await ReadAsync<EpicDto>(await admin.PostAsJsonAsync("/api/epics",
            new { teamId = teamB.Id, title = "B epic", description = (string?)null }));
        var ticketB = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = teamB.Id, type = "bug", title = "B secret", body = "confidential" }));

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id);
        return (Authed(memberToken), teamA, teamB, ticketB, epicB);
    }

    [Fact]
    public async Task Member_cross_team_ticket_reads_are_403()
    {
        var ctx = await SetupCrossTeamAsync();

        (await ctx.Member.GetAsync($"/api/tickets?teamId={ctx.TeamB.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "board read of a non-member team is forbidden");
        (await ctx.Member.GetAsync($"/api/tickets/{ctx.TicketB.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "ticket-by-id IDOR is forbidden (R-2)");
        (await ctx.Member.GetAsync($"/api/tickets/{ctx.TicketB.Id}/comments"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "reading another team's ticket comments is forbidden");
    }

    [Fact]
    public async Task Member_cross_team_ticket_writes_are_403()
    {
        var ctx = await SetupCrossTeamAsync();

        // PUT (full edit) a B ticket.
        (await ctx.Member.PutAsJsonAsync($"/api/tickets/{ctx.TicketB.Id}",
                new { teamId = ctx.TeamB.Id, type = "bug", title = "hijack", body = "x", state = "new", epicId = (Guid?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // PATCH state of a B ticket.
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/tickets/{ctx.TicketB.Id}/state")
        {
            Content = JsonContent.Create(new { state = "in_progress" })
        };
        (await ctx.Member.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // DELETE a B ticket.
        (await ctx.Member.DeleteAsync($"/api/tickets/{ctx.TicketB.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CREATE a comment on a B ticket.
        (await ctx.Member.PostAsJsonAsync($"/api/tickets/{ctx.TicketB.Id}/comments", new { body = "intruder" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_cross_team_epic_reads_and_writes_are_403()
    {
        var ctx = await SetupCrossTeamAsync();

        // List epics of B.
        (await ctx.Member.GetAsync($"/api/epics?teamId={ctx.TeamB.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Create epic in B.
        (await ctx.Member.PostAsJsonAsync("/api/epics",
                new { teamId = ctx.TeamB.Id, title = "intruder epic", description = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Edit a B epic by id (resolve-then-check).
        (await ctx.Member.PutAsJsonAsync($"/api/epics/{ctx.EpicB.Id}",
                new { title = "renamed", description = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Delete a B epic by id.
        (await ctx.Member.DeleteAsync($"/api/epics/{ctx.EpicB.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_cross_team_wip_limits_is_403()
    {
        var ctx = await SetupCrossTeamAsync();

        (await ctx.Member.PutAsJsonAsync($"/api/teams/{ctx.TeamB.Id}/wip-limits",
                new { wipLimits = new Dictionary<string, int?> { ["in_progress"] = 3 } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "setting another team's WIP limits is forbidden");
    }

    [Fact]
    public async Task Member_team_rename_and_delete_are_403_admin_only()
    {
        var ctx = await SetupCrossTeamAsync();

        // Even for the member's OWN team A, rename/delete are admin-only (§4.9).
        (await ctx.Member.PutAsJsonAsync($"/api/teams/{ctx.TeamA.Id}", new { name = "renamed by member" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ctx.Member.DeleteAsync($"/api/teams/{ctx.TeamA.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // And for a foreign team B too.
        (await ctx.Member.PutAsJsonAsync($"/api/teams/{ctx.TeamB.Id}", new { name = "renamed B" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ctx.Member.DeleteAsync($"/api/teams/{ctx.TeamB.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_cannot_move_a_ticket_into_a_foreign_team_via_epic_reassignment()
    {
        // A subtler move-into-foreign-team: keep teamId=A but point epicId at a B epic. The same-team
        // epic rule plus team-scope must still prevent leaking the ticket toward team B's data.
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Team A" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Team B" }));
        var epicB = await ReadAsync<EpicDto>(await admin.PostAsJsonAsync("/api/epics",
            new { teamId = teamB.Id, title = "B epic", description = (string?)null }));

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id);
        var member = Authed(memberToken);
        var ticketA = await ReadAsync<TicketDto>(await member.PostAsJsonAsync("/api/tickets",
            new { teamId = teamA.Id, type = "bug", title = "A", body = "x" }));

        // teamId stays A (accessible) but epicId references B → must NOT succeed (epic_team_mismatch 400,
        // never a 2xx that would attach the member's ticket to a foreign team's epic).
        var resp = await member.PutAsJsonAsync($"/api/tickets/{ticketA.Id}",
            new { teamId = teamA.Id, type = "bug", title = "A", body = "x", state = "new", priority = "medium", epicId = epicB.Id });
        resp.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "a member must not be able to reference another team's epic on their ticket");
        ((int)resp.StatusCode).Should().Be(400, "cross-team epic reference is rejected as epic_team_mismatch (V16)");
        (await ReadErrorAsync(resp)).Code.Should().Be("epic_team_mismatch");
    }

    [Fact]
    public async Task Comment_404_for_missing_ticket_then_403_for_other_team_ticket_ordering()
    {
        var ctx = await SetupCrossTeamAsync();

        // Genuinely missing ticket → 404 (the resource does not exist).
        (await ctx.Member.GetAsync($"/api/tickets/{Guid.NewGuid()}/comments"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Existing-but-foreign ticket → 403 (resolve-then-check ordering, §3.3).
        (await ctx.Member.GetAsync($"/api/tickets/{ctx.TicketB.Id}/comments"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Epic_edit_404_for_missing_then_403_for_other_team_epic_ordering()
    {
        var ctx = await SetupCrossTeamAsync();

        (await ctx.Member.PutAsJsonAsync($"/api/epics/{Guid.NewGuid()}",
                new { title = "x", description = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Member.PutAsJsonAsync($"/api/epics/{ctx.EpicB.Id}",
                new { title = "x", description = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_can_fully_operate_within_their_own_team()
    {
        // Positive counterpart so the 403s above are proven to be authorization, not a blanket denial.
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Owned" }));

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id);
        var member = Authed(memberToken);

        (await member.GetAsync($"/api/tickets?teamId={teamA.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetAsync($"/api/epics?teamId={teamA.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var ticket = await ReadAsync<TicketDto>(await member.PostAsJsonAsync("/api/tickets",
            new { teamId = teamA.Id, type = "feature", title = "mine", body = "ok" }));
        (await member.GetAsync($"/api/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "hi" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await member.PutAsJsonAsync($"/api/teams/{teamA.Id}/wip-limits",
                new { wipLimits = new Dictionary<string, int?> { ["in_progress"] = 5 } }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "WIP limits is M(team) — a member of the team may set them");
    }

    [Fact]
    public async Task Admin_can_read_and_write_any_team_regardless_of_membership()
    {
        var ctx = await SetupCrossTeamAsync();
        // Re-create an admin to act globally (the SetupCrossTeam admin token is not returned).
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        (await admin.GetAsync($"/api/tickets?teamId={ctx.TeamB.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/tickets/{ctx.TicketB.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/epics?teamId={ctx.TeamB.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/teams/{ctx.TeamB.Id}/wip-limits",
                new { wipLimits = new Dictionary<string, int?> { ["new"] = 9 } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =========================================================== 6) Team-list filtering (§4.9)

    [Fact]
    public async Task Member_team_list_shows_only_their_teams_while_admin_sees_all()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Visible A" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Hidden B" }));

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id);
        var member = Authed(memberToken);

        var memberTeams = await ReadAsync<List<TeamDto>>(await member.GetAsync("/api/teams"));
        memberTeams.Select(t => t.Id).Should().BeEquivalentTo(new[] { teamA.Id },
            "a member sees only the teams they belong to (ADR-0007)");
        memberTeams.Select(t => t.Id).Should().NotContain(teamB.Id);

        var adminTeams = await ReadAsync<List<TeamDto>>(await admin.GetAsync("/api/teams"));
        adminTeams.Select(t => t.Id).Should().Contain(new[] { teamA.Id, teamB.Id },
            "an admin sees all teams regardless of membership");
    }

    // =========================================================== 7) Anonymous on protected surface

    [Theory]
    [InlineData("GET", "/api/teams")]
    [InlineData("GET", "/api/auth/me")]
    public async Task Anonymous_on_authenticated_endpoints_is_401(string method, string path)
    {
        var resp = await Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(resp)).Code.Should().Be("unauthorized");
    }

    [Fact]
    public async Task Anonymous_on_team_scoped_board_is_401_not_403()
    {
        // No token → the bearer middleware rejects before any team-scope logic runs.
        var resp = await Client.GetAsync($"/api/tickets?teamId={Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(resp)).Code.Should().Be("unauthorized");
    }
}
