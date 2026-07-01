using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite — email outbox worker (WAVE2 §10 E, ADR-0014). Drives
/// <c>NotificationEmailDispatcher.DrainOnceAsync</c> directly (the hosted worker is removed by the factory)
/// with the fake clock + fake sender. Presses the risk areas beyond the smoke tests: multi-recipient
/// coalescing (each recipient gets ONE digest with their own N lines), debounce boundary precision (an
/// event exactly AT the cutoff is eligible; one just inside the window is not), idempotency across a second
/// drain for multiple recipients, and — the R-4 concern — ONE bad recipient (send throws) does NOT starve
/// the others: the good recipients are emailed and stamped, the bad one stays un-emailed and is retried on
/// the next drain. Tolerates at-least-once (a duplicate digest is acceptable; a lost one is not).
/// </summary>
public sealed class NotificationEmailWorkerTests : IntegrationTestBase
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

    private async Task<(Guid UserId, string Email, HttpClient Client)> AddWatcherAsync(Guid teamId, Guid ticketId)
    {
        var (token, userId, email) = await RegisterMemberAsync();
        await AddMembershipAsync(userId, teamId);
        var client = Authed(token);
        (await client.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();
        return (userId, email, client);
    }

    [Fact]
    public async Task Multiple_recipients_each_get_one_coalesced_digest_with_their_own_lines()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, aEmail, _) = await AddWatcherAsync(teamId, ticketId);
        var (_, bEmail, _) = await AddWatcherAsync(teamId, ticketId);

        // Two admin events → each watcher (both != actor) gets two pending notifications.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "one" })).EnsureSuccessStatusCode();

        // Within the window → nothing.
        (await DrainNotificationEmailsAsync()).Should().Be(0);

        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var emailed = await DrainNotificationEmailsAsync();
        emailed.Should().Be(2, "two distinct recipients are each emailed once");

        Factory.Email.DigestsFor(aEmail).Should().ContainSingle().Which.Lines.Should().HaveCount(2);
        Factory.Email.DigestsFor(bEmail).Should().ContainSingle().Which.Lines.Should().HaveCount(2);

        // Second drain sends nothing for anyone (idempotent via emailed_at across all recipients).
        (await DrainNotificationEmailsAsync()).Should().Be(0);
        Factory.Email.DigestsFor(aEmail).Should().ContainSingle();
        Factory.Email.DigestsFor(bEmail).Should().ContainSingle();
    }

    [Fact]
    public async Task Digest_lines_are_the_rendered_summaries_in_created_order()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, email, _) = await AddWatcherAsync(teamId, ticketId);

        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "look" })).EnsureSuccessStatusCode();

        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        (await DrainNotificationEmailsAsync()).Should().Be(1);

        var digest = Factory.Email.DigestsFor(email).Should().ContainSingle().Subject;
        digest.Lines.Should().HaveCount(2);
        digest.Lines.Should().Contain(l => l.Contains("moved this"));
        digest.Lines.Should().Contain(l => l.Contains("commented"));
    }

    [Fact]
    public async Task Debounce_boundary_notification_exactly_at_cutoff_is_eligible()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, email, _) = await AddWatcherAsync(teamId, ticketId);

        // Notification created at t0.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();

        // At exactly +59s the row is younger than the 60s window → not eligible.
        Factory.Clock.Advance(TimeSpan.FromSeconds(59));
        (await DrainNotificationEmailsAsync()).Should().Be(0, "a notification inside the debounce window is not emailed");
        Factory.Email.DigestsFor(email).Should().BeEmpty();

        // At exactly +60s (createdAt <= now - 60s) it becomes eligible.
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await DrainNotificationEmailsAsync()).Should().Be(1, "a notification exactly at the cutoff is eligible");
        Factory.Email.DigestsFor(email).Should().ContainSingle();
    }

    [Fact]
    public async Task One_bad_recipient_does_not_starve_the_others_and_is_retried()
    {
        // R-4: per-recipient try/catch. A bad address throws on send; the good recipients are still emailed
        // and stamped, and the bad recipient's rows remain un-emailed for a later retry.
        var (admin, teamId, ticketId) = await SetupAsync();
        var (goodId, goodEmail, _) = await AddWatcherAsync(teamId, ticketId);
        var (badId, badEmail, _) = await AddWatcherAsync(teamId, ticketId);

        Factory.Email.FailDigestsFor.Add(badEmail);

        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();

        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var emailed = await DrainNotificationEmailsAsync();
        emailed.Should().Be(1, "only the good recipient is counted as emailed; the bad one threw");

        Factory.Email.DigestsFor(goodEmail).Should().ContainSingle("the good recipient is not starved by the bad one");
        Factory.Email.DigestsFor(badEmail).Should().BeEmpty();

        await Factory.WithDbAsync(async db =>
        {
            (await db.Notifications.Where(n => n.RecipientId == goodId).AllAsync(n => n.EmailedAt != null))
                .Should().BeTrue("the good recipient's rows are stamped emailed");
            (await db.Notifications.Where(n => n.RecipientId == badId).AllAsync(n => n.EmailedAt == null))
                .Should().BeTrue("the failing recipient's rows stay null for retry");
        });

        // Fix the address and drain again → the bad recipient is now emailed (retry succeeds); no lost digest.
        Factory.Email.FailDigestsFor.Clear();
        var retry = await DrainNotificationEmailsAsync();
        retry.Should().Be(1, "the previously-failing recipient is retried and now emailed");
        Factory.Email.DigestsFor(badEmail).Should().ContainSingle();
        // The good recipient is NOT re-sent (idempotent) — tolerate at-least-once but do not duplicate here.
        Factory.Email.DigestsFor(goodEmail).Should().ContainSingle();
    }

    [Fact]
    public async Task Email_off_toggled_before_drain_suppresses_send_but_keeps_in_app()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (userId, email, watcher) = await AddWatcherAsync(teamId, ticketId);

        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        // Turn email off AFTER the in-app rows exist but BEFORE the drain.
        (await watcher.PutAsJsonAsync("/api/me/notification-settings", new { emailNotificationsEnabled = false }))
            .EnsureSuccessStatusCode();

        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        (await DrainNotificationEmailsAsync()).Should().Be(0);
        Factory.Email.DigestsFor(email).Should().BeEmpty();

        // In-app row still present; the drain marked it emailed so it never backlogs.
        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        list.Items.Should().ContainSingle(n => n.EventType == "ticket_moved");
        await Factory.WithDbAsync(async db =>
            (await db.Notifications.Where(n => n.RecipientId == userId).AllAsync(n => n.EmailedAt != null))
                .Should().BeTrue("email-off rows are stamped emailed without sending (no backlog)"));
    }

    [Fact]
    public async Task Ticket_deleted_digest_line_survives_and_is_emailed_alongside_other_lines()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, email, _) = await AddWatcherAsync(teamId, ticketId);

        // A normal event, then the delete — both notifications land for the watcher.
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" })).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/tickets/{ticketId}")).EnsureSuccessStatusCode();

        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        (await DrainNotificationEmailsAsync()).Should().Be(1);

        var digest = Factory.Email.DigestsFor(email).Should().ContainSingle().Subject;
        digest.Lines.Should().Contain(l => l.Contains("deleted ticket 'Login fails'"),
            "the ticket_deleted digest line survives the ticket's removal (tombstone, §6.6)");
    }
}
