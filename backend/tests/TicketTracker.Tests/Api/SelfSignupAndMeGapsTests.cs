using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA gap-closing coverage for the self-signup default-team branch (req 8 / ASR-6) and the
/// <c>GET /api/auth/me</c> payload for members vs admins. The existing
/// <see cref="UserManagementTests"/> covers the POSITIVE default-team branch (a seeded "Demo Team")
/// and the admin /me shape; these close the complementary gaps the spec calls out but the developer's
/// tests do not: the MISSING-default-team branch (user ends with no teams, isAdmin=false, still
/// verified + able to log in) and the member /me shape (teams populated, isAdmin false, isBlocked
/// false). Expectations come from the SPECIFICATION (§3.4/§3.6/§4.9). Real HTTP, SQLite factory.
/// </summary>
public sealed class SelfSignupAndMeGapsTests : IntegrationTestBase
{
    // ============================================================== Self-signup: default team MISSING

    [Fact]
    public async Task Self_registered_user_with_default_team_disabled_verifies_but_joins_no_team()
    {
        // Auto-provisioning is OFF in this class (DEFAULT_SIGNUP_TEAM_NAME blank), the documented degrade
        // path (ADR-0011 step 1). The user still verifies successfully and simply ends up with no
        // membership (a warning is logged server-side); verification must NOT fail.
        const string email = "no-default-team@dataart.com";

        var signup = await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });
        signup.StatusCode.Should().Be(HttpStatusCode.Created);

        var token = Fakes.FakeEmailSender.ExtractToken(Factory.Email.LastFor(email)!.Link);
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        verify.StatusCode.Should().Be(HttpStatusCode.OK,
            "a missing default team does not block verification (req 8)");

        // The now-verified member can log in and is a team-less, non-admin member.
        var login = await Client.PostAsJsonAsync("/api/auth/login", new { email, password = DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<LoginDto>(login);
        body.User.IsAdmin.Should().BeFalse("a self-registered user is a member (ASR-6)");
        body.User.IsBlocked.Should().BeFalse();
        body.User.Teams.Should().NotBeNull();
        body.User.Teams!.Should().BeEmpty("with auto-provisioning disabled the verified user joins no team");
    }

    // The default-team AUTO-CREATE / auto-join positive branch (F-10, ADR-0011) lives in
    // DefaultTeamProvisioningTests, which runs with DEFAULT_SIGNUP_TEAM_NAME = "Demo Team".

    // ============================================================== /me — member vs admin shape (§3.6)

    [Fact]
    public async Task Me_for_a_member_returns_their_teams_isAdmin_false_and_isBlocked_false()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var teamA = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Alpha" }));
        var teamB = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Bravo" }));

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id, teamB.Id);
        var member = Authed(memberToken);

        var me = await ReadAsync<UserDto>(await member.GetAsync("/api/auth/me"));
        me.IsAdmin.Should().BeFalse();
        me.IsBlocked.Should().BeFalse();
        me.EmailVerified.Should().BeTrue();
        me.Teams!.Select(t => t.Id).Should().BeEquivalentTo(new[] { teamA.Id, teamB.Id },
            "a member's /me carries exactly their memberships (§3.6)");
    }

    [Fact]
    public async Task Me_for_an_admin_reports_isAdmin_true_and_does_not_leak_non_member_teams()
    {
        // An admin's teams[] reflects ONLY their own memberships (may be empty) — it is NOT "all teams".
        // The admin sees all teams via GET /api/teams, but /me.teams is membership, not global (§3.6).
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        await admin.PostAsJsonAsync("/api/teams", new { name = "Some Team" });

        var me = await ReadAsync<UserDto>(await admin.GetAsync("/api/auth/me"));
        me.IsAdmin.Should().BeTrue();
        me.Teams.Should().NotBeNull();
        me.Teams!.Should().BeEmpty("this admin has no memberships; /me.teams is membership, not all-teams (§3.6)");
    }
}
