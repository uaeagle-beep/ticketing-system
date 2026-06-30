using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// WIP (Work-In-Progress) limits over real HTTP (UX_LIMITS spec; API_CONTRACT §4/§6).
/// Covers: reading defaults (all unlimited), PUT validation ([1,999]/null/unknown-state),
/// setting a cap below the current count (allowed), board badge fields (wipLimit + UNFILTERED
/// total even under a type filter), and 409 wip_limit_reached enforcement on create / PATCH state /
/// PUT update — including that same-state and exit-state moves are always allowed.
/// </summary>
public sealed class WipLimitsTests : IntegrationTestBase
{
    private sealed record Ctx(HttpClient Client, Guid TeamId);

    private async Task<Ctx> SetupAsync(string teamName = "Platform")
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = teamName }));
        return new Ctx(client, team.Id);
    }

    private static Task<HttpResponseMessage> PutLimitsAsync(Ctx ctx, object wipLimits)
        => ctx.Client.PutAsJsonAsync($"/api/teams/{ctx.TeamId}/wip-limits", new { wipLimits });

    private async Task<TicketDto> CreateAsync(Ctx ctx, string title, string state = "new", string type = "bug")
        => await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type, title, body = "B", state }));

    [Fact]
    public async Task New_team_has_all_states_unlimited()
    {
        var ctx = await SetupAsync();
        var team = (await ReadAsync<List<TeamDto>>(await ctx.Client.GetAsync("/api/teams"))).Single();

        team.WipLimits.Should().NotBeNull();
        team.WipLimits!.Should().HaveCount(5);
        team.WipLimits.Keys.Should().BeEquivalentTo(
            new[] { "new", "ready_for_implementation", "in_progress", "ready_for_acceptance", "done" });
        team.WipLimits.Values.Should().OnlyContain(v => v == null, "a fresh team has no limits (unlimited)");
    }

    [Fact]
    public async Task Set_limits_persists_and_is_returned_for_all_five_states()
    {
        var ctx = await SetupAsync();

        var resp = await PutLimitsAsync(ctx, new { in_progress = 3, ready_for_implementation = 5 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var team = await ReadAsync<TeamDto>(resp);

        team.WipLimits!["in_progress"].Should().Be(3);
        team.WipLimits["ready_for_implementation"].Should().Be(5);
        team.WipLimits["new"].Should().BeNull();
        team.WipLimits["done"].Should().BeNull();

        // Persisted across a fresh read.
        var reread = (await ReadAsync<List<TeamDto>>(await ctx.Client.GetAsync("/api/teams"))).Single();
        reread.WipLimits!["in_progress"].Should().Be(3);
    }

    [Fact]
    public async Task Null_clears_a_previously_set_limit()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 3 });

        // Re-save with null => unlimited again.
        var resp = await PutLimitsAsync(ctx, new { in_progress = (int?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var team = await ReadAsync<TeamDto>(resp);
        team.WipLimits!["in_progress"].Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public async Task Out_of_range_value_is_400_with_per_state_error(int value)
    {
        var ctx = await SetupAsync();
        var resp = await PutLimitsAsync(ctx, new Dictionary<string, int> { ["in_progress"] = value });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("in_progress");
    }

    [Theory]
    [InlineData("2.5")]
    [InlineData("\"abc\"")]
    public async Task Fractional_or_non_numeric_value_is_400(string rawJsonValue)
    {
        var ctx = await SetupAsync();
        // Hand-craft the body so the raw JSON value is exactly the (invalid) literal.
        var json = $"{{\"wipLimits\":{{\"in_progress\":{rawJsonValue}}}}}";
        var resp = await ctx.Client.PutAsync($"/api/teams/{ctx.TeamId}/wip-limits",
            new StringContent(json, Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("validation_error");
    }

    [Fact]
    public async Task Unknown_state_key_is_400_with_that_key_in_errors()
    {
        var ctx = await SetupAsync();
        var resp = await PutLimitsAsync(ctx, new Dictionary<string, int> { ["doing"] = 3 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("doing");
    }

    [Fact]
    public async Task Set_limits_on_unknown_team_is_404()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PutAsJsonAsync($"/api/teams/{Guid.NewGuid()}/wip-limits",
            new { wipLimits = new { in_progress = 3 } });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lowering_limit_below_current_count_is_allowed_and_existing_tickets_remain()
    {
        var ctx = await SetupAsync();
        await CreateAsync(ctx, "A", state: "in_progress");
        await CreateAsync(ctx, "B", state: "in_progress");
        await CreateAsync(ctx, "C", state: "in_progress");

        // Set the cap to 1 even though 3 are already in_progress — allowed (only NEW arrivals blocked).
        var resp = await PutLimitsAsync(ctx, new { in_progress = 1 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}"));
        var col = board.Columns.Single(c => c.State == "in_progress");
        col.Total.Should().Be(3, "existing over-limit tickets are not removed");
        col.WipLimit.Should().Be(1);
    }

    [Fact]
    public async Task Board_total_is_unfiltered_even_when_a_filter_is_active()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { @new = 5 });
        await CreateAsync(ctx, "Bug1", state: "new", type: "bug");
        await CreateAsync(ctx, "Bug2", state: "new", type: "bug");
        await CreateAsync(ctx, "Feat1", state: "new", type: "feature");

        var board = await ReadAsync<BoardDto>(
            await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&type=feature"));
        var col = board.Columns.Single(c => c.State == "new");
        col.Count.Should().Be(1, "the filtered card count is post-filter (A23)");
        col.Total.Should().Be(3, "the badge numerator/total is the UNFILTERED per-state total (UX §3.1)");
        col.WipLimit.Should().Be(5);
    }

    [Fact]
    public async Task Create_into_a_full_state_is_409_wip_limit_reached()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        await CreateAsync(ctx, "A", state: "in_progress");

        var resp = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "B", body = "B", state = "in_progress" });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("wip_limit_reached");
        err.Message.Should().Be("This status already has the maximum number of tickets — finish existing ones first.");
    }

    [Fact]
    public async Task Patch_state_into_a_full_column_is_409()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        await CreateAsync(ctx, "Occupant", state: "in_progress");
        var mover = await CreateAsync(ctx, "Mover", state: "new");

        var resp = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{mover.Id}/state", new { state = "in_progress" });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("wip_limit_reached");
    }

    [Fact]
    public async Task Patch_state_into_a_not_yet_full_column_succeeds()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 2 });
        await CreateAsync(ctx, "Occupant", state: "in_progress");
        var mover = await CreateAsync(ctx, "Mover", state: "new");

        var resp = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{mover.Id}/state", new { state = "in_progress" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Same_state_move_into_a_full_column_is_a_noop_and_allowed()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        var t = await CreateAsync(ctx, "A", state: "in_progress"); // fills the column

        // Patch to the same state — no-op, must not be blocked even though the column is "full".
        var resp = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{t.Id}/state", new { state = "in_progress" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Leaving_a_full_state_is_always_allowed()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { @new = 1 }); // 'new' is capped at 1
        var t = await CreateAsync(ctx, "A", state: "new"); // new is now full

        // Moving OUT of the full 'new' column to 'in_progress' (uncapped) is allowed.
        var resp = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{t.Id}/state", new { state = "in_progress" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_moving_into_a_full_state_is_409()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        await CreateAsync(ctx, "Occupant", state: "in_progress");
        var mover = await CreateAsync(ctx, "Mover", state: "new");

        var resp = await ctx.Client.PutAsJsonAsync($"/api/tickets/{mover.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "Mover", body = "B", state = "in_progress", epicId = (Guid?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("wip_limit_reached");
    }

    [Fact]
    public async Task Update_editing_a_ticket_already_in_a_full_state_without_moving_is_allowed()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        var t = await CreateAsync(ctx, "A", state: "in_progress"); // fills in_progress

        // Edit the title but keep the same state — the ticket isn't arriving, so it's allowed.
        var resp = await ctx.Client.PutAsJsonAsync($"/api/tickets/{t.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "A edited", body = "B", state = "in_progress", epicId = (Guid?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<TicketDto>(resp)).Title.Should().Be("A edited");
    }
}
