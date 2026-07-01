using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Webhook subscription management (Wave 3, ADR-0021, API_CONTRACT §5.5). All endpoints are M(team of the
/// subscription): the service resolves the team/subscription then requires access. Team-scoped list/create
/// live under <c>/api/teams/{id}/webhooks</c>; update/delete/deliveries/ping are top-level by subscription id.
/// The signing secret is returned ONLY on create/rotate (never on read). All endpoints require auth.
/// </summary>
[ApiController]
public sealed class WebhooksController : ControllerBase
{
    private readonly WebhookService _webhooks;

    public WebhooksController(WebhookService webhooks) => _webhooks = webhooks;

    // ----- List a team's subscriptions (§5.5) — M(team) -----
    [HttpGet("api/teams/{teamId:guid}/webhooks")]
    public async Task<ActionResult<IReadOnlyList<WebhookSubscriptionDto>>> List(Guid teamId, CancellationToken ct)
        => Ok(await _webhooks.ListAsync(teamId, ct));

    // ----- Create a subscription (secret shown once, §5.5) — M(team) -----
    [HttpPost("api/teams/{teamId:guid}/webhooks")]
    public async Task<ActionResult<CreateWebhookResponse>> Create(
        Guid teamId, [FromBody] CreateWebhookRequest request, CancellationToken ct)
    {
        var result = await _webhooks.CreateAsync(teamId, request ?? new CreateWebhookRequest(null, null, null), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // ----- Update url/events/active (+ optional rotate secret, §5.5) — M(team of subscription) -----
    [HttpPut("api/webhooks/{id:guid}")]
    public async Task<ActionResult<UpdateWebhookResponse>> Update(
        Guid id, [FromBody] UpdateWebhookRequest request, CancellationToken ct)
        => Ok(await _webhooks.UpdateAsync(id, request ?? new UpdateWebhookRequest(null, null, null, null), ct));

    // ----- Delete a subscription (cascades deliveries, §5.5) — M(team of subscription) -----
    [HttpDelete("api/webhooks/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _webhooks.DeleteAsync(id, ct);
        return NoContent();
    }

    // ----- Delivery audit (paged, §5.5) — M(team of subscription) -----
    [HttpGet("api/webhooks/{id:guid}/deliveries")]
    public async Task<ActionResult<WebhookDeliveryListDto>> Deliveries(
        Guid id, [FromQuery] int? limit, [FromQuery] string? cursor, CancellationToken ct)
        => Ok(await _webhooks.ListDeliveriesAsync(id, limit, cursor, ct));

    // ----- Ping: enqueue a test delivery (§5.5) — M(team of subscription) -----
    [HttpPost("api/webhooks/{id:guid}/ping")]
    public async Task<ActionResult<WebhookPingResponse>> Ping(Guid id, CancellationToken ct)
        => StatusCode(StatusCodes.Status202Accepted, await _webhooks.PingAsync(id, ct));
}
