using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Comments over real HTTP (E5, API_CONTRACT §7). Covers add (server-set author + UTC created_at),
/// empty body rejection (400), oldest-first ordering, and the rule that adding a comment must NOT
/// advance the ticket's modified_at (V21) — and therefore must not reorder it on the board (EC8).
/// </summary>
public sealed class CommentsTests : IntegrationTestBase
{

    private sealed record Ctx(HttpClient Client, Guid UserId, Guid TeamId);

    private async Task<Ctx> SetupAsync()
    {
        var (token, userId, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        return new Ctx(client, userId, team.Id);
    }

    private async Task<TicketDto> CreateTicketAsync(Ctx ctx, string title = "T")
        => await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title, body = "B" }));

    [Fact]
    public async Task Add_comment_sets_author_and_created_at_and_returns_201()
    {
        var ctx = await SetupAsync();
        var ticket = await CreateTicketAsync(ctx);

        var resp = await ctx.Client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "  Looks fixed.  " });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var comment = await ReadAsync<CommentDto>(resp);
        comment.Body.Should().Be("Looks fixed.", "body is trimmed");
        comment.AuthorId.Should().Be(ctx.UserId, "author is the authenticated user (V23, A20)");
        comment.AuthorEmail.Should().NotBeNullOrEmpty();
        comment.TicketId.Should().Be(ticket.Id);
        comment.CreatedAt.Should().Be(Factory.Clock.UtcNow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_blank_comment_is_400_and_creates_nothing(string body)
    {
        var ctx = await SetupAsync();
        var ticket = await CreateTicketAsync(ctx);

        var resp = await ctx.Client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("body");

        var comments = await ReadAsync<List<CommentDto>>(
            await ctx.Client.GetAsync($"/api/tickets/{ticket.Id}/comments"));
        comments.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_comment_to_unknown_ticket_is_404()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync($"/api/tickets/{Guid.NewGuid()}/comments", new { body = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Comments_are_listed_oldest_first()
    {
        var ctx = await SetupAsync();
        var ticket = await CreateTicketAsync(ctx);

        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 30, 10, 00, 00, DateTimeKind.Utc));
        await ctx.Client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "first" });
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await ctx.Client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "second" });
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await ctx.Client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "third" });

        var comments = await ReadAsync<List<CommentDto>>(
            await ctx.Client.GetAsync($"/api/tickets/{ticket.Id}/comments"));
        comments.Select(c => c.Body).Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public async Task Empty_comment_list_returns_empty_array()
    {
        var ctx = await SetupAsync();
        var ticket = await CreateTicketAsync(ctx);
        var comments = await ReadAsync<List<CommentDto>>(
            await ctx.Client.GetAsync($"/api/tickets/{ticket.Id}/comments"));
        comments.Should().BeEmpty();
    }

    [Fact]
    public async Task Adding_a_comment_does_not_change_ticket_modified_at_or_board_order()
    {
        var ctx = await SetupAsync();

        // Two tickets created in order; advance clock between so they have distinct modified_at.
        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 30, 10, 00, 00, DateTimeKind.Utc));
        var older = await CreateTicketAsync(ctx, "Older");
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var newer = await CreateTicketAsync(ctx, "Newer");

        // Board is ordered modified desc: Newer before Older.
        var before = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}"));
        var newColumnBefore = before.Columns.Single(c => c.State == "new").Tickets.Select(t => t.Title).ToList();
        newColumnBefore.Should().ContainInOrder("Newer", "Older");

        // Comment on the OLDER ticket much later. If modified_at moved, ordering would flip.
        Factory.Clock.Advance(TimeSpan.FromHours(1));
        var reloadOlderBefore = await ReadAsync<TicketDto>(await ctx.Client.GetAsync($"/api/tickets/{older.Id}"));
        await ctx.Client.PostAsJsonAsync($"/api/tickets/{older.Id}/comments", new { body = "late comment" });
        var reloadOlderAfter = await ReadAsync<TicketDto>(await ctx.Client.GetAsync($"/api/tickets/{older.Id}"));

        reloadOlderAfter.ModifiedAt.Should().Be(reloadOlderBefore.ModifiedAt,
            "adding a comment must not touch ticket.modified_at (V21)");

        var after = await ReadAsync<BoardDto>(await ctx.Client.GetAsync($"/api/tickets?teamId={ctx.TeamId}"));
        var newColumnAfter = after.Columns.Single(c => c.State == "new").Tickets.Select(t => t.Title).ToList();
        newColumnAfter.Should().ContainInOrder("Newer", "Older");
        newColumnAfter.Should().Equal(newColumnBefore, "the board order is unchanged after commenting (EC8)");
        _ = newer; // referenced for clarity
    }
}
