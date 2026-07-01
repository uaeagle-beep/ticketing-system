using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Default-team auto-provisioning at self-signup verification (F-10, ADR-0011). Unlike the rest of the
/// integration suite (which runs with auto-provisioning OFF for a clean membership baseline), this class
/// configures <c>DEFAULT_SIGNUP_TEAM_NAME = "Demo Team"</c> so verify AUTO-CREATES the default team if it
/// is missing and grants membership, race-safely. These reconcile the pre-Wave-1 default-team tests to
/// the new contract (the team is created lazily at first verify, not seeded by an admin). The Tester owns
/// the fuller F-10 matrix incl. the concurrency race (WAVE1_DESIGN §8.F). Real HTTP, SQLite factory.
/// </summary>
public sealed class DefaultTeamProvisioningTests : IntegrationTestBase
{
    protected override string DefaultSignupTeamName => "Demo Team";

    [Fact]
    public async Task Self_registered_user_auto_creates_and_joins_the_default_team_on_verify()
    {
        // No admin pre-creates the team: the FIRST self-registration's verify auto-creates it (F-10).
        var (memberToken, _, _) = await RegisterMemberAsync("self@dataart.com");
        var member = Authed(memberToken);

        var me = await ReadAsync<UserDto>(await member.GetAsync("/api/auth/me"));
        me.IsAdmin.Should().BeFalse();
        me.IsBlocked.Should().BeFalse();
        me.Teams.Should().NotBeNull();
        me.Teams!.Should().ContainSingle(t => t.Name == "Demo Team",
            "verify auto-creates the default team and grants membership (F-10, ADR-0011)");
    }

    [Fact]
    public async Task Self_registered_user_joins_only_the_default_team_not_other_existing_teams()
    {
        // An admin exists (and is itself auto-joined to Demo Team at its own verify) and creates ANOTHER
        // team. A later self-registration must land ONLY in the default team, never in unrelated teams.
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var other = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Other Team" }));

        var (memberToken, _, _) = await RegisterMemberAsync("only-default@dataart.com");
        var member = Authed(memberToken);

        var me = await ReadAsync<UserDto>(await member.GetAsync("/api/auth/me"));
        me.Teams!.Should().ContainSingle(t => t.Name == "Demo Team",
            "membership is granted only in the default team (F-10)");
        me.Teams!.Select(t => t.Id).Should().NotContain(other.Id);
    }

    [Fact]
    public async Task Verifying_twice_does_not_duplicate_default_team_membership()
    {
        // The default team is auto-created on the first verify; a second verify is a no-op (single-use
        // token) so membership is never duplicated (INV-1 unique membership).
        var (memberToken, _, _) = await RegisterMemberAsync("dedupe@dataart.com");
        var member = Authed(memberToken);

        var me = await ReadAsync<UserDto>(await member.GetAsync("/api/auth/me"));
        me.Teams!.Where(t => t.Name == "Demo Team").Should().HaveCount(1,
            "the default-team membership is granted exactly once (INV-1 unique membership)");
    }

    // ============================================================== Convergence over the HTTP stack (§8.F)

    /// <summary>
    /// Two DIFFERENT self-registered users verify one after another while the default team starts ABSENT.
    /// The first verify auto-creates "Demo Team"; the second verify must find and JOIN that same team
    /// (not create a second one). Exactly one team exists afterwards and both users are members — the
    /// converged outcome the race must also reach. Real HTTP end-to-end.
    ///
    /// The genuinely-parallel TOCTOU (both verifies colliding on the unique index) is exercised
    /// deterministically at the service level in <see cref="DefaultTeamProvisioningRaceTests"/>, which
    /// uses a shared-cache multi-connection SQLite DB (the codebase's concurrency-test pattern,
    /// mirroring LastAdminGuardConcurrencyTests). Genuinely-parallel HTTP is not asserted here because
    /// the WebApplicationFactory shares ONE SQLite connection (two parallel transactions on a single
    /// connection is a harness limitation, not a product behaviour — see the QA report's honest gaps).
    /// </summary>
    [Fact]
    public async Task Two_sequential_verifications_by_different_users_converge_on_one_default_team()
    {
        // Confirm the default team is absent up front (no admin pre-created it).
        await Factory.WithDbAsync(async db =>
            (await db.Teams.AnyAsync(t => t.NameNormalized == "demo team")).Should().BeFalse());

        var (memberToken1, _, _) = await RegisterMemberAsync("converge1@dataart.com"); // creates the team
        var (memberToken2, _, _) = await RegisterMemberAsync("converge2@dataart.com"); // must join the same team

        var me1 = await ReadAsync<UserDto>(await Authed(memberToken1).GetAsync("/api/auth/me"));
        var me2 = await ReadAsync<UserDto>(await Authed(memberToken2).GetAsync("/api/auth/me"));
        me1.Teams!.Should().ContainSingle(t => t.Name == "Demo Team");
        me2.Teams!.Should().ContainSingle(t => t.Name == "Demo Team");

        await Factory.WithDbAsync(async db =>
        {
            var teams = await db.Teams.Where(t => t.NameNormalized == "demo team").ToListAsync();
            teams.Should().ContainSingle("both users converge on exactly one default team (ADR-0011)");
            var memberCount = await db.UserTeams.CountAsync(m => m.TeamId == teams[0].Id);
            memberCount.Should().Be(2, "both users are members of the single default team");
        });
    }
}
