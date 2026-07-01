using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite — comment edit/delete (F-12), extending the developer smoke tests (WAVE2 §10 F,
/// §5.2, ADR-0015). Presses gaps: oversize body → 400 keyed body; a real edit stamps edited_at and writes
/// comment_edited ACTIVITY only (no notification); a no-op edit writes NO activity and does NOT advance
/// edited_at; a second real edit re-advances edited_at while createdAt is immutable; delete on a foreign-team
/// ticket is 403 (team access) BEFORE the author/admin gate; delete of an unknown comment is 404; an admin
/// deleting another user's comment writes comment_deleted activity attributed to the ADMIN as actor.
/// </summary>
public sealed class CommentEditDeleteAcceptanceTests : IntegrationTestBase
{
    private async Task<(HttpClient Admin, Guid AdminId, Guid TeamId, Guid TicketId)> SetupAsync()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "T", body = "B" }));
        return (admin, adminId, team.Id, ticket.Id);
    }

    private async Task<(Guid UserId, HttpClient Client)> AddMemberAsync(Guid teamId, string? email = null)
    {
        var (token, userId, _) = await RegisterMemberAsync(email);
        await AddMembershipAsync(userId, teamId);
        return (userId, Authed(token));
    }

    private static async Task<CommentDto> AddCommentAsync(HttpClient client, Guid ticketId, string body)
        => await ReadAsync<CommentDto>(await client.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body }));

    private static async Task<ActivityListDto> ActivityAsync(HttpClient client, Guid ticketId)
        => await ReadAsync<ActivityListDto>(await client.GetAsync($"/api/tickets/{ticketId}/activity?limit=100"));

    [Fact]
    public async Task Oversize_body_edit_is_400_keyed_body()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Original");

        var oversize = new string('x', 20_001); // CommentBodyMax = 20_000
        var resp = await member.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = oversize });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("body");
    }

    [Fact]
    public async Task Real_edit_stamps_edited_at_and_writes_comment_edited_activity()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Original");

        Factory.Clock.Advance(TimeSpan.FromMinutes(3));
        var editedAt1 = Factory.Clock.UtcNow;
        var edited = await ReadAsync<CommentDto>(
            await member.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "Rewritten" }));
        edited.Edited.Should().BeTrue();
        edited.EditedAt.Should().Be(editedAt1);
        edited.CreatedAt.Should().Be(created.CreatedAt, "createdAt is immutable across edits");

        var activity = await ActivityAsync(admin, ticketId);
        activity.Items.Count(a => a.EventType == "comment_edited").Should().Be(1);
    }

    [Fact]
    public async Task No_op_edit_writes_no_activity_and_does_not_advance_edited_at()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Same body");
        var baselineActivity = (await ActivityAsync(admin, ticketId)).Items.Count(a => a.EventType == "comment_edited");

        Factory.Clock.Advance(TimeSpan.FromMinutes(3));
        var noop = await ReadAsync<CommentDto>(
            await member.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "  Same body  " }));
        noop.Edited.Should().BeFalse();
        noop.EditedAt.Should().BeNull();

        (await ActivityAsync(admin, ticketId)).Items.Count(a => a.EventType == "comment_edited")
            .Should().Be(baselineActivity, "a no-op edit raises no event, so no comment_edited activity");
    }

    [Fact]
    public async Task Second_real_edit_re_advances_edited_at()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "v1");

        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var first = await ReadAsync<CommentDto>(await member.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "v2" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var secondAt = Factory.Clock.UtcNow;
        var second = await ReadAsync<CommentDto>(await member.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "v3" }));

        second.EditedAt.Should().Be(secondAt);
        second.EditedAt.Should().BeAfter(first.EditedAt!.Value, "each real edit re-advances edited_at");
    }

    [Fact]
    public async Task Delete_on_foreign_team_ticket_is_403_before_author_gate()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, author) = await AddMemberAsync(teamId);
        var created = await AddCommentAsync(author, ticketId, "Mine");

        var otherTeam = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        var (_, outsider) = await AddMemberAsync(otherTeam.Id);

        // The outsider cannot even see the ticket → 403 (team access), NOT the author-gate 403 or a 404.
        (await outsider.DeleteAsync($"/api/comments/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_delete_of_another_users_comment_writes_activity_attributed_to_admin()
    {
        var (admin, adminId, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Member's words");

        (await admin.DeleteAsync($"/api/comments/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var activity = await ActivityAsync(admin, ticketId);
        var del = activity.Items.Should().ContainSingle(a => a.EventType == "comment_deleted").Subject;
        del.ActorId.Should().Be(adminId, "the actor of a moderation delete is the admin, not the comment author");
    }

    [Fact]
    public async Task Author_can_delete_own_and_admin_can_delete_but_neither_edits_others()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberAsync(teamId);
        var c1 = await AddCommentAsync(member, ticketId, "one");
        var c2 = await AddCommentAsync(member, ticketId, "two");

        // Admin cannot edit the member's comment (no edit override).
        (await admin.PutAsJsonAsync($"/api/comments/{c1.Id}", new { body = "hijacked" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Author can delete their own.
        (await member.DeleteAsync($"/api/comments/{c1.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        // Admin can delete the other one (moderation override).
        (await admin.DeleteAsync($"/api/comments/{c2.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
