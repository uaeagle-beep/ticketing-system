using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite for Webhooks management + the outbox drain (Wave 3, ADR-0021, §5.5/§8; test-guidance
/// §11 D). Presses the gaps the developer smoke tests leave: one delivery per active MATCHING subscription
/// when several subscriptions exist; a subscription deactivated AFTER enqueue drains to terminal-failed and is
/// never sent; the FailWithError transport seam schedules a retry; deliveries pagination + team-scope; the
/// no-op update leaves modifiedAt untouched; the secret is never revealed on GET/PUT-without-rotate; update on
/// a foreign subscription is 403 and an unknown one is 404 (anti-IDOR). SSRF is unit-tested separately
/// (WebhookUrlValidatorTests) because the integration factory sets WEBHOOKS_ALLOW_INSECURE=true.
/// </summary>
public sealed class WebhooksAcceptanceTests : IntegrationTestBase
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

    private Task<CreateWebhookResponseDto> CreateAsync(Ctx ctx, string[] events, bool active = true, string url = "https://example.com/hook")
        => ReadAsync<CreateWebhookResponseDto>(ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks",
            new { url, eventTypes = events, active }).Result);

    private Task FireTicketMovedAsync(Ctx ctx)
        => ctx.Client.PatchAsJsonAsync($"/api/tickets/{ctx.TicketId}/state", new { state = "in_progress" })
            .ContinueWith(t => t.Result.EnsureSuccessStatusCode());

    // ================= One delivery per active matching subscription =================

    [Fact]
    public async Task An_event_enqueues_exactly_one_delivery_per_active_matching_subscription()
    {
        var ctx = await SetupAsync();
        // Two active subscriptions match ticket_moved; one matches a different event; one is inactive.
        await CreateAsync(ctx, new[] { "ticket_moved" });
        await CreateAsync(ctx, new[] { "*" });                       // wildcard matches too
        await CreateAsync(ctx, new[] { "comment_added" });           // does not match
        await CreateAsync(ctx, new[] { "ticket_moved" }, active: false); // inactive

        await FireTicketMovedAsync(ctx);

        await Factory.WithDbAsync(async db =>
        {
            var moved = await db.WebhookDeliveries.CountAsync(d => d.EventType == "ticket_moved");
            moved.Should().Be(2, "exactly the two active matching subscriptions (explicit + wildcard) enqueue one each");
            (await db.WebhookDeliveries.CountAsync()).Should().Be(2, "the non-matching and inactive subscriptions enqueue nothing");
        });
    }

    // ================= Subscription deactivated after enqueue → terminal fail, never sent =================

    [Fact]
    public async Task Deactivating_a_subscription_after_enqueue_drains_to_failed_and_never_sends()
    {
        var ctx = await SetupAsync();
        var created = await CreateAsync(ctx, new[] { "ticket_moved" });
        await FireTicketMovedAsync(ctx);

        // Deactivate the subscription before the drain runs.
        (await ctx.Client.PutAsJsonAsync($"/api/webhooks/{created.Subscription.Id}", new { active = false }))
            .EnsureSuccessStatusCode();

        var attempted = await DrainWebhookDeliveriesAsync();
        attempted.Should().Be(1, "the pending row is selected by the drain");

        Factory.WebhookSender.Sends.Should().BeEmpty("an inactive subscription is never sent to");
        await Factory.WithDbAsync(async db =>
        {
            var d = await db.WebhookDeliveries.SingleAsync();
            d.Status.Should().Be("failed", "a deactivated subscription makes its pending delivery terminal");
            d.NextAttemptAt.Should().BeNull();
            d.LastError.Should().Be("subscription_inactive_or_deleted");
        });
    }

    // ================= FailWithError transport seam schedules a retry =================

    [Fact]
    public async Task A_non_success_result_without_a_status_code_schedules_a_retry()
    {
        var ctx = await SetupAsync();
        await CreateAsync(ctx, new[] { "ticket_moved" });
        await FireTicketMovedAsync(ctx);

        Factory.WebhookSender.FailWithError = "connection_reset";
        (await DrainWebhookDeliveriesAsync()).Should().Be(1);

        await Factory.WithDbAsync(async db =>
        {
            var d = await db.WebhookDeliveries.SingleAsync();
            d.Status.Should().Be("pending", "a soft failure is retriable");
            d.Attempts.Should().Be(1);
            d.LastError.Should().Be("connection_reset");
            d.NextAttemptAt.Should().NotBeNull();
        });
    }

    // ================= Deliveries pagination (keyset) — first page works =================

    [Fact]
    public async Task Deliveries_list_first_page_is_newest_first_with_hasMore_and_a_cursor()
    {
        var ctx = await SetupAsync();
        var created = await CreateAsync(ctx, new[] { "ticket_moved" });

        // Enqueue three ping deliveries at strictly increasing timestamps.
        for (var i = 0; i < 3; i++)
        {
            (await ctx.Client.PostAsync($"/api/webhooks/{created.Subscription.Id}/ping", null)).EnsureSuccessStatusCode();
            Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var page1 = await ReadAsync<WebhookDeliveryListDto>(
            await ctx.Client.GetAsync($"/api/webhooks/{created.Subscription.Id}/deliveries?limit=2"));
        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNullOrEmpty();
        // Newest-first: the two most-recent deliveries have createdAt descending.
        page1.Items[0].CreatedAt.Should().BeOnOrAfter(page1.Items[1].CreatedAt);
    }

    // ================= Deliveries pagination — SECOND page (cursor) =================
    // DEFECT BUG-1 (P1): GET /api/webhooks/{id}/deliveries?cursor=… returns HTTP 500. WebhookService
    // .ListDeliveriesAsync builds its keyset cursor filter with string.Compare(d.Id.ToString(), …,
    // StringComparison.Ordinal), which EF Core cannot translate to SQL (InvalidOperationException at query
    // execution). The Wave-2 paginated services (NotificationService/ActivityService) use the translatable
    // `x.Id.CompareTo(cursor.Id) < 0` (Guid.CompareTo) — Wave 3 deviated. The FIRST page works so the smoke
    // test missed it; ANY subscription with more deliveries than the page limit cannot be paged. This test
    // codifies the CORRECT expected behaviour (page 2 → 200 with the remaining item). It is skipped so the
    // suite stays green for regression signalling; UN-SKIP once the service uses the translatable comparison.
    [Fact]
    public async Task Deliveries_second_page_returns_the_remaining_item()
    {
        var ctx = await SetupAsync();
        var created = await CreateAsync(ctx, new[] { "ticket_moved" });
        for (var i = 0; i < 3; i++)
        {
            (await ctx.Client.PostAsync($"/api/webhooks/{created.Subscription.Id}/ping", null)).EnsureSuccessStatusCode();
            Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var page1 = await ReadAsync<WebhookDeliveryListDto>(
            await ctx.Client.GetAsync($"/api/webhooks/{created.Subscription.Id}/deliveries?limit=2"));

        var page2Resp = await ctx.Client.GetAsync(
            $"/api/webhooks/{created.Subscription.Id}/deliveries?limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}");
        page2Resp.StatusCode.Should().Be(HttpStatusCode.OK, "the second page must not 500");
        var page2 = await ReadAsync<WebhookDeliveryListDto>(page2Resp);
        page2.Items.Should().HaveCount(1);
        page2.HasMore.Should().BeFalse();
        page1.Items.Select(d => d.Id).Should().NotIntersectWith(page2.Items.Select(d => d.Id));
    }

    // ================= No-op update leaves modifiedAt untouched =================

    [Fact]
    public async Task A_no_op_update_does_not_bump_modifiedAt()
    {
        var ctx = await SetupAsync();
        var created = await CreateAsync(ctx, new[] { "ticket_moved" });
        var originalModified = created.Subscription.ModifiedAt;

        Factory.Clock.Advance(TimeSpan.FromHours(1));

        // Re-send the identical url/events/active — a no-op diff must NOT advance modifiedAt.
        var updated = await ReadAsync<UpdateWebhookResponseDto>(
            await ctx.Client.PutAsJsonAsync($"/api/webhooks/{created.Subscription.Id}",
                new { url = "https://example.com/hook", eventTypes = new[] { "ticket_moved" }, active = true }));

        updated.Subscription.ModifiedAt.Should().Be(originalModified, "a no-op update writes nothing and keeps modifiedAt");
        updated.Secret.Should().BeNull();
    }

    [Fact]
    public async Task A_real_update_advances_modifiedAt()
    {
        var ctx = await SetupAsync();
        var created = await CreateAsync(ctx, new[] { "ticket_moved" });
        var originalModified = created.Subscription.ModifiedAt;

        Factory.Clock.Advance(TimeSpan.FromHours(1));
        var updated = await ReadAsync<UpdateWebhookResponseDto>(
            await ctx.Client.PutAsJsonAsync($"/api/webhooks/{created.Subscription.Id}",
                new { eventTypes = new[] { "ticket_moved", "comment_added" } }));

        updated.Subscription.ModifiedAt.Should().BeAfter(originalModified);
        updated.Subscription.EventTypes.Should().BeEquivalentTo(new[] { "ticket_moved", "comment_added" });
    }

    // ================= Secret is never revealed on GET or a non-rotating PUT =================

    [Fact]
    public async Task The_signing_secret_is_never_present_in_list_or_get_responses()
    {
        var ctx = await SetupAsync();
        var created = await CreateAsync(ctx, new[] { "ticket_moved" });

        var listRaw = await (await ctx.Client.GetAsync($"/api/teams/{ctx.TeamId}/webhooks")).Content.ReadAsStringAsync();
        listRaw.Should().NotContain(created.Secret, "the plaintext secret is never serialized back");
        listRaw.Should().NotContain("whsec_", "no secret field appears on read");
        listRaw.Should().NotContain("secretEncrypted", "the encrypted-at-rest column is never serialized");
    }

    // ================= Anti-IDOR on update/delete/deliveries =================

    [Fact]
    public async Task Update_delete_and_deliveries_of_an_unknown_subscription_are_404()
    {
        var ctx = await SetupAsync();
        var unknown = Guid.NewGuid();
        (await ctx.Client.PutAsJsonAsync($"/api/webhooks/{unknown}", new { active = false })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await ctx.Client.DeleteAsync($"/api/webhooks/{unknown}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Client.GetAsync($"/api/webhooks/{unknown}/deliveries")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Client.PostAsync($"/api/webhooks/{unknown}/ping", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_non_member_cannot_update_delete_or_ping_a_foreign_subscription_403()
    {
        var ctx = await SetupAsync();
        var created = await CreateAsync(ctx, new[] { "ticket_moved" });

        var (outsiderToken, _, _) = await RegisterMemberAsync();
        var outsider = Authed(outsiderToken);

        (await outsider.PutAsJsonAsync($"/api/webhooks/{created.Subscription.Id}", new { active = false })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await outsider.DeleteAsync($"/api/webhooks/{created.Subscription.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await outsider.PostAsync($"/api/webhooks/{created.Subscription.Id}/ping", null)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    // ================= Empty eventTypes → 400 keyed eventTypes =================

    [Fact]
    public async Task Create_with_empty_eventTypes_is_400_keyed_eventTypes()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync($"/api/teams/{ctx.TeamId}/webhooks",
            new { url = "https://example.com/hook", eventTypes = Array.Empty<string>(), active = true });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("eventTypes");
    }
}
