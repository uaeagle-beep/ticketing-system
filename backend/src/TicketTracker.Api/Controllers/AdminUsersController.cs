using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Admin "Users" zone (API_CONTRACT §8, USER_MANAGEMENT_DESIGN §4). All endpoints are admin-only:
/// guarded by <c>BearerAuthMiddleware</c> (valid, verified, non-blocked session) AND the admin-zone
/// middleware gate, with <c>UserAdminService.RequireAdmin()</c> as the authoritative backstop.
/// Controllers stay thin — DTO ↔ service mapping + status codes only.
/// </summary>
[ApiController]
[Route("api/admin/users")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly UserAdminService _users;

    public AdminUsersController(UserAdminService users) => _users = users;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminUserDto>>> List(CancellationToken ct)
        => Ok(await _users.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<CreateUserResponse>> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var result = await _users.CreateAsync(
            request ?? new CreateUserRequest(null, null, false, null), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult<AdminUserDto>> SetRole(Guid id, [FromBody] SetRoleRequest request, CancellationToken ct)
        => Ok(await _users.SetRoleAsync(id, request ?? new SetRoleRequest(false), ct));

    [HttpPut("{id:guid}/teams")]
    public async Task<ActionResult<AdminUserDto>> SetTeams(Guid id, [FromBody] SetTeamsRequest request, CancellationToken ct)
        => Ok(await _users.SetTeamsAsync(id, request ?? new SetTeamsRequest(null), ct));

    [HttpPost("{id:guid}/block")]
    public async Task<ActionResult<AdminUserDto>> Block(Guid id, CancellationToken ct)
        => Ok(await _users.BlockAsync(id, ct));

    [HttpPost("{id:guid}/unblock")]
    public async Task<ActionResult<AdminUserDto>> Unblock(Guid id, CancellationToken ct)
        => Ok(await _users.UnblockAsync(id, ct));

    [HttpPost("{id:guid}/reset-password")]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(Guid id, CancellationToken ct)
        => Ok(await _users.ResetPasswordAsync(id, ct));
}
