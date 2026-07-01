using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Developer smoke tests for webhook subscription management + enqueue-on-event (Wave 3, ADR-0021,
/// API_CONTRACT §5.5). Samples the load-bearing behaviours — create returns the secret once, GET never
/// returns it; bad url → 400 keyed url; team-scope 403/404; enqueue writes one pending delivery per active
/// matching subscription (inactive/non-matching enqueue nothing); ping enqueues; delete cascades deliveries.
/// The delivery drain itself is covered in <see cref="WebhookDeliveryDispatcherTests"/>. Real HTTP over the
/// in-memory SQLite factory. Full acceptance coverage is the Tester's job.
/// </summary>
public sealed class WebhooksTests : IntegrationTestBase
{
    private sealed record Ctx(HttpClient Client, Guid UserId, Guid TeamId, Guid TicketId);

    private async Task<Ctx> SetupAsync()
    {
        var (token, userId, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));
        return new Ctx(client, userId, team.Id, ticket.Id);
    }

    private static object CreateBody(string url = "https://example.com/hook", string[]? events = null, bool active = true)
        => new { url, eventTypes = events ?? new[] { "ticket_moved", "comment_added" }, active };

    // ---- Create: secret once; GET never returns it ----

    [Fact]
    public async Task Create_returns_subscription_and_secret_once_and_get_never_returns_secret()
    {
        var ctx = await SetupAsync();

        var resp = await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks", CreateBody());
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadAsync<CreateWebhookResponseDto>(resp);

        created.Secret.Should().StartWith("whsec_", "the signing secret is revealed once on create");
        created.Subscription.Url.Should().Be("https://example.com/hook");
        created.Subscription.EventTypes.Should().BeEquivalentTo(new[] { "ticket_moved", "comment_added" });
        created.Subscription.Active.Should().BeTrue();

        // The list never carries a secret field (the DTO has no such property) and the stored secret is encrypted.
        var list = await ReadAsync<List<WebhookSubscriptionDto>>(
            await ctx.Client.GetAsync($"/api/teams/{ctx.TeamId}/webhooks"));
        list.Should().ContainSingle(s => s.Id == created.Subscription.Id);

        await Factory.WithDbAsync(async db =>
        {
            var stored = await db.WebhookSubscriptions.SingleAsync(s => s.Id == created.Subscription.Id);
            stored.SecretEncrypted.Should().NotContain(created.Secret, "the secret is encrypted at rest, never plaintext");
            stored.SecretEncrypted.Should().NotBeNullOrEmpty();
        });
    }

    // ---- Bad url → 400 keyed url ----

    [Fact]
    public async Task Create_with_non_https_url_is_400_keyed_url()
    {
        var ctx = await SetupAsync();

        // WEBHOOKS_ALLOW_INSECURE is true in tests, so plain http passes; a malformed url still fails.
        var resp = await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks",
            CreateBody(url: "not-a-valid-url"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("url");
    }

    [Fact]
    public async Task Create_with_unknown_event_type_is_400_keyed_eventTypes()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks",
            CreateBody(events: new[] { "not_a_real_event" }));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("eventTypes");
    }

    // ---- Team-scope: 404 unknown team, 403 non-member ----

    [Fact]
    public async Task Create_for_unknown_team_is_404()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync($"/api/teams/{Guid.NewGuid()}/webhooks", CreateBody());
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Non_member_cannot_list_or_create_a_teams_webhooks_403()
    {
        var ctx = await SetupAsync();
        var (outsiderToken, _, _) = await RegisterMemberAsync();
        var outsider = Authed(outsiderToken);

        (await outsider.GetAsync($"/api/teams/{ctx.TeamId}/webhooks")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await outsider.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks", CreateBody())).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Non_member_cannot_read_another_teams_subscription_deliveries_403()
    {
        var ctx = await SetupAsync();
        var created = await ReadAsync<CreateWebhookResponseDto>(
            await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks", CreateBody()));

        var (outsiderToken, _, _) = await RegisterMemberAsync();
        var outsider = Authed(outsiderToken);
        (await outsider.GetAsync($"/api/webhooks/{created.Subscription.Id}/deliveries")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Enqueue on a matching event ----

    [Fact]
    public async Task Matching_event_enqueues_one_pending_delivery_per_active_subscription()
    {
        var ctx = await SetupAsync();
        await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks",
            CreateBody(events: new[] { "ticket_moved" }));

        // Fire a ticket_moved event.
        (await ctx.Client.PatchAsJsonAsync($"/api/tickets/{ctx.TicketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
        {
            var deliveries = await db.WebhookDeliveries.ToListAsync();
            deliveries.Should().ContainSingle();
            deliveries[0].EventType.Should().Be("ticket_moved");
            deliveries[0].Status.Should().Be("pending");
            deliveries[0].Attempts.Should().Be(0);
            deliveries[0].NextAttemptAt.Should().NotBeNull();
            deliveries[0].PayloadJson.Should().Contain("ticket_moved");
        });
    }

    [Fact]
    public async Task Wildcard_subscription_enqueues_for_any_event()
    {
        var ctx = await SetupAsync();
        await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks", CreateBody(events: new[] { "*" }));

        (await ctx.Client.PostAsJsonAsync($"/api/tickets/{ctx.TicketId}/comments", new { body = "hi" }))
            .EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
            (await db.WebhookDeliveries.CountAsync(d => d.EventType == "comment_added")).Should().Be(1));
    }

    [Fact]
    public async Task Inactive_subscription_and_non_matching_event_enqueue_nothing()
    {
        var ctx = await SetupAsync();
        // Inactive subscription for the event that will fire.
        await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks",
            CreateBody(events: new[] { "ticket_moved" }, active: false));
        // Active subscription for a DIFFERENT event.
        await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks",
            CreateBody(events: new[] { "comment_added" }));

        (await ctx.Client.PatchAsJsonAsync($"/api/tickets/{ctx.TicketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        await Factory.WithDbAsync(async db =>
            (await db.WebhookDeliveries.CountAsync()).Should().Be(0,
                "an inactive subscription and a non-matching active subscription both enqueue nothing"));
    }

    // ---- Ping enqueues a test delivery ----

    [Fact]
    public async Task Ping_enqueues_a_webhook_ping_delivery()
    {
        var ctx = await SetupAsync();
        var created = await ReadAsync<CreateWebhookResponseDto>(
            await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks", CreateBody()));

        var resp = await ctx.Client.PostAsync($"/api/webhooks/{created.Subscription.Id}/ping", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var ping = await ReadAsync<WebhookPingResponseDto>(resp);

        await Factory.WithDbAsync(async db =>
        {
            var delivery = await db.WebhookDeliveries.SingleAsync(d => d.Id == ping.DeliveryId);
            delivery.EventType.Should().Be("webhook_ping");
            delivery.Status.Should().Be("pending");
        });
    }

    // ---- Delete cascades deliveries ----

    [Fact]
    public async Task Delete_removes_subscription_and_cascades_its_deliveries()
    {
        var ctx = await SetupAsync();
        var created = await ReadAsync<CreateWebhookResponseDto>(
            await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks", CreateBody()));
        await ctx.Client.PostAsync($"/api/webhooks/{created.Subscription.Id}/ping", null);

        var del = await ctx.Client.DeleteAsync($"/api/webhooks/{created.Subscription.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await Factory.WithDbAsync(async db =>
        {
            (await db.WebhookSubscriptions.CountAsync()).Should().Be(0);
            (await db.WebhookDeliveries.CountAsync()).Should().Be(0, "deliveries cascade with the subscription");
        });
    }

    // ---- Update: rotate secret returns a new secret once ----

    [Fact]
    public async Task Update_rotate_secret_returns_a_new_secret_once()
    {
        var ctx = await SetupAsync();
        var created = await ReadAsync<CreateWebhookResponseDto>(
            await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks", CreateBody()));

        var updated = await ReadAsync<UpdateWebhookResponseDto>(
            await ctx.Client.PutAsJsonAsync($"/api/webhooks/{created.Subscription.Id}",
                new { active = false, rotateSecret = true }));

        updated.Secret.Should().NotBeNullOrEmpty().And.StartWith("whsec_");
        updated.Secret.Should().NotBe(created.Secret, "rotation issues a fresh secret");
        updated.Subscription.Active.Should().BeFalse();

        // A plain update (no rotate) returns no secret.
        var plain = await ReadAsync<UpdateWebhookResponseDto>(
            await ctx.Client.PutAsJsonAsync($"/api/webhooks/{created.Subscription.Id}", new { active = true }));
        plain.Secret.Should().BeNull("a non-rotating update never reveals a secret");
    }
}
