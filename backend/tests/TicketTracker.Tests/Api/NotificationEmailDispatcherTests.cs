using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Deterministic email outbox drain tests (WAVE2 §10.E, ADR-0014). The hosted worker is removed by the
/// factory (§7.5), so tests drive <c>NotificationEmailDispatcher.DrainOnceAsync(Factory.Clock.UtcNow, …)</c>
/// directly via <see cref="IntegrationTestBase.DrainNotificationEmailsAsync"/> with the fake clock +
/// fake sender: debounce, coalescing, idempotency, email-off/blocked skip, and the ticket_deleted tombstone.
/// </summary>
public sealed class NotificationEmailDispatcherTests : IntegrationTestBase
{
    private async Task<(HttpClient Admin, Guid TeamId, Guid TicketId)> SetupAsync()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));
        return (admin, team.Id, ticket.Id);
    }

    private async Task<(Guid UserId, string Email, HttpClient Client)> AddWatchingMemberAsync(Guid teamId, Guid ticketId)
    {
        var (token, userId, email) = await RegisterMemberAsync();
        await AddMembershipAsync(userId, teamId);
        var client = Authed(token);
        (await client.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();
        return (userId, email, client);
    }

    [Fact]
    public async Task Burst_within_window_is_debounced_then_coalesced_into_one_digest_and_idempotent()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, watcherEmail, _) = await AddWatchingMemberAsync(teamId, ticketId);

        // Three events for the one watcher within the debounce window.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "one" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "two" })).EnsureSuccessStatusCode();

        // Drain now (within 60s window) → nothing sent yet (debounced).
        var sentNow = await DrainNotificationEmailsAsync();
        sentNow.Should().Be(0, "notifications younger than the 60s debounce window are not emailed yet");
        Factory.Email.Digests.Should().BeEmpty();

        // Advance past the window and drain → exactly one digest with three coalesced lines.
        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var sentAfter = await DrainNotificationEmailsAsync();
        sentAfter.Should().Be(1);
        var digests = Factory.Email.DigestsFor(watcherEmail);
        digests.Should().ContainSingle();
        digests[0].Lines.Should().HaveCount(3, "a rapid burst coalesces into one email with N lines");

        // A second drain sends nothing (idempotent via emailed_at).
        var sentAgain = await DrainNotificationEmailsAsync();
        sentAgain.Should().Be(0);
        Factory.Email.DigestsFor(watcherEmail).Should().ContainSingle("emailed rows are never re-sent");
    }

    [Fact]
    public async Task Email_off_recipient_gets_in_app_but_no_digest_and_no_backlog()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (userId, watcherEmail, watcher) = await AddWatchingMemberAsync(teamId, ticketId);

        // Turn the watcher's email off.
        (await watcher.PutAsJsonAsync("/api/me/notification-settings", new { emailNotificationsEnabled = false }))
            .EnsureSuccessStatusCode();

        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();

        // In-app row exists.
        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        list.Items.Should().ContainSingle(n => n.EventType == "ticket_moved");

        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var sent = await DrainNotificationEmailsAsync();
        sent.Should().Be(0, "an email-off recipient is skipped");
        Factory.Email.DigestsFor(watcherEmail).Should().BeEmpty();

        // The rows were marked emailed (no send) so they do not backlog: a second drain still sends nothing.
        await Factory.WithDbAsync(async db =>
        {
            var rows = db.Notifications.Where(n => n.RecipientId == userId).ToList();
            rows.Should().OnlyContain(n => n.EmailedAt != null, "email-off rows are marked emailed without sending");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Blocked_recipient_is_skipped_without_send()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (userId, watcherEmail, _) = await AddWatchingMemberAsync(teamId, ticketId);

        // The event fans out while the watcher still has access, then they are blocked before the drain.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        await BlockUserAsync(userId);

        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var sent = await DrainNotificationEmailsAsync();
        sent.Should().Be(0, "a blocked recipient is skipped at drain");
        Factory.Email.DigestsFor(watcherEmail).Should().BeEmpty();
    }

    [Fact]
    public async Task Ticket_deleted_notification_survives_as_tombstone_and_is_emailed()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, watcherEmail, watcher) = await AddWatchingMemberAsync(teamId, ticketId);

        (await admin.DeleteAsync($"/api/tickets/{ticketId}")).EnsureSuccessStatusCode();

        // The watcher has a ticket_deleted notification whose ticketId is null (the ticket is gone → SET NULL).
        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        var tombstone = list.Items.Should().ContainSingle(n => n.EventType == "ticket_deleted").Subject;
        tombstone.TicketId.Should().BeNull("a ticket_deleted notification outlives its ticket (§6.6 tombstone)");
        tombstone.Summary.Should().Contain("Login fails");

        // It still emails after the debounce window.
        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var sent = await DrainNotificationEmailsAsync();
        sent.Should().Be(1);
        Factory.Email.DigestsFor(watcherEmail).Should().ContainSingle();
    }
}
