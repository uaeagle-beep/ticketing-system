using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Epics CRUD over real HTTP (E3, API_CONTRACT §5). Covers create/list/edit/delete, team
/// immutability on edit (any teamId in the body ignored), no-op edit (modified_at unchanged),
/// empty title (400), unknown team (400), list scoping to a team, and the delete-guard 409
/// when tickets reference the epic.
/// </summary>
public sealed class EpicsTests : IntegrationTestBase
{

    private async Task<(HttpClient Client, Guid TeamId)> TeamAsync(string teamName = "Platform")
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = teamName }));
        return (client, team.Id);
    }

    [Fact]
    public async Task Create_epic_trims_title_and_belongs_to_team()
    {
        var (client, teamId) = await TeamAsync();

        var epic = await ReadAsync<EpicDto>(await client.PostAsJsonAsync("/api/epics",
            new { teamId, title = "  Billing Revamp  ", description = "optional" }));

        epic.Title.Should().Be("Billing Revamp");
        epic.TeamId.Should().Be(teamId);
        epic.Description.Should().Be("optional");
        epic.TicketCount.Should().Be(0);
        epic.CreatedAt.Should().Be(epic.ModifiedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_epic_with_blank_title_is_400(string title)
    {
        var (client, teamId) = await TeamAsync();
        var resp = await client.PostAsJsonAsync("/api/epics", new { teamId, title });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("title");
    }

    [Fact]
    public async Task Create_epic_with_unknown_team_is_400()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var resp = await client.PostAsJsonAsync("/api/epics",
            new { teamId = Guid.NewGuid(), title = "Orphan" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("teamId");
    }

    [Fact]
    public async Task List_returns_only_epics_for_the_selected_team()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var platform = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var payments = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Payments" }));

        await client.PostAsJsonAsync("/api/epics", new { teamId = platform.Id, title = "P1" });
        await client.PostAsJsonAsync("/api/epics", new { teamId = platform.Id, title = "P2" });
        await client.PostAsJsonAsync("/api/epics", new { teamId = payments.Id, title = "Pay1" });

        var platformEpics = await ReadAsync<List<EpicDto>>(
            await client.GetAsync($"/api/epics?teamId={platform.Id}"));
        platformEpics.Should().HaveCount(2);
        platformEpics.Should().OnlyContain(e => e.TeamId == platform.Id);
    }

    [Fact]
    public async Task Edit_title_and_description_advances_modified_at()
    {
        var (client, teamId) = await TeamAsync();
        var epic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId, title = "Billing Revamp" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var updated = await ReadAsync<EpicDto>(
            await client.PutAsJsonAsync($"/api/epics/{epic.Id}", new { title = "Billing v2", description = "updated" }));

        updated.Title.Should().Be("Billing v2");
        updated.Description.Should().Be("updated");
        updated.ModifiedAt.Should().BeAfter(epic.ModifiedAt);
    }

    [Fact]
    public async Task Edit_with_identical_values_is_a_noop_and_does_not_advance_modified_at()
    {
        var (client, teamId) = await TeamAsync();
        var epic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId, title = "Billing Revamp", description = "d" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var result = await ReadAsync<EpicDto>(
            await client.PutAsJsonAsync($"/api/epics/{epic.Id}", new { title = "Billing Revamp", description = "d" }));

        result.ModifiedAt.Should().Be(epic.ModifiedAt, "a no-op edit must not advance modified_at (A14)");
    }

    [Fact]
    public async Task Edit_cannot_change_the_team_team_is_immutable()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var platform = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var payments = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Payments" }));
        var epic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId = platform.Id, title = "E" }));

        // Send a teamId in the edit body — it must be ignored; the epic stays in Platform (US-EPIC-2).
        var updated = await ReadAsync<EpicDto>(await client.PutAsJsonAsync($"/api/epics/{epic.Id}",
            new { title = "E2", teamId = payments.Id }));
        updated.TeamId.Should().Be(platform.Id, "epic team is read-only after creation (FR-E3-1)");

        // And it still lists only under Platform.
        var paymentsEpics = await ReadAsync<List<EpicDto>>(
            await client.GetAsync($"/api/epics?teamId={payments.Id}"));
        paymentsEpics.Should().NotContain(e => e.Id == epic.Id);
    }

    [Fact]
    public async Task Delete_unreferenced_epic_succeeds_204()
    {
        var (client, teamId) = await TeamAsync();
        var epic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId, title = "Old Initiative" }));

        var del = await client.DeleteAsync($"/api/epics/{epic.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_epic_referenced_by_a_ticket_is_409()
    {
        var (client, teamId) = await TeamAsync();
        var epic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId, title = "Billing Revamp" }));
        await client.PostAsJsonAsync("/api/tickets",
            new { teamId, type = "feature", title = "T", body = "B", epicId = epic.Id });

        var del = await client.DeleteAsync($"/api/epics/{epic.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(del)).Code.Should().Be("epic_referenced_by_tickets");

        // Epic remains.
        var epics = await ReadAsync<List<EpicDto>>(await client.GetAsync($"/api/epics?teamId={teamId}"));
        epics.Should().ContainSingle(e => e.Id == epic.Id);
    }

    [Fact]
    public async Task Delete_unknown_epic_is_404()
    {
        var (client, _) = await TeamAsync();
        var resp = await client.DeleteAsync($"/api/epics/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
