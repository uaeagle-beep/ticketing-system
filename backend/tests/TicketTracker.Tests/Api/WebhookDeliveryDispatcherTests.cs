using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Deterministic webhook delivery drain tests (Wave 3, ADR-0021, §8.4). The hosted worker is removed by the
/// factory (R-A13), so tests drive <c>WebhookDeliveryDispatcher.DrainOnceAsync(Factory.Clock.UtcNow, …)</c>
/// via <see cref="IntegrationTestBase.DrainWebhookDeliveriesAsync"/> with the fake clock + fake sender:
/// sign+deliver (correct HMAC over the raw body + headers), retry with backoff, max-attempts → failed, and
/// idempotency (a delivered row is never re-sent). SSRF is bypassed in tests via WEBHOOKS_ALLOW_INSECURE +
/// the fake sender (the IP-block logic is unit-tested separately in <see cref="WebhookUrlValidatorTests"/>).
/// </summary>
public sealed class WebhookDeliveryDispatcherTests : IntegrationTestBase
{
    private sealed record Ctx(HttpClient Client, Guid TeamId, Guid TicketId, Guid SubscriptionId, string Secret);

    private async Task<Ctx> SetupWithSubscriptionAsync(string[]? events = null)
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));
        var created = await ReadAsync<CreateWebhookResponseDto>(
            await client.PostAsJsonAsync($"/api/teams/{team.Id}/webhooks",
                new { url = "https://example.com/hook", eventTypes = events ?? new[] { "ticket_moved" }, active = true }));
        return new Ctx(client, team.Id, ticket.Id, created.Subscription.Id, created.Secret);
    }

    private async Task FireTicketMovedAsync(Ctx ctx)
        => (await ctx.Client.PatchAsJsonAsync($"/api/tickets/{ctx.TicketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

    // ---- Deliver on 2xx, correct HMAC signature + headers ----

    [Fact]
    public async Task Drain_delivers_on_2xx_sets_delivered_and_signs_the_raw_body()
    {
        var ctx = await SetupWithSubscriptionAsync();
        await FireTicketMovedAsync(ctx);

        Factory.WebhookSender.NextStatusCode = 200;
        var attempted = await DrainWebhookDeliveriesAsync();
        attempted.Should().Be(1);

        // The row is delivered.
        await Factory.WithDbAsync(async db =>
        {
            var d = await db.WebhookDeliveries.SingleAsync();
            d.Status.Should().Be("delivered");
            d.DeliveredAt.Should().NotBeNull();
            d.NextAttemptAt.Should().BeNull();
            d.Attempts.Should().Be(1);
            d.LastStatusCode.Should().Be(200);
        });

        // The sender received exactly one signed request; verify the HMAC over the RAW body + the headers.
        var sent = Factory.WebhookSender.Sends.Should().ContainSingle().Subject;
        sent.Url.Should().Be("https://example.com/hook");
        sent.Headers.Should().ContainKey("X-TicketTracker-Event").WhoseValue.Should().Be("ticket_moved");
        sent.Headers.Should().ContainKey("X-TicketTracker-Delivery");
        sent.Headers.Should().ContainKey("X-TicketTracker-Timestamp");

        var expected = "sha256=" + HmacHex(ctx.Secret, sent.Body);
        sent.Headers["X-TicketTracker-Signature"].Should().Be(expected,
            "the signature must be HMAC-SHA256 of the raw request body keyed by the subscription secret");
    }

    // ---- Idempotency: a delivered row is never re-sent ----

    [Fact]
    public async Task Delivered_row_is_not_re_sent_on_a_subsequent_drain()
    {
        var ctx = await SetupWithSubscriptionAsync();
        await FireTicketMovedAsync(ctx);

        Factory.WebhookSender.NextStatusCode = 200;
        (await DrainWebhookDeliveriesAsync()).Should().Be(1);

        // Draining again attempts nothing (the row is terminal; the outbox selector excludes it).
        (await DrainWebhookDeliveriesAsync()).Should().Be(0);
        Factory.WebhookSender.Sends.Should().ContainSingle("a delivered row is never re-sent (idempotent)");
    }

    // ---- Retry + backoff on 5xx ----

    [Fact]
    public async Task Drain_on_500_increments_attempts_and_schedules_backoff_then_retries_after_the_clock_advances()
    {
        var ctx = await SetupWithSubscriptionAsync();
        await FireTicketMovedAsync(ctx);

        Factory.WebhookSender.NextStatusCode = 500;
        (await DrainWebhookDeliveriesAsync()).Should().Be(1);

        DateTime scheduledNext = default;
        await Factory.WithDbAsync(async db =>
        {
            var d = await db.WebhookDeliveries.SingleAsync();
            d.Status.Should().Be("pending", "a 5xx is retriable");
            d.Attempts.Should().Be(1);
            d.LastStatusCode.Should().Be(500);
            d.NextAttemptAt.Should().NotBeNull();
            d.NextAttemptAt!.Value.Should().BeAfter(Factory.Clock.UtcNow, "the next attempt is backed off");
            scheduledNext = d.NextAttemptAt!.Value;
        });

        // A drain BEFORE the backoff elapses attempts nothing (the row is not yet due).
        (await DrainWebhookDeliveriesAsync()).Should().Be(0);

        // Advance past the first backoff (~1m) and drain → the row is retried and now succeeds.
        Factory.Clock.SetUtcNow(scheduledNext.AddSeconds(1));
        Factory.WebhookSender.NextStatusCode = 200;
        (await DrainWebhookDeliveriesAsync()).Should().Be(1);

        await Factory.WithDbAsync(async db =>
        {
            var d = await db.WebhookDeliveries.SingleAsync();
            d.Status.Should().Be("delivered");
            d.Attempts.Should().Be(2);
        });
    }

    // ---- Max attempts → failed ----

    [Fact]
    public async Task Drain_marks_failed_after_max_attempts_of_5xx()
    {
        var ctx = await SetupWithSubscriptionAsync();
        await FireTicketMovedAsync(ctx);

        Factory.WebhookSender.NextStatusCode = 503;

        // WEBHOOK_MAX_ATTEMPTS defaults to 5. Drain, advance past each backoff, repeat until terminal.
        for (var i = 0; i < 6; i++)
        {
            var attempted = await DrainWebhookDeliveriesAsync();
            var next = await NextAttemptAtAsync();
            if (next is null)
                break; // terminal
            Factory.Clock.SetUtcNow(next.Value.AddSeconds(1));
        }

        await Factory.WithDbAsync(async db =>
        {
            var d = await db.WebhookDeliveries.SingleAsync();
            d.Status.Should().Be("failed", "after the attempt budget is exhausted the row is terminal");
            d.Attempts.Should().Be(5);
            d.NextAttemptAt.Should().BeNull();
            d.LastError.Should().NotBeNullOrEmpty();
        });

        // No further drain re-sends a failed row.
        var before = Factory.WebhookSender.Sends.Count;
        (await DrainWebhookDeliveriesAsync()).Should().Be(0);
        Factory.WebhookSender.Sends.Count.Should().Be(before);
    }

    // ---- Transport failure (exception) is isolated and retried ----

    [Fact]
    public async Task Drain_isolates_a_transport_exception_and_schedules_retry()
    {
        var ctx = await SetupWithSubscriptionAsync();
        await FireTicketMovedAsync(ctx);

        Factory.WebhookSender.ThrowOnSend = () => new HttpRequestException("connection refused");
        (await DrainWebhookDeliveriesAsync()).Should().Be(1, "the drain attempts the delivery and catches the throw");

        await Factory.WithDbAsync(async db =>
        {
            var d = await db.WebhookDeliveries.SingleAsync();
            d.Status.Should().Be("pending", "a transport failure is retriable");
            d.Attempts.Should().Be(1);
            d.NextAttemptAt.Should().NotBeNull();
            d.LastError.Should().NotBeNullOrEmpty();
        });
    }

    // ---- Ping delivery flows through the same drain ----

    [Fact]
    public async Task Ping_delivery_is_signed_and_delivered_by_the_drain()
    {
        var ctx = await SetupWithSubscriptionAsync();
        (await ctx.Client.PostAsync($"/api/webhooks/{ctx.SubscriptionId}/ping", null)).EnsureSuccessStatusCode();

        Factory.WebhookSender.NextStatusCode = 200;
        (await DrainWebhookDeliveriesAsync()).Should().Be(1);

        var sent = Factory.WebhookSender.Sends.Should().ContainSingle().Subject;
        sent.Headers["X-TicketTracker-Event"].Should().Be("webhook_ping");
        sent.Headers["X-TicketTracker-Signature"].Should().Be("sha256=" + HmacHex(ctx.Secret, sent.Body));
    }

    private async Task<DateTime?> NextAttemptAtAsync()
    {
        DateTime? next = null;
        await Factory.WithDbAsync(async db =>
        {
            var d = await db.WebhookDeliveries.SingleAsync();
            next = d.NextAttemptAt;
        });
        return next;
    }

    private static string HmacHex(string secret, string body)
        => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
}
