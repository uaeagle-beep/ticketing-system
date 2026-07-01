using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Self-service API-key (personal access token) management (Wave 3, ADR-0021, API_CONTRACT §5.6). Self BY
/// CONSTRUCTION: no user id in the path — the service always targets the authenticated caller (strongest
/// anti-IDOR posture, like <see cref="MeController"/>). Not public and not under /api/admin/*, so
/// <c>BearerAuthMiddleware</c> already requires a valid, verified, non-blocked SESSION (a ptk_ key cannot
/// reach this path — it is rejected off /api/v1). The raw key is returned ONCE on create.
/// </summary>
[ApiController]
[Route("api/me/api-keys")]
public sealed class ApiKeysController : ControllerBase
{
    private readonly ApiKeyService _apiKeys;

    public ApiKeysController(ApiKeyService apiKeys) => _apiKeys = apiKeys;

    // ----- List my keys (never the raw/hash, §5.6) -----
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> List(CancellationToken ct)
        => Ok(await _apiKeys.ListAsync(ct));

    // ----- Create a key (raw shown once, §5.6) -----
    [HttpPost]
    public async Task<ActionResult<CreateApiKeyResponse>> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var result = await _apiKeys.CreateAsync(request ?? new CreateApiKeyRequest(null, null), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // ----- Revoke a key (idempotent; 404 self-mask for another user's id, §5.6) -----
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await _apiKeys.RevokeAsync(id, ct);
        return NoContent();
    }
}
