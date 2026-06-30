using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Foundation authorization tests for the User Management feature (ADR-0007/0008,
/// USER_MANAGEMENT_DESIGN §6). These prove the core admin-zone, team-scope, block, and last-admin
/// paths end-to-end over real HTTP; the exhaustive table-driven authz matrix is the tester's to
/// extend. The default <see cref="IntegrationTestBase.RegisterVerifiedUserAsync"/> principal is an
/// admin (design §6.3); members are created with <see cref="IntegrationTestBase.RegisterMemberAsync"/>
/// / <see cref="IntegrationTestBase.RegisterMemberInTeamAsync"/>.
/// </summary>
public sealed class UserManagementTests : IntegrationTestBase
{
    // ============================================================== Admin zone — privilege gate

    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/admin/users")]
    public async Task Member_hitting_admin_zone_is_403_forbidden(string method, string path)
    {
        var (token, _, _) = await RegisterMemberAsync();
        var client = Authed(token);

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new { email = "x@dataart.com", isAdmin = false });

        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(resp)).Code.Should().Be("forbidden");
    }

    [Fact]
    public async Task Admin_lists_all_users()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var (_, memberId, _) = await RegisterMemberAsync();
        var admin = Authed(adminToken);

        var users = await ReadAsync<List<AdminUserDto>>(await admin.GetAsync("/api/admin/users"));
        users.Select(u => u.Id).Should().Contain(new[] { adminId, memberId });
        users.Single(u => u.Id == memberId).Status.Should().Be("active");
        users.Single(u => u.Id == memberId).IsAdmin.Should().BeFalse();
    }

    // ============================================================== Create user

    [Fact]
    public async Task Admin_creates_user_with_generated_password_and_that_user_can_log_in()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "newdev@dataart.com", password = (string?)null, isAdmin = false });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync<CreateUserResponseDto>(create);
        body.GeneratedPassword.Should().NotBeNullOrWhiteSpace("a server-generated password is returned once");
        body.User.EmailVerified.Should().BeTrue("admin-created users are pre-verified (UM-3)");
        body.User.Status.Should().Be("active");

        // The created user can log in immediately with the generated password.
        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "newdev@dataart.com", password = body.GeneratedPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_creates_user_with_chosen_password_and_teams_no_generated_password_returned()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));

        // Count emails BEFORE: the admin's own registration already captured one verification email.
        var emailsBefore = Factory.Email.Sent.Count;

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "chosen@dataart.com", password = "chosen-password-123", isAdmin = false, teamIds = new[] { team.Id } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync<CreateUserResponseDto>(create);
        body.GeneratedPassword.Should().BeNull("no password is returned when the admin supplied one");
        body.User.Teams.Should().ContainSingle(t => t.Id == team.Id);
        Factory.Email.Sent.Count.Should().Be(emailsBefore, "admin-created users get no verification email (UM-3)");
    }

    [Fact]
    public async Task Admin_create_with_duplicate_email_is_409_email_in_use()
    {
        var (adminToken, _, existingEmail) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = existingEmail, isAdmin = false });
        create.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(create)).Code.Should().Be("email_in_use");
    }

    [Fact]
    public async Task Admin_create_with_unknown_team_is_400_validation_error()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "withteam@dataart.com", isAdmin = false, teamIds = new[] { Guid.NewGuid() } });
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(create)).Errors.Should().ContainKey("teamIds");
    }

    // ============================================================== Role + last-admin guard

    [Fact]
    public async Task Promoting_a_member_to_admin_grants_them_all_teams_in_the_team_list()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" });
        await admin.PostAsJsonAsync("/api/teams", new { name = "Payments" });

        var (memberToken, memberId, _) = await RegisterMemberAsync();
        var member = Authed(memberToken);

        // Before promotion: a team-less member sees no teams.
        (await ReadAsync<List<TeamDto>>(await member.GetAsync("/api/teams"))).Should().BeEmpty();

        var promote = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/role", new { isAdmin = true });
        promote.StatusCode.Should().Be(HttpStatusCode.OK);

        // After promotion: the now-admin sees ALL teams.
        (await ReadAsync<List<TeamDto>>(await member.GetAsync("/api/teams"))).Should().HaveCount(2);
    }

    [Fact]
    public async Task Demoting_the_last_active_admin_is_409_last_admin_required()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/role", new { isAdmin = false });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("last_admin_required");
    }

    [Fact]
    public async Task With_two_admins_demoting_one_succeeds()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var (_, secondAdminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{secondAdminId}/role", new { isAdmin = false });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(resp)).IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Blocking_the_last_active_admin_is_409_last_admin_required()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PostAsJsonAsync($"/api/admin/users/{adminId}/block", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("last_admin_required");
    }

    // ============================================================== Block / unblock

    [Fact]
    public async Task Blocked_user_cannot_log_in_then_unblocked_user_can()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, memberEmail) = await RegisterMemberAsync();

        (await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/block", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var blockedLogin = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = memberEmail, password = DefaultPassword });
        blockedLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(blockedLogin)).Code.Should().Be("account_blocked");

        (await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/unblock", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUnblock = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = memberEmail, password = DefaultPassword });
        afterUnblock.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Blocking_kills_existing_sessions_immediately()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (memberToken, memberId, _) = await RegisterMemberAsync();
        var member = Authed(memberToken);

        // Token works before block.
        (await member.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/block", new { });

        // The same token is rejected immediately after block (ASR-2).
        (await member.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reset_password_returns_once_invalidates_sessions_and_new_password_works()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (memberToken, memberId, memberEmail) = await RegisterMemberAsync();
        var member = Authed(memberToken);

        var reset = await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/reset-password", new { });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var newPassword = (await ReadAsync<ResetPasswordResponseDto>(reset)).GeneratedPassword;
        newPassword.Should().NotBeNullOrWhiteSpace();

        // Old session is dead.
        (await member.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Old password no longer works; the new one does.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email = memberEmail, password = DefaultPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client.PostAsJsonAsync("/api/auth/login", new { email = memberEmail, password = newPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_password_on_a_blocked_user_is_403_forbidden()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, _) = await RegisterMemberAsync();
        await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/block", new { });

        var resp = await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/reset-password", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(resp)).Code.Should().Be("forbidden");
    }

    // ============================================================== Team-scope / IDOR

    [Fact]
    public async Task Member_cross_team_read_and_write_is_403_forbidden()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "TeamA" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "TeamB" }));
        // A ticket in team B (created by the admin who can access everything).
        var ticketB = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = teamB.Id, type = "bug", title = "B-secret", body = "x" }));

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id);
        var member = Authed(memberToken);

        // Read board of B → 403.
        (await member.GetAsync($"/api/tickets?teamId={teamB.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Read a ticket in B by id → 403 (IDOR).
        (await member.GetAsync($"/api/tickets/{ticketB.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Read epics of B → 403.
        (await member.GetAsync($"/api/epics?teamId={teamB.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Create a ticket in B → 403.
        (await member.PostAsJsonAsync("/api/tickets",
            new { teamId = teamB.Id, type = "bug", title = "x", body = "y" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Member CAN read their own team A board.
        (await member.GetAsync($"/api/tickets?teamId={teamA.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Member_404_for_missing_id_403_for_other_team_id_ordering()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "TeamA" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "TeamB" }));
        var ticketB = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = teamB.Id, type = "bug", title = "B", body = "x" }));

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id);
        var member = Authed(memberToken);

        (await member.GetAsync($"/api/tickets/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.GetAsync($"/api/tickets/{ticketB.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_team_create_is_403_forbidden()
    {
        var (memberToken, _, _) = await RegisterMemberAsync();
        var member = Authed(memberToken);

        var resp = await member.PostAsJsonAsync("/api/teams", new { name = "Sneaky" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(resp)).Code.Should().Be("forbidden");
    }

    [Fact]
    public async Task Member_cannot_move_a_ticket_into_a_team_they_do_not_belong_to()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "TeamA" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "TeamB" }));

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id);
        var member = Authed(memberToken);
        var ticketA = await ReadAsync<TicketDto>(await member.PostAsJsonAsync("/api/tickets",
            new { teamId = teamA.Id, type = "bug", title = "A", body = "x" }));

        var resp = await member.PutAsJsonAsync($"/api/tickets/{ticketA.Id}",
            new { teamId = teamB.Id, type = "bug", title = "A", body = "x", state = "new", epicId = (Guid?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ============================================================== Self-signup default team + /me

    [Fact]
    public async Task Self_registered_user_joins_the_default_team_on_verify_and_me_reflects_it()
    {
        // Seed a "Demo Team" (the configured default) via an admin first.
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var demo = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Demo Team" }));

        // A brand-new self-registration → verify → login (a MEMBER).
        var (memberToken, _, _) = await RegisterMemberAsync("self@dataart.com");
        var member = Authed(memberToken);

        var me = await ReadAsync<UserDto>(await member.GetAsync("/api/auth/me"));
        me.IsAdmin.Should().BeFalse();
        me.IsBlocked.Should().BeFalse();
        me.Teams.Should().NotBeNull();
        me.Teams!.Should().ContainSingle(t => t.Id == demo.Id, "verify grants the default-team membership (req 8)");
    }

    [Fact]
    public async Task Me_returns_isAdmin_isBlocked_and_teams()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var me = await ReadAsync<UserDto>(await admin.GetAsync("/api/auth/me"));
        me.IsAdmin.Should().BeTrue();
        me.IsBlocked.Should().BeFalse();
        me.Teams.Should().NotBeNull();
    }
}
