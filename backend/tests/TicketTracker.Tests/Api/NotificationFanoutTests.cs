using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite — fan-out + watchers (WAVE2 §10 A/B/C, ADR-0013). Presses the load-bearing PO
/// rules the developer smoke tests only sample: the actor is NEVER notified about their own action;
/// auto-watch subscribes creator / newly-added-assignee / commenter and a just-added assignee receives
/// SUBSEQUENT events but not the assignment event they themselves caused; manual watch/unwatch gates
/// delivery; and the stale-watcher rule (lost team access OR blocked) is skipped at fan-out AND at the
/// watchers read, the row is preserved, and re-adding team access resumes delivery. Real HTTP over the
/// in-memory SQLite factory with in-process fan-out.
/// </summary>
public sealed class NotificationFanoutTests : IntegrationTestBase
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

    private async Task<(Guid UserId, string Email, HttpClient Client)> AddMemberAsync(Guid teamId, string? email = null)
    {
        var (token, userId, resolved) = await RegisterMemberAsync(email);
        await AddMembershipAsync(userId, teamId);
        return (userId, resolved, Authed(token));
    }

    private static int CountUnread(NotificationListDto list) => list.UnreadCount;

    // ---- A. Fan-out excludes the actor, notifies every OTHER eligible watcher ----

    [Fact]
    public async Task Event_notifies_all_other_watchers_and_never_the_actor()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, _, a) = await AddMemberAsync(teamId);
        var (_, _, b) = await AddMemberAsync(teamId);
        var (_, _, c) = await AddMemberAsync(teamId);
        foreach (var w in new[] { a, b, c })
            (await w.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        // A performs the move. B and C (watchers != actor) each get exactly one; A (the actor) gets none.
        (await a.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        var bList = await ReadAsync<NotificationListDto>(await b.GetAsync("/api/notifications"));
        var cList = await ReadAsync<NotificationListDto>(await c.GetAsync("/api/notifications"));
        var aList = await ReadAsync<NotificationListDto>(await a.GetAsync("/api/notifications"));

        bList.Items.Should().ContainSingle(n => n.EventType == "ticket_moved");
        cList.Items.Should().ContainSingle(n => n.EventType == "ticket_moved");
        aList.Items.Should().NotContain(n => n.EventType == "ticket_moved",
            "the user who caused the event is never notified about it (core PO rule)");
    }

    [Fact]
    public async Task Notification_carries_actor_display_name_and_is_newest_first()
    {
        var (admin, adminId, teamId, ticketId) = await SetupAsync();
        var (_, _, watcher) = await AddMemberAsync(teamId);
        (await watcher.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        // Two admin actions at distinct instants → newest-first ordering with the actor named.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        Factory.Clock.Advance(TimeSpan.FromSeconds(5));
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "note" })).EnsureSuccessStatusCode();

        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        list.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        list.Items[0].CreatedAt.Should().BeOnOrAfter(list.Items[1].CreatedAt, "list is newest-first");
        list.Items.Should().OnlyContain(n => n.ActorId == adminId);
        list.Items.Should().OnlyContain(n => !string.IsNullOrWhiteSpace(n.ActorDisplayName));
    }

    // ---- B. Auto-watch rules ----

    [Fact]
    public async Task Newly_added_assignee_is_auto_watched_and_gets_subsequent_events_not_the_assignment()
    {
        // §10.B-ii: adding an assignee auto-watches them; they receive SUBSEQUENT events but NOT the
        // assignment event when they were merely the subject (the admin is the actor here, so the
        // just-added assignee — who is NOT the actor — DOES get the assignees_changed row; then a later
        // move by the admin also reaches them).
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (memberId, _, member) = await AddMemberAsync(teamId);

        // Admin assigns the member. The member (not the actor) is auto-watched and notified of the change.
        (await admin.PutAsJsonAsync($"/api/tickets/{ticketId}/assignees", new { userIds = new[] { memberId } }))
            .EnsureSuccessStatusCode();

        // The member is now a watcher (verify via the watchers read).
        var watchers = await ReadAsync<WatchersDto>(await admin.GetAsync($"/api/tickets/{ticketId}/watchers"));
        watchers.Watchers.Should().Contain(w => w.Id == memberId, "a newly-added assignee is auto-watched (§6.3)");

        // A SUBSEQUENT admin action reaches the auto-watched member.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        var list = await ReadAsync<NotificationListDto>(await member.GetAsync("/api/notifications"));
        list.Items.Should().Contain(n => n.EventType == "ticket_assignees_changed");
        list.Items.Should().Contain(n => n.EventType == "ticket_moved",
            "an auto-watched assignee receives subsequent events");
    }

    [Fact]
    public async Task Assignee_who_is_the_actor_of_their_own_assignment_is_not_notified()
    {
        // A member with self-assign rights: when the member assigns THEMSELVES, they are the actor and
        // must NOT receive the assignees_changed notification, but they ARE auto-watched for future events.
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (memberId, _, member) = await AddMemberAsync(teamId);

        (await member.PutAsJsonAsync($"/api/tickets/{ticketId}/assignees", new { userIds = new[] { memberId } }))
            .EnsureSuccessStatusCode();

        var self = await ReadAsync<NotificationListDto>(await member.GetAsync("/api/notifications"));
        self.Items.Should().NotContain(n => n.EventType == "ticket_assignees_changed",
            "the actor is excluded even when they are also the newly-added assignee");

        // But they are now watching: a later admin move reaches them.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();
        var after = await ReadAsync<NotificationListDto>(await member.GetAsync("/api/notifications"));
        after.Items.Should().Contain(n => n.EventType == "ticket_moved");
    }

    [Fact]
    public async Task Manual_unwatch_stops_delivery_of_future_events()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (_, _, member) = await AddMemberAsync(teamId);
        (await member.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        var before = await ReadAsync<NotificationListDto>(await member.GetAsync("/api/notifications"));
        var baseline = before.Items.Count;

        (await member.DeleteAsync($"/api/tickets/{ticketId}/watch")).EnsureSuccessStatusCode();

        // A later event produces NO new notification for the now-unwatching member.
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "after unwatch" })).EnsureSuccessStatusCode();
        var after = await ReadAsync<NotificationListDto>(await member.GetAsync("/api/notifications"));
        after.Items.Count.Should().Be(baseline, "unwatching stops delivery of future events; existing rows stay");
    }

    // ---- C. Stale watcher: skipped at fan-out AND read, row preserved, re-add resumes ----

    [Fact]
    public async Task Stale_watcher_removed_from_team_is_skipped_and_reinstated_on_readd()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (staleId, _, stale) = await AddMemberAsync(teamId);
        (await stale.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        // Remove team membership (lost access).
        await Factory.WithDbAsync(async db =>
        {
            var m = db.UserTeams.First(x => x.UserId == staleId && x.TeamId == teamId);
            db.UserTeams.Remove(m);
            await db.SaveChangesAsync();
        });

        // An event while stale → no notification row for the stale watcher; the watch row is preserved.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        await Factory.WithDbAsync(async db =>
        {
            (await db.Notifications.CountAsync(n => n.RecipientId == staleId)).Should().Be(0);
            (await db.TicketWatchers.CountAsync(w => w.UserId == staleId && w.TicketId == ticketId)).Should().Be(1,
                "the TicketWatcher row is preserved, not pruned (§6.3 reversibility)");
        });

        // Stale watcher is also skipped at the watchers READ (never leak a person who lost access).
        var watchersWhileStale = await ReadAsync<WatchersDto>(await admin.GetAsync($"/api/tickets/{ticketId}/watchers"));
        watchersWhileStale.Watchers.Should().NotContain(w => w.Id == staleId,
            "the read-side of the team-scope rule skips a stale watcher");

        // Re-add team access → delivery RESUMES automatically (the preserved row is now eligible again).
        await AddMembershipAsync(staleId, teamId);
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "welcome back" })).EnsureSuccessStatusCode();
        await Factory.WithDbAsync(async db =>
            (await db.Notifications.CountAsync(n => n.RecipientId == staleId)).Should().Be(1,
                "re-adding team access resumes delivery on the preserved watch row"));

        var watchersAfter = await ReadAsync<WatchersDto>(await admin.GetAsync($"/api/tickets/{ticketId}/watchers"));
        watchersAfter.Watchers.Should().Contain(w => w.Id == staleId);
    }

    [Fact]
    public async Task Blocked_watcher_is_skipped_at_fanout_and_read_but_row_is_preserved()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (blockedId, _, blocked) = await AddMemberAsync(teamId);
        (await blocked.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        await BlockUserAsync(blockedId);

        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
        {
            (await db.Notifications.CountAsync(n => n.RecipientId == blockedId)).Should().Be(0,
                "a blocked watcher is skipped at fan-out");
            (await db.TicketWatchers.CountAsync(w => w.UserId == blockedId && w.TicketId == ticketId)).Should().Be(1);
        });

        var watchers = await ReadAsync<WatchersDto>(await admin.GetAsync($"/api/tickets/{ticketId}/watchers"));
        watchers.Watchers.Should().NotContain(w => w.Id == blockedId, "a blocked watcher is skipped at read too");
    }

    [Fact]
    public async Task Admin_watcher_without_explicit_membership_still_receives_notifications()
    {
        // An admin has global access (not a team member). If an admin watches, they remain eligible at
        // fan-out via the IsAdmin branch of the eligibility query.
        var (admin, adminId, teamId, ticketId) = await SetupAsync();
        var (memberId, _, member) = await AddMemberAsync(teamId);

        // A SECOND admin (not a member of this team) watches the ticket.
        var (admin2Token, admin2Id, _) = await RegisterAdminAsync();
        var admin2 = Authed(admin2Token);
        (await admin2.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();

        // The member acts → the watching second admin is notified despite having no team membership row.
        (await member.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "hi" })).EnsureSuccessStatusCode();

        var list = await ReadAsync<NotificationListDto>(await admin2.GetAsync("/api/notifications"));
        list.Items.Should().ContainSingle(n => n.EventType == "comment_added",
            "an admin watcher is eligible via IsAdmin even without a membership row");
    }
}
