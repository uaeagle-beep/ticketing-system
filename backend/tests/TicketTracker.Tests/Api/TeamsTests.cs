using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Teams CRUD over real HTTP (E2, API_CONTRACT §4). Covers create/list/rename/delete,
/// case-insensitive uniqueness (409 duplicate_team_name), empty/whitespace name (400),
/// no-op rename (modified_at unchanged), and the delete-guard 409 when the team has tickets/epics.
/// </summary>
public sealed class TeamsTests : IntegrationTestBase
{

    private async Task<HttpClient> AuthedClientAsync()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        return Authed(token);
    }

    [Fact]
    public async Task Create_trims_name_and_lists_with_zero_counts()
    {
        var client = await AuthedClientAsync();

        var create = await client.PostAsJsonAsync("/api/teams", new { name = "  Platform  " });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var team = await ReadAsync<TeamDto>(create);
        team.Name.Should().Be("Platform", "name is stored trimmed (V8)");
        team.TicketCount.Should().Be(0);
        team.EpicCount.Should().Be(0);
        team.CreatedAt.Should().Be(team.ModifiedAt, "createdAt == modifiedAt at creation");

        var list = await ReadAsync<List<TeamDto>>(await client.GetAsync("/api/teams"));
        list.Should().ContainSingle(t => t.Id == team.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_with_blank_name_is_400(string name)
    {
        var client = await AuthedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/teams", new { name });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("validation_error");
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("  PLATFORM  ")]
    public async Task Create_duplicate_name_is_409_case_insensitive(string duplicate)
    {
        var client = await AuthedClientAsync();
        (await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var resp = await client.PostAsJsonAsync("/api/teams", new { name = duplicate });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("duplicate_team_name");

        // Sanity: still exactly one team.
        var list = await ReadAsync<List<TeamDto>>(await client.GetAsync("/api/teams"));
        list.Count(t => string.Equals(t.Name, "Platform", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    [Fact]
    public async Task Rename_to_new_unique_name_advances_modified_at()
    {
        var client = await AuthedClientAsync();
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var renamed = await ReadAsync<TeamDto>(
            await client.PutAsJsonAsync($"/api/teams/{team.Id}", new { name = "Payments" }));

        renamed.Name.Should().Be("Payments");
        renamed.ModifiedAt.Should().BeAfter(team.ModifiedAt, "an actual rename advances modified_at (A9)");
    }

    [Fact]
    public async Task Rename_to_normalized_same_value_is_a_noop_and_does_not_advance_modified_at()
    {
        var client = await AuthedClientAsync();
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var result = await ReadAsync<TeamDto>(
            await client.PutAsJsonAsync($"/api/teams/{team.Id}", new { name = "  platform  " }));

        result.Name.Should().Be("Platform", "no change is persisted");
        result.ModifiedAt.Should().Be(team.ModifiedAt, "no-op rename must not advance modified_at (A10)");
    }

    [Fact]
    public async Task Rename_colliding_with_another_team_is_409()
    {
        var client = await AuthedClientAsync();
        var platform = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        await client.PostAsJsonAsync("/api/teams", new { name = "Payments" });

        var resp = await client.PutAsJsonAsync($"/api/teams/{platform.Id}", new { name = "payments" });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("duplicate_team_name");
    }

    [Fact]
    public async Task Rename_unknown_team_is_404()
    {
        var client = await AuthedClientAsync();
        var resp = await client.PutAsJsonAsync($"/api/teams/{Guid.NewGuid()}", new { name = "Whatever" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorAsync(resp)).Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Delete_empty_team_succeeds_204()
    {
        var client = await AuthedClientAsync();
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Sandbox" }));

        var del = await client.DeleteAsync($"/api/teams/{team.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await ReadAsync<List<TeamDto>>(await client.GetAsync("/api/teams"));
        list.Should().NotContain(t => t.Id == team.Id);
    }

    [Fact]
    public async Task Delete_team_with_a_ticket_is_409_and_nothing_is_deleted()
    {
        var client = await AuthedClientAsync();
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        await client.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "T", body = "B" });

        var del = await client.DeleteAsync($"/api/teams/{team.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(del)).Code.Should().Be("team_has_children");

        // No cascade: the team still exists.
        var list = await ReadAsync<List<TeamDto>>(await client.GetAsync("/api/teams"));
        list.Should().ContainSingle(t => t.Id == team.Id);
    }

    [Fact]
    public async Task Delete_team_with_an_epic_is_409()
    {
        var client = await AuthedClientAsync();
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        await client.PostAsJsonAsync("/api/epics", new { teamId = team.Id, title = "Epic A" });

        var del = await client.DeleteAsync($"/api/teams/{team.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(del)).Code.Should().Be("team_has_children");
    }

    [Fact]
    public async Task Delete_unknown_team_is_404()
    {
        var client = await AuthedClientAsync();
        var resp = await client.DeleteAsync($"/api/teams/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
