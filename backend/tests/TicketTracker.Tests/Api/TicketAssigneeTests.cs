using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// F-02 Multiple assignees (WAVE1_DESIGN §8.B, ADR-0009 §B). Full-set replace via
/// <c>PUT /api/tickets/{id}/assignees</c>. Eligibility = team members ∪ admins; an unknown OR
/// ineligible user id ⇒ 400 keyed <c>userIds</c> (a bad body reference → 400, NOT 403). The set is
/// de-duplicated; a full replace removes dropped assignees; a no-op set does NOT bump modified_at
/// (assignment is metadata, §4.2). Board filters: <c>assigneeId=</c> and <c>assignedToMe=true</c>
/// (assignedToMe wins if both are sent). IDOR: a member of team A cannot PUT assignees on a team B
/// ticket ⇒ 403 (caller access checked BEFORE payload — 404-then-403 ordering). Deleting a ticket
/// cascades its ticket_assignees (FK CASCADE under SQLite). Real HTTP over the SQLite factory.
/// </summary>
public sealed class TicketAssigneeTests : IntegrationTestBase
{
    // A world with an admin owner (creates the team), the ticket, and N members added to the team.
    private sealed record World(
        HttpClient Admin, Guid AdminUserId, Guid TeamId, TicketDto Ticket);

    private async Task<World> SetupAsync(string teamName = "Platform")
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = teamName }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "T", body = "B" }));
        return new World(admin, adminId, team.Id, ticket);
    }

    /// <summary>Create a verified member and add them to the given team; returns (userId, email).</summary>
    private async Task<(Guid UserId, string Email)> AddMemberToTeamAsync(Guid teamId, string? email = null)
    {
        var (_, userId, resolvedEmail) = await RegisterMemberAsync(email);
        await AddMembershipAsync(userId, teamId);
        return (userId, resolvedEmail);
    }

    private static async Task<int> CountAssigneeRowsAsync(CustomWebApplicationFactory factory, Guid ticketId)
    {
        var count = 0;
        await factory.WithDbAsync(async db =>
        {
            count = await db.TicketAssignees.CountAsync(a => a.TicketId == ticketId);
        });
        return count;
    }

    // ---------------- Set: happy path ----------------

    [Fact]
    public async Task Set_assignees_to_team_members_returns_200_with_correct_assignees()
    {
        var w = await SetupAsync();
        var (m1, e1) = await AddMemberToTeamAsync(w.TeamId);
        var (m2, e2) = await AddMemberToTeamAsync(w.TeamId);

        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees",
            new { userIds = new[] { m1, m2 } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await ReadAsync<TicketDto>(resp);
        detail.Assignees.Should().NotBeNull();
        detail.Assignees!.Select(a => a.Id).Should().BeEquivalentTo(new[] { m1, m2 });
        detail.Assignees!.Select(a => a.DisplayName).Should().BeEquivalentTo(new[] { e1, e2 },
            "displayName falls back to email when name is null (§4.2)");
    }

    [Fact]
    public async Task Assignee_displayName_uses_name_when_set()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        // The member sets their own display name via /api/me/profile (self-service, F-04).
        // Re-login as that member to get a token; simpler: set the name directly in the DB.
        await Factory.WithDbAsync(async db =>
        {
            var u = await db.Users.FirstAsync(x => x.Id == m1);
            u.Name = "Alex Doe";
            await db.SaveChangesAsync();
        });

        var detail = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync(
            $"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1 } }));
        detail.Assignees!.Single().DisplayName.Should().Be("Alex Doe");
    }

    [Fact]
    public async Task Admin_non_member_can_be_assigned()
    {
        var w = await SetupAsync();
        // A SECOND admin who is NOT a member of the team — eligible because admins ∈ eligible set (§4.2).
        var (_, otherAdminId, _) = await RegisterAdminAsync("other-admin@dataart.com");

        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees",
            new { userIds = new[] { otherAdminId } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<TicketDto>(resp)).Assignees!.Single().Id.Should().Be(otherAdminId,
            "a non-member admin is assignable (ASSUMPTION W1-ASSIGN-ELIGIBILITY)");
    }

    // ---------------- Set: validation (400 keyed userIds) ----------------

    [Fact]
    public async Task Assign_a_non_member_is_400_keyed_userIds()
    {
        var w = await SetupAsync();
        // A verified plain member who is NOT in the ticket's team and NOT an admin.
        var (_, outsiderId, _) = await RegisterMemberAsync("outsider@dataart.com");

        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees",
            new { userIds = new[] { outsiderId } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an ineligible user id is a bad body reference → 400, not 403 (§4.2, ADR-0006 §B)");
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("userIds");
    }

    [Fact]
    public async Task Assign_an_unknown_user_id_is_400_keyed_userIds()
    {
        var w = await SetupAsync();
        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees",
            new { userIds = new[] { Guid.NewGuid() } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("userIds");
    }

    [Fact]
    public async Task Mixed_eligible_and_ineligible_ids_reject_the_whole_set_and_persist_nothing()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        var (_, outsiderId, _) = await RegisterMemberAsync("outsider2@dataart.com");

        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees",
            new { userIds = new[] { m1, outsiderId } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Atomicity: a rejected set must not have partially persisted the eligible member.
        (await CountAssigneeRowsAsync(Factory, w.Ticket.Id)).Should().Be(0,
            "a rejected assignee set persists nothing");
    }

    // ---------------- De-dup, full-replace, clear ----------------

    [Fact]
    public async Task Duplicate_ids_in_the_request_are_deduplicated()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);

        var detail = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync(
            $"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1, m1, m1 } }));
        detail.Assignees!.Should().ContainSingle(a => a.Id == m1);
        (await CountAssigneeRowsAsync(Factory, w.Ticket.Id)).Should().Be(1, "no double-assign (INV-W1)");
    }

    [Fact]
    public async Task Full_replace_removes_dropped_assignees_and_adds_new_ones()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        var (m2, _) = await AddMemberToTeamAsync(w.TeamId);
        var (m3, _) = await AddMemberToTeamAsync(w.TeamId);

        // Initial set {m1, m2}.
        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1, m2 } });
        // Replace with {m2, m3}: m1 dropped, m3 added, m2 kept.
        var detail = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync(
            $"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m2, m3 } }));

        detail.Assignees!.Select(a => a.Id).Should().BeEquivalentTo(new[] { m2, m3 });
        detail.Assignees!.Select(a => a.Id).Should().NotContain(m1, "full replace drops the absent m1");
    }

    [Fact]
    public async Task Empty_set_clears_all_assignees()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1 } });

        var detail = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync(
            $"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = Array.Empty<Guid>() }));
        detail.Assignees.Should().BeEmpty("an authoritative empty set clears all assignees (§4.2)");
        (await CountAssigneeRowsAsync(Factory, w.Ticket.Id)).Should().Be(0);
    }

    // ---------------- modified_at: assignment is metadata ----------------

    [Fact]
    public async Task Setting_assignees_does_not_bump_modified_at()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);

        Factory.Clock.Advance(TimeSpan.FromMinutes(30));
        var detail = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync(
            $"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1 } }));

        detail.ModifiedAt.Should().Be(w.Ticket.ModifiedAt,
            "assignment is metadata and never bumps modified_at (§4.2, V21) — board ordering stays stable");
    }

    [Fact]
    public async Task No_op_reassign_of_the_same_set_does_not_bump_modified_at()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1 } });

        Factory.Clock.Advance(TimeSpan.FromMinutes(30));
        var detail = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync(
            $"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1 } }));
        detail.ModifiedAt.Should().Be(w.Ticket.ModifiedAt);
    }

    // ---------------- Board filters ----------------

    [Fact]
    public async Task Board_assigneeId_filter_returns_only_tickets_assigned_to_that_user()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        var ticket2 = await ReadAsync<TicketDto>(await w.Admin.PostAsJsonAsync("/api/tickets",
            new { teamId = w.TeamId, type = "bug", title = "T2", body = "B" }));

        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1 } });
        // ticket2 has no assignees.

        var board = await ReadAsync<BoardDto>(
            await w.Admin.GetAsync($"/api/tickets?teamId={w.TeamId}&assigneeId={m1}"));
        board.Total.Should().Be(1);
        board.Columns.SelectMany(c => c.Tickets).Single().Id.Should().Be(w.Ticket.Id);
    }

    [Fact]
    public async Task Board_assignedToMe_filter_returns_only_my_tickets()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        // Assign ticket to the ADMIN (the caller of the board below).
        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { w.AdminUserId } });
        // A second ticket assigned to m1, not the admin.
        var ticket2 = await ReadAsync<TicketDto>(await w.Admin.PostAsJsonAsync("/api/tickets",
            new { teamId = w.TeamId, type = "bug", title = "T2", body = "B" }));
        await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket2.Id}/assignees", new { userIds = new[] { m1 } });

        var board = await ReadAsync<BoardDto>(
            await w.Admin.GetAsync($"/api/tickets?teamId={w.TeamId}&assignedToMe=true"));
        board.Total.Should().Be(1, "assignedToMe returns only the caller's assigned tickets");
        board.Columns.SelectMany(c => c.Tickets).Single().Id.Should().Be(w.Ticket.Id);
    }

    [Fact]
    public async Task Board_assignedToMe_takes_precedence_over_assigneeId()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        // ticket1 → admin (me); ticket2 → m1.
        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { w.AdminUserId } });
        var ticket2 = await ReadAsync<TicketDto>(await w.Admin.PostAsJsonAsync("/api/tickets",
            new { teamId = w.TeamId, type = "bug", title = "T2", body = "B" }));
        await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket2.Id}/assignees", new { userIds = new[] { m1 } });

        // Send BOTH: assignedToMe=true must win, so we get the admin's ticket, not m1's (§4.2 precedence).
        var board = await ReadAsync<BoardDto>(await w.Admin.GetAsync(
            $"/api/tickets?teamId={w.TeamId}&assignedToMe=true&assigneeId={m1}"));
        board.Total.Should().Be(1);
        board.Columns.SelectMany(c => c.Tickets).Single().Id.Should().Be(w.Ticket.Id,
            "assignedToMe wins over assigneeId when both are sent");
    }

    [Fact]
    public async Task Board_card_carries_assignees()
    {
        var w = await SetupAsync();
        var (m1, e1) = await AddMemberToTeamAsync(w.TeamId);
        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1 } });

        var board = await ReadAsync<BoardDto>(await w.Admin.GetAsync($"/api/tickets?teamId={w.TeamId}"));
        var card = board.Columns.SelectMany(c => c.Tickets).Single(t => t.Id == w.Ticket.Id);
        card.Assignees!.Single().Id.Should().Be(m1);
        card.Assignees!.Single().DisplayName.Should().Be(e1);
    }

    // ---------------- IDOR / authz ordering ----------------

    [Fact]
    public async Task Member_of_team_A_cannot_set_assignees_on_a_team_B_ticket_is_403()
    {
        var w = await SetupAsync("Team Bravo"); // admin owns team B + ticket B
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId); // an eligible B member (valid body)

        // A member who belongs ONLY to team A.
        var (adminToken2, _, _) = await RegisterAdminAsync("admin2@dataart.com");
        var admin2 = Authed(adminToken2);
        var teamA = await ReadAsync<TeamDto>(await admin2.PostAsJsonAsync("/api/teams", new { name = "Team Alpha" }));
        var (memberToken, _, _) = await RegisterMemberInTeamAsync(teamA.Id);
        var memberA = Authed(memberToken);

        // Caller (team-A member) has no access to the team-B ticket ⇒ 403 (checked BEFORE the payload).
        var resp = await memberA.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees",
            new { userIds = new[] { m1 } });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "caller access is checked before the assignee payload (resolve-then-check, §3.3)");
    }

    [Fact]
    public async Task Set_assignees_on_unknown_ticket_is_404()
    {
        var w = await SetupAsync();
        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{Guid.NewGuid()}/assignees",
            new { userIds = Array.Empty<Guid>() });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------- Create with assignees + cascade on delete ----------------

    [Fact]
    public async Task Create_with_assignee_ids_applies_them()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);

        var detail = await ReadAsync<TicketDto>(await w.Admin.PostAsJsonAsync("/api/tickets",
            new { teamId = w.TeamId, type = "bug", title = "with-assignee", body = "B", assigneeIds = new[] { m1 } }));
        detail.Assignees!.Single().Id.Should().Be(m1, "assigneeIds provided on create are applied (§4.2)");
    }

    [Fact]
    public async Task Create_with_ineligible_assignee_id_is_400()
    {
        var w = await SetupAsync();
        var (_, outsiderId, _) = await RegisterMemberAsync("outsider3@dataart.com");
        var resp = await w.Admin.PostAsJsonAsync("/api/tickets",
            new { teamId = w.TeamId, type = "bug", title = "T", body = "B", assigneeIds = new[] { outsiderId } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("userIds");
    }

    [Fact]
    public async Task Update_without_assigneeIds_leaves_the_assignee_set_untouched()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1 } });

        // A normal field edit that omits assigneeIds must NOT wipe assignees (R-10).
        var detail = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}",
            new { teamId = w.TeamId, type = "feature", title = "edited", body = "B", state = "new", priority = "medium", epicId = (Guid?)null }));
        detail.Assignees!.Single().Id.Should().Be(m1, "omitting assigneeIds on PUT leaves the set alone (R-10)");
    }

    [Fact]
    public async Task Deleting_a_ticket_cascades_its_assignees()
    {
        var w = await SetupAsync();
        var (m1, _) = await AddMemberToTeamAsync(w.TeamId);
        var (m2, _) = await AddMemberToTeamAsync(w.TeamId);
        await w.Admin.PutAsJsonAsync($"/api/tickets/{w.Ticket.Id}/assignees", new { userIds = new[] { m1, m2 } });
        (await CountAssigneeRowsAsync(Factory, w.Ticket.Id)).Should().Be(2);

        (await w.Admin.DeleteAsync($"/api/tickets/{w.Ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ticket_assignees rows are gone (FK CASCADE fires under SQLite PRAGMA foreign_keys=ON).
        (await CountAssigneeRowsAsync(Factory, w.Ticket.Id)).Should().Be(0,
            "deleting a ticket cascades its ticket_assignees (§8.B, FK CASCADE)");
        // The users themselves remain (RESTRICT on user_id, no user-delete).
        await Factory.WithDbAsync(async db =>
        {
            (await db.Users.AnyAsync(u => u.Id == m1)).Should().BeTrue();
            (await db.Users.AnyAsync(u => u.Id == m2)).Should().BeTrue();
        });
    }
}
