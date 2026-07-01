using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite — per-ticket activity timeline (WAVE2 §10 G, ADR-0012). Presses the exact-cardinality
/// guarantees: exactly one ActivityEntry per event, one per changed field on a multi-field update, ticket_moved
/// as its OWN entry (distinct from field-changed), ticket_created on create, comment add/edit/delete each writing
/// one entry (edit/delete = activity but NO notification), assignees-changed writing one entry; correct
/// eventType + human summary; newest-first ordering; keyset pagination with no overlap/gap even when a new row is
/// inserted between page reads; team-scoped read (403 for a non-member, 404 for an unknown ticket); and that
/// deleting the ticket cascades its activity away.
/// </summary>
public sealed class ActivityTimelineTests : IntegrationTestBase
{
    private async Task<(HttpClient Admin, Guid AdminId, Guid TeamId, Guid TicketId)> SetupAsync()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));
        return (admin, adminId, team.Id, ticket.Id);
    }

    private static async Task<ActivityListDto> AllActivityAsync(HttpClient client, Guid ticketId)
        => await ReadAsync<ActivityListDto>(await client.GetAsync($"/api/tickets/{ticketId}/activity?limit=100"));

    [Fact]
    public async Task Create_writes_exactly_one_ticket_created_entry()
    {
        var (admin, adminId, _, ticketId) = await SetupAsync();
        var activity = await AllActivityAsync(admin, ticketId);

        activity.Items.Count(a => a.EventType == "ticket_created").Should().Be(1);
        var created = activity.Items.Single(a => a.EventType == "ticket_created");
        created.ActorId.Should().Be(adminId);
        created.Summary.Should().Contain("created this ticket");
    }

    [Fact]
    public async Task Multi_field_update_writes_one_entry_per_field_and_move_is_its_own_entry()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();

        // Change state (move) + type + priority + title in ONE update.
        (await admin.PutAsJsonAsync($"/api/tickets/{ticketId}", new
        {
            teamId,
            type = "feature",
            title = "Login broken on Safari",
            body = "Steps",
            state = "in_progress",
            priority = "high"
        })).EnsureSuccessStatusCode();

        var activity = await AllActivityAsync(admin, ticketId);

        // ticket_moved is its OWN entry, separate from the three field-changed entries (title/type/priority).
        activity.Items.Count(a => a.EventType == "ticket_moved").Should().Be(1, "state change is its own event");
        activity.Items.Count(a => a.EventType == "ticket_field_changed").Should().Be(3,
            "one field-changed entry per changed scalar (title, type, priority) — state is NOT counted here");

        // The move summary names both states in display case.
        activity.Items.Single(a => a.EventType == "ticket_moved").Summary
            .Should().Contain("New").And.Contain("In progress");
        // A field-changed summary reads naturally.
        activity.Items.Should().Contain(a => a.EventType == "ticket_field_changed" && a.Summary.Contains("priority"));
    }

    [Fact]
    public async Task No_op_update_writes_no_activity()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var baseline = (await AllActivityAsync(admin, ticketId)).Items.Count;

        // Re-submit identical field values → no change, no new activity.
        (await admin.PutAsJsonAsync($"/api/tickets/{ticketId}", new
        {
            teamId, type = "bug", title = "Login fails", body = "Steps", state = "new", priority = "medium"
        })).EnsureSuccessStatusCode();

        (await AllActivityAsync(admin, ticketId)).Items.Count.Should().Be(baseline,
            "a no-op update raises no events, so no activity is written");
    }

    [Fact]
    public async Task Comment_add_edit_delete_each_write_one_activity_entry()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var comment = await ReadAsync<CommentDto>(await admin.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments", new { body = "First" }));

        (await admin.PutAsJsonAsync($"/api/comments/{comment.Id}", new { body = "First (edited)" })).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/comments/{comment.Id}")).EnsureSuccessStatusCode();

        var activity = await AllActivityAsync(admin, ticketId);
        activity.Items.Count(a => a.EventType == "comment_added").Should().Be(1);
        activity.Items.Count(a => a.EventType == "comment_edited").Should().Be(1);
        activity.Items.Count(a => a.EventType == "comment_deleted").Should().Be(1);
    }

    [Fact]
    public async Task Comment_edit_and_delete_produce_activity_but_no_notification()
    {
        // A watcher (not the actor) confirms comment_edited/deleted are activity-only.
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (watcherToken, watcherId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(watcherId, teamId);
        var watcher = Authed(watcherToken);
        (await watcher.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        var comment = await ReadAsync<CommentDto>(await admin.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments", new { body = "First" }));
        (await admin.PutAsJsonAsync($"/api/comments/{comment.Id}", new { body = "Edited" })).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/comments/{comment.Id}")).EnsureSuccessStatusCode();

        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        list.Items.Should().Contain(n => n.EventType == "comment_added", "comment_added notifies watchers");
        list.Items.Should().NotContain(n => n.EventType == "comment_edited");
        list.Items.Should().NotContain(n => n.EventType == "comment_deleted");
    }

    [Fact]
    public async Task Assignees_changed_writes_one_activity_entry()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (memberToken, memberId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(memberId, teamId);

        (await admin.PutAsJsonAsync($"/api/tickets/{ticketId}/assignees", new { userIds = new[] { memberId } }))
            .EnsureSuccessStatusCode();

        var activity = await AllActivityAsync(admin, ticketId);
        activity.Items.Count(a => a.EventType == "ticket_assignees_changed").Should().Be(1);
    }

    [Fact]
    public async Task Timeline_is_newest_first()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        Factory.Clock.Advance(TimeSpan.FromSeconds(10));
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();

        var activity = await AllActivityAsync(admin, ticketId);
        activity.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        // Descending by createdAt.
        for (var i = 1; i < activity.Items.Count; i++)
            activity.Items[i - 1].CreatedAt.Should().BeOnOrAfter(activity.Items[i].CreatedAt);
        activity.Items[0].EventType.Should().Be("ticket_moved", "the most recent event is first");
    }

    [Fact]
    public async Task Keyset_pagination_has_no_overlap_or_gap_even_with_an_interleaved_insert()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();

        // Produce several distinct-timestamp activity entries (plus the create entry).
        for (var i = 0; i < 4; i++)
        {
            Factory.Clock.Advance(TimeSpan.FromSeconds(1));
            var state = i % 2 == 0 ? "in_progress" : "new";
            (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state })).EnsureSuccessStatusCode();
        }

        var page1 = await ReadAsync<ActivityListDto>(await admin.GetAsync($"/api/tickets/{ticketId}/activity?limit=2"));
        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();

        // Insert a NEW activity entry AFTER page 1 was read (a newer row than the whole first page).
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "done" })).EnsureSuccessStatusCode();

        // Page 2 continues strictly AFTER page 1's cursor position — the new (newer) row does not appear
        // here (keyset is stable), and there is no overlap with page 1.
        var page2 = await ReadAsync<ActivityListDto>(
            await admin.GetAsync($"/api/tickets/{ticketId}/activity?limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}"));

        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id),
            "keyset pagination never re-serves a page-1 row on page 2");
        // The freshly-inserted newest row is not smuggled into a later page.
        page2.Items.Should().NotContain(a => a.EventType == "ticket_moved" && a.Summary.Contains("Done"));
    }

    [Fact]
    public async Task Activity_read_is_team_scoped_403_and_unknown_ticket_404()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();

        // Member of a DIFFERENT team → 403 (resolve-then-check, not 404).
        var otherTeam = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        var (outsiderToken, outsiderId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(outsiderId, otherTeam.Id);
        var outsider = Authed(outsiderToken);

        (await outsider.GetAsync($"/api/tickets/{ticketId}/activity")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/api/tickets/{Guid.NewGuid()}/activity")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.GetAsync($"/api/tickets/{ticketId}/activity")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deleting_the_ticket_cascades_its_activity_away()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
            (await db.ActivityEntries.CountAsync(a => a.TicketId == ticketId)).Should().BeGreaterThan(0));

        (await admin.DeleteAsync($"/api/tickets/{ticketId}")).EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
            (await db.ActivityEntries.CountAsync(a => a.TicketId == ticketId)).Should().Be(0,
                "a ticket's timeline has no meaning without the ticket and cascades away (§7bis)"));
    }
}
