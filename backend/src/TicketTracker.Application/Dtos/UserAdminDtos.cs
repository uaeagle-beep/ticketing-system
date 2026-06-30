namespace TicketTracker.Application.Dtos;

// ----- Admin user-management DTOs (USER_MANAGEMENT_DESIGN §4.1, all admin-only) -----

/// <summary>
/// A user as shown in the admin "Users" zone. <c>Status</c> is DERIVED, not stored:
/// "blocked" if blocked; else "unverified" if not email-verified; else "active" (§2.1).
/// <c>Name</c> is the optional display name (null ⇒ the UI shows <c>Email</c>).
/// </summary>
public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string? Name,
    bool IsAdmin,
    bool IsBlocked,
    bool EmailVerified,
    string Status,
    DateTime CreatedAt,
    IReadOnlyList<TeamRefDto> Teams);

/// <summary>
/// Create-user request. A null/blank <c>Password</c> ⇒ the server generates a strong one.
/// <c>Name</c> is optional (null/blank ⇒ stored as null, UI shows the email).
/// </summary>
public sealed record CreateUserRequest(
    string? Email,
    string? Password,
    string? Name,
    bool IsAdmin,
    IReadOnlyList<Guid>? TeamIds);

/// <summary>
/// Create-user response. <c>GeneratedPassword</c> is present (shown once) only when the server
/// generated the password; it is null when the admin supplied one (§4.3, [ПРИПУЩЕННЯ UM-5]).
/// </summary>
public sealed record CreateUserResponse(AdminUserDto User, string? GeneratedPassword);

public sealed record SetRoleRequest(bool IsAdmin);

/// <summary>
/// Set/clear a user's display name. <c>Name</c> null/blank ⇒ cleared to null (UI shows the email);
/// otherwise trimmed and stored. Overflow (&gt; <see cref="Validation.FieldLimits.NameMax"/>) ⇒ 400.
/// </summary>
public sealed record SetNameRequest(string? Name);

public sealed record SetTeamsRequest(IReadOnlyList<Guid>? TeamIds);

/// <summary>Reset-password response: the plaintext is returned once, never persisted/emailed.</summary>
public sealed record ResetPasswordResponse(string GeneratedPassword);
