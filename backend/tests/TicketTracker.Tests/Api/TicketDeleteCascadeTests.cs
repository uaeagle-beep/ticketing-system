using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite — ticket-delete cascades + notification tombstone (WAVE2 §4.1/§6.6). On a ticket
/// delete under SQLite (PRAGMA foreign_keys=ON): ticket_labels, ticket_watchers, ticket_assignees and
/// activity_entries are removed; comments are removed; but the fresh ticket_deleted NOTIFICATIONS survive
/// with ticket_id SET NULL (a non-navigable tombstone whose self-contained summary still names the ticket).
/// This exercises the single most important schema subtlety of Wave 2 (R-1). The Postgres-only ordering
/// concern (fan-out publishes before removal) is exercised on the PG/deploy path and is called out in the
/// QA report, not here.
/// </summary>
public sealed class TicketDeleteCascadeTests : IntegrationTestBase
{
    [Fact]
    public async Task Delete_removes_all_associations_but_keeps_the_notification_tombstone()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));

        // A watcher who is NOT the actor, so a surviving ticket_deleted notification is created for them.
        var (watcherToken, watcherId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(watcherId, team.Id);
        var watcher = Authed(watcherToken);

        // An assignable member.
        var (_, assigneeId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(assigneeId, team.Id);

        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));

        // Wire up associations: a label, an assignee, a watcher, a comment, and some activity.
        var label = await ReadAsync<LabelDto>(await admin.PostAsJsonAsync("/api/labels",
            new { teamId = team.Id, name = "Bug", color = "#3b82f6" }));
        (await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels", new { labelIds = new[] { label.Id } })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/assignees", new { userIds = new[] { assigneeId } })).EnsureSuccessStatusCode();
        (await watcher.PostAsync($"/api/tickets/{ticket.Id}/watch", null)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "note" })).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();

        // Pre-conditions: the associations exist.
        await Factory.WithDbAsync(async db =>
        {
            (await db.TicketLabels.CountAsync(x => x.TicketId == ticket.Id)).Should().BeGreaterThan(0);
            (await db.TicketAssignees.CountAsync(x => x.TicketId == ticket.Id)).Should().BeGreaterThan(0);
            (await db.TicketWatchers.CountAsync(x => x.TicketId == ticket.Id)).Should().BeGreaterThan(0);
            (await db.Comments.CountAsync(x => x.TicketId == ticket.Id)).Should().BeGreaterThan(0);
            (await db.ActivityEntries.CountAsync(x => x.TicketId == ticket.Id)).Should().BeGreaterThan(0);
        });

        (await admin.DeleteAsync($"/api/tickets/{ticket.Id}")).EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
        {
            (await db.Tickets.CountAsync(x => x.Id == ticket.Id)).Should().Be(0);
            (await db.TicketLabels.CountAsync(x => x.TicketId == ticket.Id)).Should().Be(0, "ticket_labels cascade away");
            (await db.TicketAssignees.CountAsync(x => x.TicketId == ticket.Id)).Should().Be(0, "ticket_assignees cascade away");
            (await db.TicketWatchers.CountAsync(x => x.TicketId == ticket.Id)).Should().Be(0, "ticket_watchers cascade away");
            (await db.Comments.CountAsync(x => x.TicketId == ticket.Id)).Should().Be(0, "comments cascade away");
            (await db.ActivityEntries.CountAsync(x => x.TicketId == ticket.Id)).Should().Be(0, "activity_entries cascade away");

            // The label itself is untouched (it belongs to the team, not the ticket).
            (await db.Labels.CountAsync(x => x.Id == label.Id)).Should().Be(1, "the label survives; only its tag was removed");

            // The ticket_deleted notification for the watcher SURVIVES as a tombstone with ticket_id = null.
            var tombstones = await db.Notifications
                .Where(n => n.RecipientId == watcherId && n.EventType == "ticket_deleted")
                .ToListAsync();
            tombstones.Should().ContainSingle("the ticket_deleted notification outlives its ticket (SET NULL, §6.6)");
            tombstones[0].TicketId.Should().BeNull("the FK SET-NULLs when the ticket row is removed");
            tombstones[0].Summary.Should().Contain("Login fails", "the tombstone summary is self-contained");
        });
    }

    [Fact]
    public async Task Deleting_a_ticket_leaves_other_tickets_labels_intact_no_orphans()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var label = await ReadAsync<LabelDto>(await admin.PostAsJsonAsync("/api/labels",
            new { teamId = team.Id, name = "Shared", color = "#22c55e" }));

        var t1 = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "One", body = "B" }));
        var t2 = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Two", body = "B" }));
        (await admin.PutAsJsonAsync($"/api/tickets/{t1.Id}/labels", new { labelIds = new[] { label.Id } })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/tickets/{t2.Id}/labels", new { labelIds = new[] { label.Id } })).EnsureSuccessStatusCode();

        (await admin.DeleteAsync($"/api/tickets/{t1.Id}")).EnsureSuccessStatusCode();

        // t1's tag is gone; t2 still carries the shared label; no orphan ticket_labels rows remain.
        var detail2 = await ReadAsync<TicketDto>(await admin.GetAsync($"/api/tickets/{t2.Id}"));
        detail2.Labels.Should().ContainSingle(l => l.Id == label.Id);
        await Factory.WithDbAsync(async db =>
            (await db.TicketLabels.CountAsync(tl => tl.TicketId == t1.Id)).Should().Be(0));
    }
}
