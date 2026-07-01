using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// F-12 comment edit/delete-own (WAVE2 §5.2, ADR-0015). Smoke coverage of the key behaviours; the
/// full acceptance suite is the Tester's. Edit is author-only (no admin override); delete is author
/// OR admin. Anti-IDOR ordering: resolve comment → team-access (403) → author/role gate (403);
/// unknown comment → 404. Blank/oversize body → 400. A no-op edit does NOT set editedAt.
/// Phase 1 raises no events (backbone is Phase 2).
/// </summary>
public sealed class CommentEditDeleteTests : IntegrationTestBase
{
    // A world with an admin who owns the team + ticket, and one authored comment by the given author.
    private async Task<(HttpClient Admin, Guid AdminId, Guid TeamId, Guid TicketId)> SetupAsync()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "T", body = "B" }));
        return (admin, adminId, team.Id, ticket.Id);
    }

    private async Task<(Guid UserId, HttpClient Client)> AddMemberToTeamAsync(Guid teamId, string? email = null)
    {
        var (token, userId, _) = await RegisterMemberAsync(email);
        await AddMembershipAsync(userId, teamId);
        return (userId, Authed(token));
    }

    private static async Task<CommentDto> AddCommentAsync(HttpClient client, Guid ticketId, string body)
        => await ReadAsync<CommentDto>(await client.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body }));

    [Fact]
    public async Task Author_edits_own_comment_sets_edited_and_editedAt()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberToTeamAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Original");

        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var resp = await member.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "  Updated  " });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var edited = await ReadAsync<CommentDto>(resp);
        edited.Body.Should().Be("Updated", "the new body is trimmed");
        edited.Edited.Should().BeTrue();
        edited.EditedAt.Should().Be(Factory.Clock.UtcNow);
    }

    [Fact]
    public async Task No_op_edit_does_not_set_editedAt()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberToTeamAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Same body");

        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        // Same content after trim => no persisted change, editedAt stays null.
        var edited = await ReadAsync<CommentDto>(
            await member.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "  Same body  " }));
        edited.Edited.Should().BeFalse();
        edited.EditedAt.Should().BeNull();
    }

    [Fact]
    public async Task Non_author_edit_is_403_even_for_admin()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberToTeamAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Member's words");

        // Admin is in the team's scope but is NOT the author => edit forbidden (no admin override, ADR-0015).
        var resp = await admin.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "rewritten" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Edit_with_blank_body_is_400(string body)
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberToTeamAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Original");

        var resp = await member.PutAsJsonAsync($"/api/comments/{created.Id}", new { body });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("body");
    }

    [Fact]
    public async Task Edit_unknown_comment_is_404()
    {
        var (admin, _, _, _) = await SetupAsync();
        var resp = await admin.PutAsJsonAsync($"/api/comments/{Guid.NewGuid()}", new { body = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Edit_by_member_of_another_team_is_403()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, author) = await AddMemberToTeamAsync(teamId);
        var created = await AddCommentAsync(author, ticketId, "Original");

        // Outsider: a member of a DIFFERENT team cannot even see the ticket => team-access 403 (not 404).
        var otherTeam = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        var (_, outsider) = await AddMemberToTeamAsync(otherTeam.Id);

        var resp = await outsider.PutAsJsonAsync($"/api/comments/{created.Id}", new { body = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Author_deletes_own_comment_is_204()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberToTeamAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Bye");

        var resp = await member.DeleteAsync($"/api/comments/{created.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await ReadAsync<List<CommentDto>>(await member.GetAsync($"/api/tickets/{ticketId}/comments"));
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_deletes_another_users_comment_is_204()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberToTeamAsync(teamId);
        var created = await AddCommentAsync(member, ticketId, "Member's words");

        // Admin override (moderation) — allowed for delete (ADR-0015).
        var resp = await admin.DeleteAsync($"/api/comments/{created.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Non_author_non_admin_delete_is_403()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, author) = await AddMemberToTeamAsync(teamId);
        var created = await AddCommentAsync(author, ticketId, "Mine");

        var (_, other) = await AddMemberToTeamAsync(teamId);
        var resp = await other.DeleteAsync($"/api/comments/{created.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_unknown_comment_is_404()
    {
        var (admin, _, _, _) = await SetupAsync();
        var resp = await admin.DeleteAsync($"/api/comments/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
