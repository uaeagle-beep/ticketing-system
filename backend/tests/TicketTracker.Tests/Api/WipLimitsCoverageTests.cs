using System.Net;
using System.Net.Http.Json;
using System.Text;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA-authored complement to <see cref="WipLimitsTests"/> (the developer's 19 self-checks). These tests
/// fill the customer-required coverage that the self-checks left thin or untested, derived strictly from
/// the SPEC (API_CONTRACT §4.5/§6.1-6.5, WIP_LIMITS_UX §2/§4) rather than from the implementation:
///
///   A) Configuring limits   — valid set surfaced on the board column.wipLimit; the EXACT per-state
///                             validation strings for 0/negative/2.5/"abc"/&gt;999; auth 401 without a token.
///   B) Positive moves       — create/move into a not-yet-full column grows the unfiltered total; exit a
///                             full column; an UNLIMITED column accepts an unbounded number of arrivals.
///   C) Negative (409)       — PATCH/POST/PUT into a full state are not only rejected with the exact code
///                             AND message, but ALSO leave persisted state untouched (verified by reload):
///                             ticket NOT moved / NOT created / NOT changed; and a TEAM change that lands a
///                             ticket in the TARGET team's full state is blocked (cap counted per target team).
///
/// All over real HTTP through the SQLite WebApplicationFactory, no Docker — same harness as the rest.
/// </summary>
public sealed class WipLimitsCoverageTests : IntegrationTestBase
{
    private const string ReachedMessage =
        "This status already has the maximum number of tickets — finish existing ones first.";
    private const string WholeNumberMessage =
        "Enter a whole number of 1 or more, or leave blank for no limit.";
    private const string TooLargeMessage =
        "Enter a number no greater than 999.";

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

    private async Task<TicketDto> ReloadAsync(Ctx ctx, Guid id)
        => await ReadAsync<TicketDto>(await ctx.Client.GetAsync($"/api/tickets/{id}"));

    private async Task<BoardColumnDto> ColumnAsync(Ctx ctx, string state)
    {
        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}"));
        return board.Columns.Single(c => c.State == state);
    }

    // ============================================================== A) Configuring limits

    [Fact] // A: valid set is reflected on the board column.wipLimit (not just on the Team object)
    public async Task Valid_limit_is_exposed_on_the_board_column()
    {
        var ctx = await SetupAsync();

        var resp = await PutLimitsAsync(ctx, new { in_progress = 3 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var col = await ColumnAsync(ctx, "in_progress");
        col.WipLimit.Should().Be(3, "the configured cap is read back on the board column (API_CONTRACT §6.1)");
        col.Total.Should().Be(0);
    }

    [Fact] // A: clearing a previously-set limit (null) removes it from the board column too
    public async Task Cleared_limit_makes_the_board_column_unlimited_again()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 3 });

        await PutLimitsAsync(ctx, new { in_progress = (int?)null });

        var col = await ColumnAsync(ctx, "in_progress");
        col.WipLimit.Should().BeNull("null = unlimited, the row is removed (API_CONTRACT §4.5)");
    }

    [Theory] // A: invalid COUNTS — exact spec strings keyed by the offending state (UX §2.2, API_CONTRACT §4.5)
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public async Task Zero_or_negative_returns_the_whole_number_message(int value)
    {
        var ctx = await SetupAsync();
        var resp = await PutLimitsAsync(ctx, new Dictionary<string, int> { ["in_progress"] = value });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("in_progress");
        err.Errors!["in_progress"].Should().ContainSingle().Which.Should().Be(WholeNumberMessage);
    }

    [Fact] // A: above the 999 ceiling has its OWN distinct message (API_CONTRACT §4.5 example)
    public async Task Above_max_returns_the_too_large_message()
    {
        var ctx = await SetupAsync();
        var resp = await PutLimitsAsync(ctx, new Dictionary<string, int> { ["done"] = 1000 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Errors.Should().ContainKey("done");
        err.Errors!["done"].Should().ContainSingle().Which.Should().Be(TooLargeMessage);
    }

    [Fact] // A: 999 is the inclusive upper bound — accepted
    public async Task Exactly_999_is_accepted()
    {
        var ctx = await SetupAsync();
        var resp = await PutLimitsAsync(ctx, new { done = 999 });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<TeamDto>(resp)).WipLimits!["done"].Should().Be(999);
    }

    [Fact] // A: 1 is the inclusive lower bound — accepted
    public async Task Exactly_1_is_accepted()
    {
        var ctx = await SetupAsync();
        var resp = await PutLimitsAsync(ctx, new { @new = 1 });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<TeamDto>(resp)).WipLimits!["new"].Should().Be(1);
    }

    [Theory] // A: invalid VALUES — fractional / non-numeric — exact whole-number message
    [InlineData("2.5")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    public async Task Fractional_or_non_numeric_returns_the_whole_number_message(string rawJsonValue)
    {
        var ctx = await SetupAsync();
        var json = $"{{\"wipLimits\":{{\"in_progress\":{rawJsonValue}}}}}";
        var resp = await ctx.Client.PutAsync($"/api/teams/{ctx.TeamId}/wip-limits",
            new StringContent(json, Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("in_progress");
        err.Errors!["in_progress"].Should().ContainSingle().Which.Should().Be(WholeNumberMessage);
    }

    [Fact] // A: several invalid states in one request → per-state errors all reported together
    public async Task Multiple_invalid_states_each_get_their_own_error()
    {
        var ctx = await SetupAsync();
        var resp = await PutLimitsAsync(ctx,
            new Dictionary<string, int> { ["in_progress"] = 0, ["done"] = 1000 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Errors.Should().ContainKey("in_progress");
        err.Errors.Should().ContainKey("done");
        err.Errors!["in_progress"].Should().ContainSingle().Which.Should().Be(WholeNumberMessage);
        err.Errors!["done"].Should().ContainSingle().Which.Should().Be(TooLargeMessage);
    }

    [Fact] // A: a rejected request must NOT have persisted any of its limits (all-or-nothing)
    public async Task A_rejected_request_persists_nothing()
    {
        var ctx = await SetupAsync();
        // in_progress would be valid on its own, but done=1000 makes the whole request invalid.
        var resp = await PutLimitsAsync(ctx,
            new Dictionary<string, int> { ["in_progress"] = 3, ["done"] = 1000 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var team = (await ReadAsync<List<TeamDto>>(await ctx.Client.GetAsync("/api/teams"))).Single();
        team.WipLimits!["in_progress"].Should().BeNull("the valid sibling must not have been persisted on a rejected request");
        team.WipLimits!["done"].Should().BeNull();
    }

    [Fact] // A: auth — no bearer token → 401 (API_CONTRACT §1)
    public async Task Set_limits_without_a_token_is_401()
    {
        var ctx = await SetupAsync();
        // The base Client carries no Authorization header.
        var resp = await Client.PutAsJsonAsync($"/api/teams/{ctx.TeamId}/wip-limits",
            new { wipLimits = new { in_progress = 3 } });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ============================================================== B) Positive moves (allowed)

    [Fact] // B: creating into a not-yet-full column succeeds and grows the unfiltered total
    public async Task Create_into_a_not_full_column_succeeds_and_total_grows()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 2 });

        (await ColumnAsync(ctx, "in_progress")).Total.Should().Be(0);
        await CreateAsync(ctx, "A", state: "in_progress");
        (await ColumnAsync(ctx, "in_progress")).Total.Should().Be(1, "one arrival under the cap is allowed");

        var second = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "B", body = "B", state = "in_progress" });
        second.StatusCode.Should().Be(HttpStatusCode.Created, "the second arrival reaches but does not exceed the cap");
        (await ColumnAsync(ctx, "in_progress")).Total.Should().Be(2);
    }

    [Fact] // B: PATCH move into a not-yet-full column succeeds and the ticket is actually there afterwards
    public async Task Patch_move_into_a_not_full_column_lands_the_ticket()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 2 });
        await CreateAsync(ctx, "Occupant", state: "in_progress");
        var mover = await CreateAsync(ctx, "Mover", state: "new");

        var resp = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{mover.Id}/state", new { state = "in_progress" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReloadAsync(ctx, mover.Id)).State.Should().Be("in_progress", "the move is persisted");
        (await ColumnAsync(ctx, "in_progress")).Total.Should().Be(2);
    }

    [Fact] // B: a ticket can LEAVE a full column into another column (exit is never blocked)
    public async Task Exiting_a_full_column_into_another_succeeds()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        var t = await CreateAsync(ctx, "A", state: "in_progress"); // in_progress now full (1/1)

        var resp = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{t.Id}/state", new { state = "done" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReloadAsync(ctx, t.Id)).State.Should().Be("done");
        (await ColumnAsync(ctx, "in_progress")).Total.Should().Be(0, "the full column drained when the ticket left");
    }

    [Fact] // B: an UNLIMITED column (no cap) accepts an unbounded number of arrivals
    public async Task Unlimited_column_accepts_many_arrivals()
    {
        var ctx = await SetupAsync();
        // No limit set anywhere → 'new' is unlimited.
        for (var i = 0; i < 12; i++)
        {
            var resp = await ctx.Client.PostAsJsonAsync("/api/tickets",
                new { teamId = ctx.TeamId, type = "bug", title = $"T{i}", body = "B", state = "new" });
            resp.StatusCode.Should().Be(HttpStatusCode.Created, "an unlimited column never blocks (UX §1.3)");
        }

        var col = await ColumnAsync(ctx, "new");
        col.WipLimit.Should().BeNull();
        col.Total.Should().Be(12);
    }

    // ============================================================== C) Negative moves (blocked, 409)

    [Fact] // C: PATCH into a full column → 409 + exact message + ticket NOT moved (reload)
    public async Task Patch_into_full_is_blocked_with_message_and_ticket_not_moved()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        await CreateAsync(ctx, "Occupant", state: "in_progress");
        var mover = await CreateAsync(ctx, "Mover", state: "new");

        var resp = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{mover.Id}/state", new { state = "in_progress" });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("wip_limit_reached");
        err.Message.Should().Be(ReachedMessage);

        (await ReloadAsync(ctx, mover.Id)).State.Should().Be("new", "a rejected move must not persist (FR-E6-5)");
        (await ColumnAsync(ctx, "in_progress")).Total.Should().Be(1, "the full column count is unchanged");
    }

    [Fact] // C: POST into a full state → 409 + ticket NOT created (column count unchanged)
    public async Task Create_into_full_is_blocked_and_nothing_is_created()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        await CreateAsync(ctx, "Occupant", state: "in_progress");

        var resp = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "Rejected", body = "B", state = "in_progress" });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("wip_limit_reached");
        err.Message.Should().Be(ReachedMessage);

        var col = await ColumnAsync(ctx, "in_progress");
        col.Total.Should().Be(1, "the rejected ticket was not created");
        col.Tickets.Should().NotContain(t => t.Title == "Rejected");
    }

    [Fact] // C: PUT moving into a full state → 409 + nothing changed (reload: state/title preserved)
    public async Task Update_into_full_is_blocked_and_nothing_changes()
    {
        var ctx = await SetupAsync();
        await PutLimitsAsync(ctx, new { in_progress = 1 });
        await CreateAsync(ctx, "Occupant", state: "in_progress");
        var mover = await CreateAsync(ctx, "Mover", state: "new");

        var resp = await ctx.Client.PutAsJsonAsync($"/api/tickets/{mover.Id}",
            new { teamId = ctx.TeamId, type = "feature", title = "Mover renamed", body = "B2",
                  state = "in_progress", epicId = (Guid?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("wip_limit_reached");
        err.Message.Should().Be(ReachedMessage);

        var reloaded = await ReloadAsync(ctx, mover.Id);
        reloaded.State.Should().Be("new", "a rejected edit must not move the ticket");
        reloaded.Title.Should().Be("Mover", "a rejected edit must not persist the other field changes either");
        reloaded.Type.Should().Be("bug");
    }

    [Fact] // C: changing a ticket's TEAM so it lands in the TARGET team's full state → 409 (cap is per target team)
    public async Task Update_changing_team_into_target_teams_full_state_is_blocked()
    {
        // Two teams owned by the same user. Source has no cap; target caps in_progress at 1 and is already full.
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var source = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Source" }));
        var target = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Target" }));

        await client.PutAsJsonAsync($"/api/teams/{target.Id}/wip-limits", new { wipLimits = new { in_progress = 1 } });
        // Fill the TARGET team's in_progress column.
        await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = target.Id, type = "bug", title = "TargetOccupant", body = "B", state = "in_progress" }));

        // A ticket sitting in_progress in the SOURCE team (its in_progress is uncapped, so this is fine).
        var mover = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = source.Id, type = "bug", title = "Mover", body = "B", state = "in_progress" }));

        // Move it to the TARGET team while keeping state=in_progress → arrives in the target's FULL column.
        var resp = await client.PutAsJsonAsync($"/api/tickets/{mover.Id}",
            new { teamId = target.Id, type = "bug", title = "Mover", body = "B",
                  state = "in_progress", epicId = (Guid?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the WIP cap is evaluated for the TARGET (team, state), not the source");
        (await ReadErrorAsync(resp)).Code.Should().Be("wip_limit_reached");

        // The ticket must still belong to the source team, unchanged.
        var reloaded = await ReadAsync<TicketDto>(await client.GetAsync($"/api/tickets/{mover.Id}"));
        reloaded.TeamId.Should().Be(source.Id, "a rejected cross-team move must not change the ticket's team");
        reloaded.State.Should().Be("in_progress");
    }

    [Fact] // C: same cross-team move into the target team where the column is NOT full → allowed (the positive twin)
    public async Task Update_changing_team_into_target_teams_open_state_is_allowed()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var source = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Source" }));
        var target = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Target" }));

        await client.PutAsJsonAsync($"/api/teams/{target.Id}/wip-limits", new { wipLimits = new { in_progress = 2 } });
        // Target's in_progress has 1 of 2 — room for one more.
        await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = target.Id, type = "bug", title = "TargetOccupant", body = "B", state = "in_progress" }));

        var mover = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = source.Id, type = "bug", title = "Mover", body = "B", state = "in_progress" }));

        var resp = await client.PutAsJsonAsync($"/api/tickets/{mover.Id}",
            new { teamId = target.Id, type = "bug", title = "Mover", body = "B",
                  state = "in_progress", epicId = (Guid?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "the target column had room (1 of 2)");
        var reloaded = await ReadAsync<TicketDto>(await client.GetAsync($"/api/tickets/{mover.Id}"));
        reloaded.TeamId.Should().Be(target.Id);
        reloaded.State.Should().Be("in_progress");
    }
}
