using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Kanban board read over real HTTP (E6, API_CONTRACT §6.1). Covers the five-column workflow-
/// ordered structure (always present even when empty), most-recently-modified-first ordering
/// within a column (A22), and the AND-combined filters: type, epic, and case-insensitive title
/// substring search (A24). Also asserts teamId required (400) and unknown team (404).
/// </summary>
public sealed class BoardTests : IntegrationTestBase
{

    private sealed record Ctx(HttpClient Client, Guid TeamId);

    private async Task<Ctx> SetupAsync()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        return new Ctx(client, team.Id);
    }

    private async Task<TicketDto> CreateAsync(Ctx ctx, string title, string type = "bug",
        string state = "new", Guid? epicId = null)
        => await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type, title, body = "B", state, epicId }));

    [Fact]
    public async Task Board_always_has_five_columns_in_workflow_order()
    {
        var ctx = await SetupAsync();
        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}"));

        board.Columns.Select(c => c.State).Should().Equal(
            "new", "ready_for_implementation", "in_progress", "ready_for_acceptance", "done");
        board.Total.Should().Be(0);
        board.Columns.Should().OnlyContain(c => c.Count == 0 && c.Tickets.Count == 0);
    }

    [Fact]
    public async Task Within_a_column_tickets_are_ordered_by_modified_desc()
    {
        var ctx = await SetupAsync();

        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 30, 10, 00, 00, DateTimeKind.Utc));
        var a = await CreateAsync(ctx, "A");
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await CreateAsync(ctx, "B");
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await CreateAsync(ctx, "C");

        // Touch A (state change) so it becomes the most recently modified.
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await ctx.Client.PatchAsJsonAsync($"/api/tickets/{a.Id}/state", new { state = "new" }); // no-op, won't move
        // The no-op above intentionally does not bump A; explicitly move it to a different state and back conceptually.
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await ctx.Client.PutAsJsonAsync($"/api/tickets/{a.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "A!", body = "B", state = "new", priority = "medium", epicId = (Guid?)null });

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}"));
        var titles = board.Columns.Single(c => c.State == "new").Tickets.Select(t => t.Title).ToList();
        titles.Should().Equal("A!", "C", "B");
    }

    [Fact]
    public async Task Filter_by_type_returns_only_matching_tickets()
    {
        var ctx = await SetupAsync();
        await CreateAsync(ctx, "Bug1", type: "bug");
        await CreateAsync(ctx, "Feat1", type: "feature");
        await CreateAsync(ctx, "Bug2", type: "bug");

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&type=bug"));
        board.Total.Should().Be(2, "counts reflect the filtered set (A23)");
        board.Columns.SelectMany(c => c.Tickets).Should().OnlyContain(t => t.Type == "bug");
    }

    [Fact]
    public async Task Filter_by_invalid_type_is_400()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&type=story");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Filter_by_epic_returns_only_tickets_referencing_that_epic()
    {
        var ctx = await SetupAsync();
        var epic = await ReadAsync<EpicDto>(
            await ctx.Client.PostAsJsonAsync("/api/epics", new { teamId = ctx.TeamId, title = "Billing" }));
        await CreateAsync(ctx, "WithEpic", epicId: epic.Id);
        await CreateAsync(ctx, "NoEpic");

        var board = await ReadAsync<BoardDto>(
            await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&epicId={epic.Id}"));
        board.Total.Should().Be(1);
        board.Columns.SelectMany(c => c.Tickets).Should().OnlyContain(t => t.EpicId == epic.Id);
    }

    [Fact]
    public async Task Search_is_case_insensitive_substring_over_title()
    {
        var ctx = await SetupAsync();
        await CreateAsync(ctx, "Login fails on Safari");
        await CreateAsync(ctx, "Logout broken");
        await CreateAsync(ctx, "Dashboard slow");

        var board = await ReadAsync<BoardDto>(
            await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&search=LOGIN"));
        board.Total.Should().Be(1);
        board.Columns.SelectMany(c => c.Tickets).Single().Title.Should().Be("Login fails on Safari");
    }

    [Fact]
    public async Task Filters_combine_with_AND_logic()
    {
        var ctx = await SetupAsync();
        var epic = await ReadAsync<EpicDto>(
            await ctx.Client.PostAsJsonAsync("/api/epics", new { teamId = ctx.TeamId, title = "Billing" }));

        await CreateAsync(ctx, "login bug in billing", type: "bug", epicId: epic.Id);   // matches all three
        await CreateAsync(ctx, "login bug no epic", type: "bug");                        // missing epic
        await CreateAsync(ctx, "login feature in billing", type: "feature", epicId: epic.Id); // wrong type
        await CreateAsync(ctx, "unrelated bug in billing", type: "bug", epicId: epic.Id); // wrong search

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync(
            $"/api/tickets?teamId={ctx.TeamId}&type=bug&epicId={epic.Id}&search=login"));
        board.Total.Should().Be(1);
        board.Columns.SelectMany(c => c.Tickets).Single().Title.Should().Be("login bug in billing");
    }

    [Fact]
    public async Task Board_without_teamId_is_400()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.GetAsync("/api/tickets");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("teamId");
    }

    [Fact]
    public async Task Board_for_unknown_team_is_404()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var resp = await client.GetAsync($"/api/tickets?teamId={Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
