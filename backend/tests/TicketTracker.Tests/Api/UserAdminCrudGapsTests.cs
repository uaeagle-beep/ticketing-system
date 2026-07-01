using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA gap-closing coverage for the admin user-CRUD surface (API_CONTRACT §8, USER_MANAGEMENT_DESIGN
/// §4). These target behaviours the developer's <see cref="UserManagementTests"/> /
/// <see cref="AuthorizationMatrixTests"/> / <see cref="UserNameTests"/> do NOT already assert — chiefly
/// the full PUT /teams set-semantics, set-role/create validation and idempotency, 404-for-unknown-user
/// on every mutating endpoint, and the admin-list field completeness that the client-side filter
/// depends on. Expectations are taken from the SPECIFICATION, not the implementation. All calls go
/// over real HTTP through the SQLite WebApplicationFactory.
/// </summary>
public sealed class UserAdminCrudGapsTests : IntegrationTestBase
{
    // ============================================================== PUT /teams — set semantics (§4.4)

    [Fact]
    public async Task Set_teams_replaces_the_full_membership_set_add_then_remove()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Alpha" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Bravo" }));
        var teamC = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Charlie" }));
        var (_, memberId, _) = await RegisterMemberAsync();

        // Assign A + B.
        var setAB = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams",
            new { teamIds = new[] { teamA.Id, teamB.Id } });
        setAB.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(setAB)).Teams.Select(t => t.Id)
            .Should().BeEquivalentTo(new[] { teamA.Id, teamB.Id });

        // Replace with B + C — A must be removed, C added (authoritative full-set semantics, §4.4).
        var setBC = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams",
            new { teamIds = new[] { teamB.Id, teamC.Id } });
        setBC.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(setBC)).Teams.Select(t => t.Id)
            .Should().BeEquivalentTo(new[] { teamB.Id, teamC.Id },
                "the request is the authoritative full membership set — A is dropped, C is added");
    }

    [Fact]
    public async Task Set_teams_with_empty_set_clears_all_memberships()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Alpha" }));
        var (_, memberId, _) = await RegisterMemberAsync();
        await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams", new { teamIds = new[] { teamA.Id } });

        var cleared = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams",
            new { teamIds = Array.Empty<Guid>() });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(cleared)).Teams.Should().BeEmpty("an empty set clears all memberships (§4.4)");
    }

    [Fact]
    public async Task Set_teams_with_null_list_clears_all_memberships()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Alpha" }));
        var (_, memberId, _) = await RegisterMemberAsync();
        await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams", new { teamIds = new[] { teamA.Id } });

        var cleared = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams",
            new { teamIds = (Guid[]?)null });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(cleared)).Teams.Should().BeEmpty("a null list means no teams (§4.4)");
    }

    [Fact]
    public async Task Set_teams_is_idempotent_when_the_same_set_is_submitted_twice()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Alpha" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Bravo" }));
        var (_, memberId, _) = await RegisterMemberAsync();

        var first = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams",
            new { teamIds = new[] { teamA.Id, teamB.Id } });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Submitting the same set again is a no-op success with the identical membership (§4.4 idempotent).
        var again = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams",
            new { teamIds = new[] { teamB.Id, teamA.Id } });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(again)).Teams.Select(t => t.Id)
            .Should().BeEquivalentTo(new[] { teamA.Id, teamB.Id });
    }

    [Fact]
    public async Task Set_teams_de_duplicates_repeated_ids()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Alpha" }));
        var (_, memberId, _) = await RegisterMemberAsync();

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams",
            new { teamIds = new[] { teamA.Id, teamA.Id, teamA.Id } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(resp)).Teams.Should().ContainSingle(t => t.Id == teamA.Id,
            "duplicate ids collapse to one membership (§4.4)");
    }

    [Fact]
    public async Task Set_teams_with_an_unknown_team_is_400_validation_error_keyed_teamIds()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, _) = await RegisterMemberAsync();

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams",
            new { teamIds = new[] { Guid.NewGuid() } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("teamIds");
    }

    [Fact]
    public async Task Set_teams_on_an_unknown_user_is_404_not_found()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/teams",
            new { teamIds = Array.Empty<Guid>() });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorAsync(resp)).Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Set_teams_does_not_change_the_admin_flag()
    {
        // §4.4: "Does not affect isAdmin". An admin with zero teams is still global.
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/teams",
            new { teamIds = Array.Empty<Guid>() });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await ReadAsync<AdminUserDto>(resp);
        dto.IsAdmin.Should().BeTrue("changing teams leaves the admin flag untouched (§4.4)");
        dto.Teams.Should().BeEmpty();
    }

    [Fact]
    public async Task Set_teams_grants_board_access_reflected_on_the_next_request()
    {
        // End-to-end: assigning a team via PUT /teams gives the member real access to that team's board.
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Alpha" }));

        var (memberToken, memberId, _) = await RegisterMemberAsync();
        var member = Authed(memberToken);

        // Before assignment: board of A is forbidden (team-less member).
        (await member.GetAsync($"/api/tickets?teamId={teamA.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams", new { teamIds = new[] { teamA.Id } });

        // After assignment: the same token now reads A's board (fresh membership load, ADR-0007).
        (await member.GetAsync($"/api/tickets?teamId={teamA.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ============================================================== Set role — idempotency + 404 (§4.3)

    [Fact]
    public async Task Promoting_an_already_admin_is_a_200_no_op()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var (_, secondAdminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{secondAdminId}/role", new { isAdmin = true });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "setting the same value is an idempotent no-op success (§4.3)");
        (await ReadAsync<AdminUserDto>(resp)).IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Demoting_an_already_member_is_a_200_no_op()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, _) = await RegisterMemberAsync();

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/role", new { isAdmin = false });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "demoting a user who is already a member is a no-op (§4.3)");
        (await ReadAsync<AdminUserDto>(resp)).IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Set_role_on_an_unknown_user_is_404_not_found()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/role", new { isAdmin = true });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorAsync(resp)).Code.Should().Be("not_found");
    }

    // ============================================================== Create — validation + isAdmin (§4.3)

    [Fact]
    public async Task Create_with_an_invalid_email_format_is_400_validation_error_keyed_email()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "not-an-email", isAdmin = false });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("email");
    }

    [Fact]
    public async Task Create_with_a_blank_email_is_400_validation_error_keyed_email()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "   ", isAdmin = false });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("email");
    }

    [Fact]
    public async Task Create_with_a_supplied_password_shorter_than_min_is_400_validation_error_keyed_password()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        // A non-blank password shorter than the min (8) must be rejected (UM-4; §4.3 "≥ 8").
        var resp = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "short-pw@dataart.com", password = "1234567", isAdmin = false });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("password");
    }

    [Fact]
    public async Task Create_with_a_supplied_password_longer_than_max_is_400_validation_error_keyed_password()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        // > 1024 chars must be rejected (UM-4; §4.3 "≤ 1024", Argon2id DoS guard).
        var resp = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "long-pw@dataart.com", password = new string('a', 1025), isAdmin = false });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("password");
    }

    [Fact]
    public async Task Create_with_isAdmin_true_makes_a_working_admin_with_immediate_admin_zone_access()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        const string email = "born-admin@dataart.com";
        const string password = "chosen-admin-pw-1";

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email, password, isAdmin = true });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync<CreateUserResponseDto>(create)).User.IsAdmin.Should().BeTrue();

        // The freshly-created admin can log in AND immediately reach the admin zone (double-gate passes).
        var login = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAdmin = Authed((await ReadAsync<LoginDto>(login)).Token);
        (await newAdmin.GetAsync("/api/admin/users")).StatusCode.Should().Be(HttpStatusCode.OK,
            "an isAdmin=true created account has admin-zone access at once (§4.3)");
    }

    [Fact]
    public async Task Create_member_with_empty_team_list_succeeds_with_no_teams()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "teamless@dataart.com", isAdmin = false, teamIds = Array.Empty<Guid>() });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync<CreateUserResponseDto>(create);
        body.User.IsAdmin.Should().BeFalse();
        body.User.Teams.Should().BeEmpty("teamIds=[] yields a member with no teams (§4.3)");
    }

    // ============================================================== Block / unblock — 404 + visibility (§4.5/4.6)

    [Fact]
    public async Task Block_on_an_unknown_user_is_404_not_found()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PostAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/block", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorAsync(resp)).Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Unblock_on_an_unknown_user_is_404_not_found()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PostAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/unblock", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reset_password_on_an_unknown_user_is_404_not_found()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PostAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/reset-password", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unblock_is_idempotent_on_a_not_blocked_user()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, _) = await RegisterMemberAsync();

        // Unblocking a user who was never blocked is a no-op success (§4.6 idempotent).
        var resp = await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/unblock", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(resp)).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Blocked_users_details_remain_visible_to_admins_in_the_list()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, memberEmail) = await RegisterMemberAsync();
        await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/block", new { });

        // A block is a reversible access state, NOT a deletion (UM-7): the account stays in the list.
        var users = await ReadAsync<List<AdminUserDto>>(await admin.GetAsync("/api/admin/users"));
        var blocked = users.SingleOrDefault(u => u.Id == memberId);
        blocked.Should().NotBeNull("a blocked user's data remains visible to admins");
        blocked!.IsBlocked.Should().BeTrue();
        blocked.Status.Should().Be("blocked");
        blocked.Email.Should().Be(memberEmail);
    }

    // ============================================================== Blocked cannot regain access (ASR-2)

    [Fact]
    public async Task Blocked_unverified_user_cannot_resend_verification_to_regain_access()
    {
        // Signup (unverified) then block WITHOUT verifying. Resend must stay non-committal (202) and
        // issue NO usable token — a blocked user cannot use verification to regain access (ASR-2, §3.5).
        const string email = "blocked-unverified@dataart.com";
        await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });

        // Block the freshly-signed-up (still unverified) account directly in persistence.
        await Factory.WithDbAsync(async db =>
        {
            var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstAsync(db.Users, u => u.EmailNormalized == email);
            user.IsBlocked = true;
            await db.SaveChangesAsync();
        });

        var emailsBefore = Factory.Email.Sent.Count;
        var resend = await Client.PostAsJsonAsync("/api/auth/resend-verification", new { email });
        resend.StatusCode.Should().Be(HttpStatusCode.Accepted, "resend is always non-committal 202 (A8)");
        Factory.Email.Sent.Count.Should().Be(emailsBefore,
            "no verification email is issued to a blocked account (ASR-2)");
    }

    [Fact]
    public async Task Blocked_user_login_reports_blocked_even_when_also_unverified()
    {
        // §4.9 ordering: blocked is checked BEFORE unverified, so a blocked-and-unverified account
        // reports account_blocked (401), never account_not_verified (403).
        const string email = "blocked-and-unverified@dataart.com";
        await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });
        await Factory.WithDbAsync(async db =>
        {
            var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstAsync(db.Users, u => u.EmailNormalized == email);
            user.IsBlocked = true;
            await db.SaveChangesAsync();
        });

        var login = await Client.PostAsJsonAsync("/api/auth/login", new { email, password = DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(login)).Code.Should().Be("account_blocked",
            "blocked is evaluated before the unverified branch (§4.9)");
    }

    // ============================================================== GET /admin/users — field completeness

    [Fact]
    public async Task Admin_list_returns_every_field_the_client_side_filter_relies_on()
    {
        // The Users screen filters CLIENT-SIDE over this list (§8.1 "Filtering"), so the payload must
        // carry name, email, isAdmin, teams, emailVerified, isBlocked, createdAt and the derived status
        // for EVERY filter dimension to work. Assert the full projected shape on a rich user.
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/name", new { name = "Filterable Admin" });
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));

        // A member on a team so the teams[] projection is non-trivial.
        var (_, memberId, memberEmail) = await RegisterMemberAsync();
        await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/teams", new { teamIds = new[] { team.Id } });

        var users = await ReadAsync<List<AdminUserDto>>(await admin.GetAsync("/api/admin/users"));

        var adminRow = users.Single(u => u.Id == adminId);
        adminRow.Name.Should().Be("Filterable Admin");
        adminRow.IsAdmin.Should().BeTrue();
        adminRow.EmailVerified.Should().BeTrue();
        adminRow.IsBlocked.Should().BeFalse();
        adminRow.Status.Should().Be("active");
        adminRow.CreatedAt.Should().NotBe(default);

        var memberRow = users.Single(u => u.Id == memberId);
        memberRow.Email.Should().Be(memberEmail);
        memberRow.IsAdmin.Should().BeFalse();
        memberRow.Teams.Should().ContainSingle(t => t.Id == team.Id && t.Name == "Platform",
            "the teams[] projection carries id + name for the team filter");
        memberRow.Status.Should().Be("active");
    }

    [Fact]
    public async Task Admin_list_is_ordered_by_created_at_ascending()
    {
        // §4.2/§8.1: stable order by created_at asc. Advance the clock between registrations so order is
        // deterministic, then assert the list is non-descending by CreatedAt.
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var (_, secondId, _) = await RegisterMemberAsync();
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var (_, thirdId, _) = await RegisterMemberAsync();

        var users = await ReadAsync<List<AdminUserDto>>(await admin.GetAsync("/api/admin/users"));
        var ordered = users.OrderBy(u => u.CreatedAt).Select(u => u.Id).ToList();
        users.Select(u => u.Id).Should().Equal(ordered, "the admin list is ordered by createdAt asc (§4.2)");
        users.Select(u => u.Id).Should().Contain(new[] { adminId, secondId, thirdId });
    }
}
