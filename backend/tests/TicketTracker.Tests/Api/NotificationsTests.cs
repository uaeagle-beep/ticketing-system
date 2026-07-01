using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Wave 2 notifications subsystem smoke coverage (WAVE2 §10 A–D, G, J; ADR-0012/0013). The FULL
/// acceptance suite is the Tester's after Phase 3 — these assert the load-bearing behaviours end-to-end
/// over real HTTP + in-process fan-out: actor exclusion, auto-watch, in-app list/read, activity per
/// event, watch/unwatch endpoints, settings, and that watch/activity never bump modified_at.
/// </summary>
public sealed class NotificationsTests : IntegrationTestBase
{
    // An admin who owns the team + a ticket, plus a helper to add a member to that team.
    private async Task<(HttpClient Admin, Guid AdminId, Guid TeamId, Guid TicketId)> SetupAsync()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));
        return (admin, adminId, team.Id, ticket.Id);
    }

    private async Task<(Guid UserId, HttpClient Client)> AddMemberAsync(Guid teamId, string? email = null)
    {
        var (token, userId, _) = await RegisterMemberAsync(email);
        await AddMembershipAsync(userId, teamId);
        return (userId, Authed(token));
    }

    // ---- A. Fan-out excludes the actor ----

    [Fact]
    public async Task Move_notifies_other_watchers_but_not_the_actor()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        // A and B both watch the ticket; A performs a move.
        var (_, a) = await AddMemberAsync(teamId);
        var (_, b) = await AddMemberAsync(teamId);
        (await a.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();
        (await b.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        (await a.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        // B (a watcher != actor) got exactly one ticket_moved; A (the actor) got none.
        var bList = await ReadAsync<NotificationListDto>(await b.GetAsync("/api/notifications"));
        bList.Items.Should().ContainSingle(n => n.EventType == "ticket_moved");
        bList.UnreadCount.Should().Be(1);

        var aList = await ReadAsync<NotificationListDto>(await a.GetAsync("/api/notifications"));
        aList.Items.Should().NotContain(n => n.EventType == "ticket_moved");
    }

    // ---- B. Auto-watch rules ----

    [Fact]
    public async Task Creator_is_auto_watched_but_not_self_notified()
    {
        var (admin, adminId, _, ticketId) = await SetupAsync();
        var watchers = await ReadAsync<WatchersDto>(await admin.GetAsync($"/api/tickets/{ticketId}/watchers"));
        watchers.Watching.Should().BeTrue("the creator is auto-watched (§6.3)");
        watchers.Watchers.Should().Contain(w => w.Id == adminId);

        // The creator gets no self-notification for ticket_created.
        var list = await ReadAsync<NotificationListDto>(await admin.GetAsync("/api/notifications"));
        list.Items.Should().NotContain(n => n.EventType == "ticket_created");
    }

    [Fact]
    public async Task Commenter_is_auto_watched_and_watchers_are_notified_of_the_comment()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (memberId, member) = await AddMemberAsync(teamId);

        (await member.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "Looking into it" }))
            .EnsureSuccessStatusCode();

        // The commenter is now a watcher.
        var watchers = await ReadAsync<WatchersDto>(await member.GetAsync($"/api/tickets/{ticketId}/watchers"));
        watchers.Watchers.Should().Contain(w => w.Id == memberId);

        // The admin (auto-watched as creator) is notified of the comment; the commenter is not.
        var adminList = await ReadAsync<NotificationListDto>(await admin.GetAsync("/api/notifications"));
        adminList.Items.Should().ContainSingle(n => n.EventType == "comment_added");
        var memberList = await ReadAsync<NotificationListDto>(await member.GetAsync("/api/notifications"));
        memberList.Items.Should().NotContain(n => n.EventType == "comment_added");
    }

    [Fact]
    public async Task Comment_edit_and_delete_write_activity_but_no_notification()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, member) = await AddMemberAsync(teamId);
        var comment = await ReadAsync<CommentDto>(await member.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments", new { body = "First" }));

        // Clear the admin's comment_added notification baseline count.
        var afterAdd = await ReadAsync<NotificationListDto>(await admin.GetAsync("/api/notifications"));
        var addedCount = afterAdd.Items.Count(n => n.EventType == "comment_added");

        (await member.PutAsJsonAsync($"/api/comments/{comment.Id}", new { body = "First (edited)" }))
            .EnsureSuccessStatusCode();
        (await member.DeleteAsync($"/api/comments/{comment.Id}")).EnsureSuccessStatusCode();

        // No comment_edited / comment_deleted notifications (activity-only, ADR-0015).
        var adminList = await ReadAsync<NotificationListDto>(await admin.GetAsync("/api/notifications"));
        adminList.Items.Should().NotContain(n => n.EventType == "comment_edited");
        adminList.Items.Should().NotContain(n => n.EventType == "comment_deleted");
        adminList.Items.Count(n => n.EventType == "comment_added").Should().Be(addedCount);

        // But BOTH appear in the ticket activity timeline.
        var activity = await ReadAsync<ActivityListDto>(await admin.GetAsync($"/api/tickets/{ticketId}/activity"));
        activity.Items.Should().Contain(a => a.EventType == "comment_edited");
        activity.Items.Should().Contain(a => a.EventType == "comment_deleted");
    }

    // ---- C. Stale watcher skipped ----

    [Fact]
    public async Task Stale_watcher_is_skipped_at_fanout_and_read()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (staleId, stale) = await AddMemberAsync(teamId);
        (await stale.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        // Remove the watcher's team membership directly (lost access).
        await Factory.WithDbAsync(async db =>
        {
            var m = db.UserTeams.First(x => x.UserId == staleId && x.TeamId == teamId);
            db.UserTeams.Remove(m);
            await db.SaveChangesAsync();
        });

        // A later event by the admin produces NO notification row for the stale watcher.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
        {
            var rows = db.Notifications.Where(n => n.RecipientId == staleId).ToList();
            rows.Should().BeEmpty("a watcher who lost team access is skipped at fan-out (§6.3)");
            var watch = db.TicketWatchers.Where(w => w.UserId == staleId && w.TicketId == ticketId).ToList();
            watch.Should().ContainSingle("the TicketWatcher row is preserved, not pruned");
            await Task.CompletedTask;
        });
    }

    // ---- D. In-app API ----

    [Fact]
    public async Task Mark_one_read_decrements_and_mark_all_read_zeroes()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddMemberAsync(teamId);
        (await watcher.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        // Two admin actions → two notifications for the watcher.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "hi" })).EnsureSuccessStatusCode();

        var count = await ReadAsync<UnreadCountDto>(await watcher.GetAsync("/api/notifications/unread-count"));
        count.UnreadCount.Should().Be(2);

        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        var afterOne = await ReadAsync<UnreadCountDto>(
            await watcher.PostAsync($"/api/notifications/{list.Items[0].Id}/read", null));
        afterOne.UnreadCount.Should().Be(1);

        var afterAll = await ReadAsync<UnreadCountDto>(await watcher.PostAsync("/api/notifications/read-all", null));
        afterAll.UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task Another_users_notification_id_is_404_self_scope()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddMemberAsync(teamId);
        (await watcher.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();

        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        var otherId = list.Items[0].Id;

        // A different member cannot mark someone else's notification read — 404 (self-owned masking).
        var (_, stranger) = await AddMemberAsync(teamId);
        var resp = await stranger.PostAsync($"/api/notifications/{otherId}/read", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_pagination_cursor_returns_next_page_without_overlap()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddMemberAsync(teamId);
        (await watcher.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        // Generate 3 notifications for the watcher.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "ready_for_acceptance" })).EnsureSuccessStatusCode();
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "note" })).EnsureSuccessStatusCode();

        var page1 = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications?limit=2"));
        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();

        var page2 = await ReadAsync<NotificationListDto>(
            await watcher.GetAsync($"/api/notifications?limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}"));
        page2.Items.Should().HaveCount(1);
        page2.HasMore.Should().BeFalse();
        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id));
    }

    // ---- G. Activity per event + team-scoping ----

    [Fact]
    public async Task Field_change_writes_one_activity_entry_per_field()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        // Change type + priority + title in one update → three ticket_field_changed entries.
        (await admin.PutAsJsonAsync($"/api/tickets/{ticketId}", new
        {
            teamId, type = "feature", title = "Login broken", body = "Steps",
            state = "new", priority = "high"
        })).EnsureSuccessStatusCode();

        var activity = await ReadAsync<ActivityListDto>(await admin.GetAsync($"/api/tickets/{ticketId}/activity"));
        activity.Items.Count(a => a.EventType == "ticket_field_changed").Should().Be(3);
        activity.Items.Should().Contain(a => a.EventType == "ticket_created");
    }

    [Fact]
    public async Task Activity_read_is_team_scoped()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();

        // A member of a DIFFERENT team cannot read this ticket's activity → 403.
        var (outsiderToken, outsiderId, _) = await RegisterMemberAsync();
        var otherTeam = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        await AddMembershipAsync(outsiderId, otherTeam.Id);
        var outsider = Authed(outsiderToken);

        (await outsider.GetAsync($"/api/tickets/{ticketId}/activity")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Unknown ticket → 404.
        (await admin.GetAsync($"/api/tickets/{Guid.NewGuid()}/activity")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Watch endpoints + J. modified_at unaffected ----

    [Fact]
    public async Task Watch_and_unwatch_are_idempotent_and_do_not_bump_modified_at()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (memberId, member) = await AddMemberAsync(teamId);

        var before = await ReadAsync<TicketDto>(await member.GetAsync($"/api/tickets/{ticketId}"));
        Factory.Clock.Advance(TimeSpan.FromMinutes(10));

        var w1 = await ReadAsync<WatchStatusDto>(await member.PostAsync($"/api/tickets/{ticketId}/watch", null));
        w1.Watching.Should().BeTrue();
        // Idempotent second watch is still 200 true.
        (await ReadAsync<WatchStatusDto>(await member.PostAsync($"/api/tickets/{ticketId}/watch", null)))
            .Watching.Should().BeTrue();

        var detail = await ReadAsync<TicketDto>(await member.GetAsync($"/api/tickets/{ticketId}"));
        detail.IsWatching.Should().BeTrue();
        detail.ModifiedAt.Should().Be(before.ModifiedAt, "watching never bumps modified_at (§J)");

        var unwatch = await ReadAsync<WatchStatusDto>(await member.DeleteAsync($"/api/tickets/{ticketId}/watch"));
        unwatch.Watching.Should().BeFalse();
        // Idempotent second unwatch is still 200 false.
        (await ReadAsync<WatchStatusDto>(await member.DeleteAsync($"/api/tickets/{ticketId}/watch")))
            .Watching.Should().BeFalse();
    }

    [Fact]
    public async Task Watch_endpoints_are_team_scoped()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (outsiderToken, outsiderId, _) = await RegisterMemberAsync();
        var otherTeam = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        await AddMembershipAsync(outsiderId, otherTeam.Id);
        var outsider = Authed(outsiderToken);

        (await outsider.PostAsync($"/api/tickets/{ticketId}/watch", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/tickets/{ticketId}/watchers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Notification settings (self) ----

    [Fact]
    public async Task Notification_settings_default_true_and_can_be_toggled()
    {
        var (admin, _, _, _) = await SetupAsync();

        var initial = await ReadAsync<NotificationSettingsDto>(await admin.GetAsync("/api/me/notification-settings"));
        initial.EmailNotificationsEnabled.Should().BeTrue();

        var updated = await ReadAsync<NotificationSettingsDto>(
            await admin.PutAsJsonAsync("/api/me/notification-settings", new { emailNotificationsEnabled = false }));
        updated.EmailNotificationsEnabled.Should().BeFalse();

        var reread = await ReadAsync<NotificationSettingsDto>(await admin.GetAsync("/api/me/notification-settings"));
        reread.EmailNotificationsEnabled.Should().BeFalse();
    }
}
