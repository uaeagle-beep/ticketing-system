using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// F-08 Due date (WAVE1_DESIGN §8.C, ADR-0009 §C). Optional nullable date-only ("YYYY-MM-DD",
/// calendar day UTC). Create/edit/clear; past dates are allowed (a past due date is simply overdue,
/// not invalid). <c>isOverdue</c> is BACKEND-computed from the injected clock:
/// <c>dueDate != null &amp;&amp; dueDate &lt; today(UTC) &amp;&amp; state != done</c> — driven here with
/// <see cref="TestClock"/>. Board filter <c>dueFilter=overdue|has_due_date|no_due_date</c>; a bad
/// dueFilter or an ill-formed date string ⇒ 400. The base clock is 2026-06-30 12:00 UTC (today =
/// 2026-06-30). Real HTTP over the SQLite factory.
/// </summary>
public sealed class TicketDueDateTests : IntegrationTestBase
{
    private sealed record Ctx(HttpClient Client, Guid TeamId);

    private async Task<Ctx> SetupAsync()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        return new Ctx(client, team.Id);
    }

    // ---------------- Create / edit / clear ----------------

    [Fact]
    public async Task Create_with_a_future_due_date_persists_it_and_is_not_overdue()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate = "2026-07-05" }));

        ticket.DueDate.Should().Be(new DateOnly(2026, 7, 5));
        ticket.IsOverdue.Should().BeFalse("a future due date is not overdue (today = 2026-06-30)");
    }

    [Fact]
    public async Task Create_without_due_date_has_null_and_not_overdue()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));
        ticket.DueDate.Should().BeNull();
        ticket.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task Create_with_a_past_due_date_is_allowed_and_overdue()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate = "2026-06-01", state = "new" }));

        ticket.DueDate.Should().Be(new DateOnly(2026, 6, 1), "a past date is valid, just overdue (§4.3)");
        ticket.IsOverdue.Should().BeTrue("2026-06-01 < today (2026-06-30) and state != done");
    }

    [Fact]
    public async Task Edit_can_set_and_then_clear_the_due_date()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));

        // Set it.
        var withDue = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", priority = "medium", dueDate = "2026-07-10", epicId = (Guid?)null }));
        withDue.DueDate.Should().Be(new DateOnly(2026, 7, 10));

        // Clear it back to null.
        var cleared = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", priority = "medium", dueDate = (string?)null, epicId = (Guid?)null }));
        cleared.DueDate.Should().BeNull("clearing the due date to null is an accepted change (§4.3)");
    }

    [Fact]
    public async Task Changing_only_due_date_advances_modified_at_and_clearing_it_is_a_change()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate = "2026-07-10" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var changed = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", priority = "medium", dueDate = "2026-07-20", epicId = (Guid?)null }));
        changed.ModifiedAt.Should().BeAfter(ticket.ModifiedAt, "a due-date change advances modified_at (§4.3)");

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var cleared = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", priority = "medium", dueDate = (string?)null, epicId = (Guid?)null }));
        cleared.ModifiedAt.Should().BeAfter(changed.ModifiedAt, "clearing to null is a change (§4.3)");
    }

    [Fact]
    public async Task Resending_the_same_due_date_is_a_no_op()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate = "2026-07-10" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var result = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "new", priority = "medium", dueDate = "2026-07-10", epicId = (Guid?)null }));
        result.ModifiedAt.Should().Be(ticket.ModifiedAt, "re-sending the same due date is part of a no-op");
    }

    // ---------------- isOverdue rule (TestClock-driven) ----------------

    [Fact]
    public async Task IsOverdue_becomes_true_when_today_advances_past_the_due_date()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate = "2026-07-01" }));
        ticket.IsOverdue.Should().BeFalse("today (2026-06-30) < due date (2026-07-01)");

        // Advance the clock so "today" crosses past the due date. Stay WITHIN the 72h session TTL
        // (base clock 2026-06-30 12:00 ⇒ session valid until 2026-07-03 12:00) so the bearer token
        // is still valid — otherwise the read would 401 (session expiry, not an overdue-computation bug).
        Factory.Clock.SetUtcNow(new DateTime(2026, 07, 02, 09, 00, 00, DateTimeKind.Utc));
        var reread = await ReadAsync<TicketDto>(await ctx.Client.GetAsync($"/api/tickets/{ticket.Id}"));
        reread.IsOverdue.Should().BeTrue("2026-07-01 < today (2026-07-02) and state != done");
        reread.DueDate.Should().Be(new DateOnly(2026, 7, 1), "the due date itself is unchanged by the clock");
    }

    [Fact]
    public async Task Due_date_equal_to_today_is_not_overdue_boundary()
    {
        var ctx = await SetupAsync();
        // due == today (2026-06-30): the rule is strictly dueDate < today, so NOT overdue.
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate = "2026-06-30" }));
        ticket.IsOverdue.Should().BeFalse("dueDate == today is NOT overdue (strict <, §3.3 boundary)");
    }

    [Fact]
    public async Task Done_ticket_with_a_past_due_date_is_not_overdue()
    {
        var ctx = await SetupAsync();
        // Past due date but state == done ⇒ NOT overdue (rule excludes done).
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate = "2026-06-01", state = "done" }));
        ticket.IsOverdue.Should().BeFalse("a done ticket is never overdue even with a past due date (§3.3)");
    }

    // ---------------- dueFilter ----------------

    private async Task SeedForDueFilterAsync(Ctx ctx)
    {
        // overdue: past date, not done.
        await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "overdue", body = "B", dueDate = "2026-06-01", state = "new" });
        // has due date but future (not overdue).
        await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "future", body = "B", dueDate = "2026-07-15", state = "new" });
        // past date but done (has due date, NOT overdue).
        await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "past-done", body = "B", dueDate = "2026-06-02", state = "done" });
        // no due date.
        await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "no-due", body = "B", state = "new" });
    }

    [Fact]
    public async Task DueFilter_overdue_returns_only_overdue_tickets()
    {
        var ctx = await SetupAsync();
        await SeedForDueFilterAsync(ctx);

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&dueFilter=overdue"));
        var titles = board.Columns.SelectMany(c => c.Tickets).Select(t => t.Title).ToList();
        titles.Should().BeEquivalentTo(new[] { "overdue" },
            "only past-and-not-done tickets are overdue (past-done excluded, §4.3)");
    }

    [Fact]
    public async Task DueFilter_has_due_date_returns_all_tickets_with_a_due_date()
    {
        var ctx = await SetupAsync();
        await SeedForDueFilterAsync(ctx);

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&dueFilter=has_due_date"));
        board.Columns.SelectMany(c => c.Tickets).Select(t => t.Title)
            .Should().BeEquivalentTo(new[] { "overdue", "future", "past-done" });
    }

    [Fact]
    public async Task DueFilter_no_due_date_returns_only_tickets_without_a_due_date()
    {
        var ctx = await SetupAsync();
        await SeedForDueFilterAsync(ctx);

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&dueFilter=no_due_date"));
        board.Columns.SelectMany(c => c.Tickets).Select(t => t.Title)
            .Should().BeEquivalentTo(new[] { "no-due" });
    }

    [Theory]
    [InlineData("past_due")]
    [InlineData("overdue_soon")]
    [InlineData("OVERDUE")]
    [InlineData("has-due-date")]
    public async Task Bad_dueFilter_value_is_400(string dueFilter)
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}&dueFilter={dueFilter}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("validation_error");
    }

    // ---------------- Ill-formed date string ----------------

    [Theory]
    [InlineData("2026-13-01")]   // invalid month
    [InlineData("2026-02-30")]   // invalid day
    [InlineData("not-a-date")]
    [InlineData("07/05/2026")]   // wrong format
    public async Task Create_with_ill_formed_due_date_is_400(string dueDate)
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an ill-formed date string fails model binding ⇒ 400 (§4.3)");
    }

    [Fact]
    public async Task Board_card_carries_due_date_and_is_overdue()
    {
        var ctx = await SetupAsync();
        await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", dueDate = "2026-06-01", state = "new" });

        var board = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}"));
        var card = board.Columns.SelectMany(c => c.Tickets).Single();
        card.DueDate.Should().Be(new DateOnly(2026, 6, 1));
        card.IsOverdue.Should().BeTrue("the board card surfaces the computed isOverdue (§4.1 example)");
    }
}
