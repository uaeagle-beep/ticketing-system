using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// F-03 Priority (WAVE1_DESIGN §8.A, ADR-0009 §A). Dictionary = low/medium/high/urgent, default
/// medium. Priority is OPTIONAL on create (defaults medium) and REQUIRED in the PUT edit body
/// (mirrors type/state). An unknown value ⇒ 400 keyed <c>priority</c>. It participates in the
/// modified_at no-op diff (a priority-only change advances modified_at; an identical value is part of
/// a no-op). The board <c>&amp;priority=</c> filter returns the matching subset and combines with other
/// filters via AND. Expectations are taken from the specification, not the implementation. Real HTTP
/// over the SQLite WebApplicationFactory.
/// </summary>
public sealed class TicketPriorityTests : IntegrationTestBase
{
    private sealed record Ctx(HttpClient Client, Guid TeamId);

    private async Task<Ctx> SetupAsync()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        return new Ctx(client, team.Id);
    }

    // ---------------- Create ----------------

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("urgent")]
    public async Task Create_with_each_valid_priority_persists_it(string priority)
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", priority }));
        ticket.Priority.Should().Be(priority);
    }

    [Fact]
    public async Task Create_without_priority_defaults_to_medium()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));
        ticket.Priority.Should().Be("medium", "priority defaults to medium when omitted (§4.1, ADR-0009 §A)");
    }

    [Fact]
    public async Task Create_with_explicit_null_priority_defaults_to_medium()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", priority = (string?)null }));
        ticket.Priority.Should().Be("medium");
    }

    [Theory]
    [InlineData("Medium")]   // wrong case: strict parse (§6.4)
    [InlineData("URGENT")]
    [InlineData("critical")] // not in the dictionary
    [InlineData("p1")]
    public async Task Create_with_invalid_priority_is_400_keyed_priority(string priority)
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", priority });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("priority");
    }

    [Fact]
    public async Task Create_with_empty_string_priority_is_treated_as_omitted_defaulting_to_medium()
    {
        // Empty string on create is treated as "omitted" (consistent with the state default path,
        // which uses the same string.IsNullOrEmpty check) ⇒ defaults to medium (§4.1). Documenting the
        // contract's behaviour, not asserting a 400 the spec does not require.
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", priority = "" }));
        ticket.Priority.Should().Be("medium");
    }

    // ---------------- Update (priority REQUIRED in the body) ----------------

    [Fact]
    public async Task Update_omitting_priority_is_400_keyed_priority()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", priority = "high" }));

        // priority omitted from the edit body ⇒ 400 (required in PUT, §4.1).
        var resp = await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", epicId = (Guid?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("priority");
    }

    [Fact]
    public async Task Update_with_invalid_priority_is_400_keyed_priority()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));

        var resp = await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", priority = "sky-high", epicId = (Guid?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("priority");
    }

    [Fact]
    public async Task Update_changing_only_priority_advances_modified_at()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", priority = "low" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var updated = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", priority = "urgent", epicId = (Guid?)null }));

        updated.Priority.Should().Be("urgent");
        updated.ModifiedAt.Should().BeAfter(ticket.ModifiedAt,
            "a priority-only change advances modified_at (§4.1 participates in the no-op diff)");
    }

    [Fact]
    public async Task Update_resending_same_priority_is_a_no_op_and_does_not_advance_modified_at()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", priority = "high" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var result = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", priority = "high", epicId = (Guid?)null }));

        result.Priority.Should().Be("high");
        result.ModifiedAt.Should().Be(ticket.ModifiedAt,
            "re-sending the identical priority is part of a no-op (V20, EC6)");
    }

    // ---------------- Board filter ----------------

    [Fact]
    public async Task Board_priority_filter_returns_only_matching_subset()
    {
        var ctx = await SetupAsync();
        await ctx.Client.PostAsJsonAsync("/api/tickets", new { teamId = ctx.TeamId, type = "bug", title = "L", body = "B", priority = "low" });
        await ctx.Client.PostAsJsonAsync("/api/tickets", new { teamId = ctx.TeamId, type = "bug", title = "H1", body = "B", priority = "high" });
        await ctx.Client.PostAsJsonAsync("/api/tickets", new { teamId = ctx.TeamId, type = "bug", title = "H2", body = "B", priority = "high" });
        await ctx.Client.PostAsJsonAsync("/api/tickets", new { teamId = ctx.TeamId, type = "bug", title = "U", body = "B", priority = "urgent" });

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&priority=high"));
        board.Total.Should().Be(2, "only the two high-priority tickets match (A23 counts the filtered set)");
        board.Columns.SelectMany(c => c.Tickets).Should().OnlyContain(t => t.Priority == "high");
    }

    [Fact]
    public async Task Board_priority_filter_combines_with_type_filter_via_AND()
    {
        var ctx = await SetupAsync();
        await ctx.Client.PostAsJsonAsync("/api/tickets", new { teamId = ctx.TeamId, type = "bug", title = "bug-high", body = "B", priority = "high" });
        await ctx.Client.PostAsJsonAsync("/api/tickets", new { teamId = ctx.TeamId, type = "feature", title = "feat-high", body = "B", priority = "high" });
        await ctx.Client.PostAsJsonAsync("/api/tickets", new { teamId = ctx.TeamId, type = "bug", title = "bug-low", body = "B", priority = "low" });

        var board = await ReadAsync<BoardDto>(
            await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&type=bug&priority=high"));
        board.Total.Should().Be(1);
        board.Columns.SelectMany(c => c.Tickets).Single().Title.Should().Be("bug-high");
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("High")]
    [InlineData("p0")]
    public async Task Board_with_invalid_priority_filter_is_400(string priority)
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&priority={priority}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("validation_error");
    }

    [Fact]
    public async Task Board_card_carries_priority()
    {
        var ctx = await SetupAsync();
        await ctx.Client.PostAsJsonAsync("/api/tickets", new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", priority = "urgent" });

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}"));
        board.Columns.SelectMany(c => c.Tickets).Single().Priority.Should().Be("urgent",
            "the board card surfaces priority (§4.1)");
    }
}
