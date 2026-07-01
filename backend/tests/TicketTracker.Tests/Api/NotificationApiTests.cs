using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite — in-app notifications API (WAVE2 §10 D, §5.3, ADR-0013). Presses the Self-scoped
/// read/mark surface beyond the smoke tests: newest-first list carrying unreadCount, unread-count matches
/// the list, mark-one-read is idempotent and decrements, mark-all zeroes, another user's notification id →
/// 404 (self-owned masking, NOT 403), limit clamping to [1,50], and keyset pagination that stays stable
/// (no dupes/gaps) when a newer notification is inserted between page reads.
/// </summary>
public sealed class NotificationApiTests : IntegrationTestBase
{
    private async Task<(HttpClient Admin, Guid TeamId, Guid TicketId)> SetupAsync()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "T", body = "B" }));
        return (admin, team.Id, ticket.Id);
    }

    private async Task<(Guid UserId, HttpClient Client)> AddWatcherAsync(Guid teamId, Guid ticketId)
    {
        var (token, userId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(userId, teamId);
        var client = Authed(token);
        (await client.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();
        return (userId, client);
    }

    /// <summary>
    /// Produce EXACTLY N distinct-timestamp notifications for the watcher. Comments always raise an event
    /// (unlike a same-state move, which is a no-op), so this yields a deterministic count of N — the right
    /// tool for asserting list size / pagination cardinality.
    /// </summary>
    private async Task GenerateEventsAsync(HttpClient admin, Guid ticketId, int n)
    {
        for (var i = 0; i < n; i++)
        {
            Factory.Clock.Advance(TimeSpan.FromSeconds(1));
            (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = $"c{i}" }))
                .EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task List_is_newest_first_and_unread_count_matches()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddWatcherAsync(teamId, ticketId);
        await GenerateEventsAsync(admin, ticketId, 3);

        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        list.Items.Should().HaveCount(3);
        list.UnreadCount.Should().Be(3, "all fresh notifications are unread");
        for (var i = 1; i < list.Items.Count; i++)
            list.Items[i - 1].CreatedAt.Should().BeOnOrAfter(list.Items[i].CreatedAt);

        var count = await ReadAsync<UnreadCountDto>(await watcher.GetAsync("/api/notifications/unread-count"));
        count.UnreadCount.Should().Be(list.UnreadCount, "the cheap poll agrees with the list's unreadCount");
    }

    [Fact]
    public async Task Mark_one_read_is_idempotent_and_decrements_once()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddWatcherAsync(teamId, ticketId);
        await GenerateEventsAsync(admin, ticketId, 2);

        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        var id = list.Items[0].Id;

        var first = await ReadAsync<UnreadCountDto>(await watcher.PostAsync($"/api/notifications/{id}/read", null));
        first.UnreadCount.Should().Be(1);

        // Marking the SAME notification read again is idempotent — the count does not go negative / change.
        var second = await ReadAsync<UnreadCountDto>(await watcher.PostAsync($"/api/notifications/{id}/read", null));
        second.UnreadCount.Should().Be(1, "re-marking an already-read notification is a no-op");

        // The row carries a readAt now.
        var after = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        after.Items.Single(n => n.Id == id).ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Mark_all_read_zeroes_the_unread_count()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddWatcherAsync(teamId, ticketId);
        await GenerateEventsAsync(admin, ticketId, 3);

        var all = await ReadAsync<UnreadCountDto>(await watcher.PostAsync("/api/notifications/read-all", null));
        all.UnreadCount.Should().Be(0);

        var list = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        list.UnreadCount.Should().Be(0);
        list.Items.Should().OnlyContain(n => n.ReadAt != null, "read-all stamps every unread row");
    }

    [Fact]
    public async Task Another_users_notification_id_is_404_not_403()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, alice) = await AddWatcherAsync(teamId, ticketId);
        await GenerateEventsAsync(admin, ticketId, 1);
        var aliceList = await ReadAsync<NotificationListDto>(await alice.GetAsync("/api/notifications"));
        var aliceNotificationId = aliceList.Items[0].Id;

        var (_, bob) = await AddWatcherAsync(teamId, ticketId);
        var resp = await bob.PostAsync($"/api/notifications/{aliceNotificationId}/read", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a self-owned resource masks another user's id as 404 (never confirm its existence with a 403)");
    }

    [Fact]
    public async Task Unknown_notification_id_is_404_and_anon_is_401()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddWatcherAsync(teamId, ticketId);

        (await watcher.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.GetAsync("/api/notifications")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client.GetAsync("/api/notifications/unread-count")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Limit_is_clamped_to_the_documented_bounds()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddWatcherAsync(teamId, ticketId);
        await GenerateEventsAsync(admin, ticketId, 5);

        // limit above the max (50) is clamped, not rejected — the request still succeeds.
        var high = await watcher.GetAsync("/api/notifications?limit=9999");
        high.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<NotificationListDto>(high)).Items.Should().HaveCount(5);

        // limit below the min (1) is clamped up to 1.
        var low = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications?limit=0"));
        low.Items.Should().HaveCount(1);
        low.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Keyset_pagination_is_stable_under_a_concurrent_newer_insert()
    {
        var (admin, teamId, ticketId) = await SetupAsync();
        var (_, watcher) = await AddWatcherAsync(teamId, ticketId);
        await GenerateEventsAsync(admin, ticketId, 4);

        var page1 = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications?limit=2"));
        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();

        // A NEW notification arrives (newer than everything on page 1) BEFORE page 2 is fetched.
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "late" })).EnsureSuccessStatusCode();

        var page2 = await ReadAsync<NotificationListDto>(
            await watcher.GetAsync($"/api/notifications?limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}"));

        // No dupes across pages; the newly-inserted (newer) row does not leak into page 2 (keyset stability).
        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id));
        page2.Items.Should().NotContain(n => n.Summary.Contains("commented") && n.CreatedAt > page1.Items[0].CreatedAt);
    }
}
