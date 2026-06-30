using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Services;

/// <summary>
/// Admin "Users" zone — account lifecycle for <c>/api/admin/users</c> (USER_MANAGEMENT_DESIGN §4).
/// Every operation's first line is <see cref="ICurrentUser.RequireAdmin"/> so a direct/bypassed call
/// is still authorized (ASR-1, R-3). Enforces the last-admin invariant (INV-2) on demote/block,
/// purges sessions on block/reset inside the provider execution strategy + an explicit transaction
/// (fix 14e4424, R-9), and returns a generated password exactly once (never logged/persisted, R-6).
/// </summary>
public sealed class UserAdminService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordGenerator _passwordGenerator;

    public UserAdminService(
        IAppDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        IPasswordHasher hasher,
        IPasswordGenerator passwordGenerator)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _hasher = hasher;
        _passwordGenerator = passwordGenerator;
    }

    // ----- List (§4.2) -----

    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(CancellationToken ct)
    {
        _currentUser.RequireAdmin();

        // All users (admin zone, no team filter — UM-8), stable order by created_at asc (§4.2).
        var users = await _db.Users.AsNoTracking()
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => new
            {
                u.Id, u.Email, u.IsAdmin, u.IsBlocked, u.EmailVerified, u.CreatedAt,
                Teams = u.Memberships
                    .OrderBy(m => m.Team!.NameNormalized)
                    .Select(m => new TeamRefDto(m.TeamId, m.Team!.Name))
                    .ToList()
            })
            .ToListAsync(ct);

        return users
            .Select(u => new AdminUserDto(
                u.Id, u.Email, u.IsAdmin, u.IsBlocked, u.EmailVerified,
                DeriveStatus(u.IsBlocked, u.EmailVerified), u.CreatedAt, u.Teams))
            .ToList();
    }

    // ----- Create (§4.3) -----

    public async Task<CreateUserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        _currentUser.RequireAdmin();

        var email = Normalization.Trim(request.Email);
        if (Normalization.IsBlank(email))
            throw ServiceException.Validation("email", "Email is required.");
        if (!EmailValidator.IsValid(email))
            throw ServiceException.Validation("email", "Email is not a valid email address.");
        if (email.Length > FieldLimits.EmailMax)
            throw ServiceException.Validation("email", $"Email must be at most {FieldLimits.EmailMax} characters.");

        // Password optional: blank/null ⇒ generate; otherwise enforce the signup length policy (UM-4).
        var generated = Normalization.IsBlank(request.Password);
        var password = generated ? _passwordGenerator.Generate() : request.Password!;
        if (!generated)
        {
            if (password.Length < FieldLimits.PasswordMin)
                throw ServiceException.Validation("password", $"Password must be at least {FieldLimits.PasswordMin} characters.");
            if (password.Length > FieldLimits.PasswordMax)
                throw ServiceException.Validation("password", $"Password must be at most {FieldLimits.PasswordMax} characters.");
        }

        var normalized = Normalization.NormalizeKey(email);
        var exists = await _db.Users.AnyAsync(u => u.EmailNormalized == normalized, ct);
        if (exists)
            throw new ServiceException(ServiceErrorCode.EmailInUse, "A user with this email already exists.");

        // Validate + de-duplicate the requested team ids (each must reference an existing team).
        var teamIds = await ValidateTeamIdsAsync(request.TeamIds, ct);

        var now = _clock.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailNormalized = normalized,
            PasswordHash = _hasher.Hash(password),
            EmailVerified = true,   // admin-created accounts are pre-verified, no token, no email (UM-3)
            IsAdmin = request.IsAdmin,
            IsBlocked = false,
            CreatedAt = now
        };
        _db.Users.Add(user);

        foreach (var teamId in teamIds)
            _db.UserTeams.Add(new UserTeam { Id = Guid.NewGuid(), UserId = user.Id, TeamId = teamId, CreatedAt = now });

        await _db.SaveChangesAsync(ct);

        var dto = await ToDtoAsync(user.Id, ct);
        return new CreateUserResponse(dto, generated ? password : null);
    }

    // ----- Set role (§4.4) -----

    public async Task<AdminUserDto> SetRoleAsync(Guid id, SetRoleRequest request, CancellationToken ct)
    {
        _currentUser.RequireAdmin();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ServiceException.NotFound("User not found.");

        // Demotion of the last active admin is forbidden (INV-2). Promotion is always allowed.
        if (user.IsAdmin && !request.IsAdmin)
            await EnsureNotLastActiveAdminAsync(user.Id, ct);

        if (user.IsAdmin != request.IsAdmin) // idempotent: same value is a no-op success
        {
            user.IsAdmin = request.IsAdmin;
            await _db.SaveChangesAsync(ct);
        }

        return await ToDtoAsync(user.Id, ct);
    }

    // ----- Set teams (§4.5) -----

    public async Task<AdminUserDto> SetTeamsAsync(Guid id, SetTeamsRequest request, CancellationToken ct)
    {
        _currentUser.RequireAdmin();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ServiceException.NotFound("User not found.");

        var desired = await ValidateTeamIdsAsync(request.TeamIds, ct);

        var existing = await _db.UserTeams.Where(m => m.UserId == user.Id).ToListAsync(ct);
        var existingByTeam = existing.ToDictionary(m => m.TeamId);

        var changed = false;
        // Remove memberships no longer desired.
        foreach (var membership in existing)
        {
            if (!desired.Contains(membership.TeamId))
            {
                _db.UserTeams.Remove(membership);
                changed = true;
            }
        }
        // Add newly desired memberships.
        var now = _clock.UtcNow;
        foreach (var teamId in desired)
        {
            if (!existingByTeam.ContainsKey(teamId))
            {
                _db.UserTeams.Add(new UserTeam { Id = Guid.NewGuid(), UserId = user.Id, TeamId = teamId, CreatedAt = now });
                changed = true;
            }
        }

        if (changed)
            await _db.SaveChangesAsync(ct);

        return await ToDtoAsync(user.Id, ct);
    }

    // ----- Block (§4.6) -----

    public async Task<AdminUserDto> BlockAsync(Guid id, CancellationToken ct)
    {
        _currentUser.RequireAdmin();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ServiceException.NotFound("User not found.");

        if (!user.IsBlocked)
        {
            // Blocking the last active admin would leave zero usable admins (INV-2).
            await EnsureNotLastActiveAdminAsync(user.Id, ct);

            // Set blocked + purge ALL of the user's sessions atomically (ASR-2, INV-3, R-9).
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                user.IsBlocked = true;
                var sessions = await _db.Sessions.Where(s => s.UserId == user.Id).ToListAsync(ct);
                if (sessions.Count > 0)
                    _db.Sessions.RemoveRange(sessions);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }

        return await ToDtoAsync(user.Id, ct);
    }

    // ----- Unblock (§4.7) -----

    public async Task<AdminUserDto> UnblockAsync(Guid id, CancellationToken ct)
    {
        _currentUser.RequireAdmin();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ServiceException.NotFound("User not found.");

        if (user.IsBlocked) // idempotent
        {
            user.IsBlocked = false;
            await _db.SaveChangesAsync(ct);
        }

        return await ToDtoAsync(user.Id, ct);
    }

    // ----- Reset password (§4.8) -----

    public async Task<ResetPasswordResponse> ResetPasswordAsync(Guid id, CancellationToken ct)
    {
        _currentUser.RequireAdmin();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ServiceException.NotFound("User not found.");

        // A blocked account cannot have its password reset (req 5). 403 forbidden — the refusal is an
        // "not allowed in this state" condition; no new code is introduced (§4.8 single source of truth).
        if (user.IsBlocked)
            throw ServiceException.Forbidden("Unblock the account before resetting its password.");

        var password = _passwordGenerator.Generate();
        var hash = _hasher.Hash(password);

        // Set hash + purge ALL sessions (force re-login) atomically (R-9).
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            user.PasswordHash = hash;
            var sessions = await _db.Sessions.Where(s => s.UserId == user.Id).ToListAsync(ct);
            if (sessions.Count > 0)
                _db.Sessions.RemoveRange(sessions);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new ResetPasswordResponse(password);
    }

    // ----- helpers -----

    private static string DeriveStatus(bool isBlocked, bool emailVerified)
        => isBlocked ? "blocked" : !emailVerified ? "unverified" : "active";

    /// <summary>
    /// Validates that every requested team id exists; de-duplicates. Null/empty ⇒ no teams.
    /// An unknown id ⇒ 400 validation_error keyed "teamIds" (§4.3/§4.5).
    /// </summary>
    private async Task<HashSet<Guid>> ValidateTeamIdsAsync(IReadOnlyList<Guid>? requested, CancellationToken ct)
    {
        var unique = new HashSet<Guid>();
        if (requested is not null)
            foreach (var teamId in requested)
                if (teamId != Guid.Empty)
                    unique.Add(teamId);

        if (unique.Count == 0)
            return unique;

        var existingCount = await _db.Teams.CountAsync(t => unique.Contains(t.Id), ct);
        if (existingCount != unique.Count)
            throw ServiceException.Validation("teamIds", "One or more teams do not exist.");

        return unique;
    }

    /// <summary>
    /// Guards INV-2: the operation must not remove the only ACTIVE admin (is_admin AND NOT is_blocked
    /// AND email_verified). If <paramref name="targetUserId"/> is the sole such admin, throws
    /// 409 last_admin_required. Counts the target as one of the active admins by definition.
    /// </summary>
    private async Task EnsureNotLastActiveAdminAsync(Guid targetUserId, CancellationToken ct)
    {
        var otherActiveAdmins = await _db.Users.CountAsync(
            u => u.Id != targetUserId && u.IsAdmin && !u.IsBlocked && u.EmailVerified, ct);
        if (otherActiveAdmins == 0)
            throw new ServiceException(ServiceErrorCode.LastAdminRequired,
                "The system must keep at least one active administrator.");
    }

    private async Task<AdminUserDto> ToDtoAsync(Guid userId, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.Id, x.Email, x.IsAdmin, x.IsBlocked, x.EmailVerified, x.CreatedAt,
                Teams = x.Memberships
                    .OrderBy(m => m.Team!.NameNormalized)
                    .Select(m => new TeamRefDto(m.TeamId, m.Team!.Name))
                    .ToList()
            })
            .FirstOrDefaultAsync(ct)
            ?? throw ServiceException.NotFound("User not found.");

        return new AdminUserDto(
            u.Id, u.Email, u.IsAdmin, u.IsBlocked, u.EmailVerified,
            DeriveStatus(u.IsBlocked, u.EmailVerified), u.CreatedAt, u.Teams);
    }
}
