using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(
        IAppDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        IPasswordHasher hasher,
        IPasswordGenerator passwordGenerator,
        ILogger<UserAdminService> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _hasher = hasher;
        _passwordGenerator = passwordGenerator;
        _logger = logger;
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

        // Audit trail for a privileged account-lifecycle action (SEC-3). NEVER log the password/hash.
        await LogAdminActionAsync("create_user", user.Id, user.Email,
            "isAdmin={IsAdmin} teamCount={TeamCount}",
            extraArg0: request.IsAdmin, extraArg1: teamIds.Count, ct: ct);

        var dto = await ToDtoAsync(user.Id, ct);
        return new CreateUserResponse(dto, generated ? password : null);
    }

    // ----- Set role (§4.4) -----

    public async Task<AdminUserDto> SetRoleAsync(Guid id, SetRoleRequest request, CancellationToken ct)
    {
        _currentUser.RequireAdmin();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ServiceException.NotFound("User not found.");

        var isDemotion = user.IsAdmin && !request.IsAdmin;

        if (user.IsAdmin != request.IsAdmin) // idempotent: same value is a no-op success
        {
            if (isDemotion)
            {
                // Demotion of the last active admin is forbidden (INV-2). The guard COUNT and the
                // mutation must be atomic and serialized against a concurrent demote/block of the
                // other final admin (SEC-1 TOCTOU, SEC-2): run them in one serializable, retriable
                // transaction so two parallel demotes cannot both observe "another admin exists"
                // and both commit (provider-agnostic; see GuardedMutateAsync).
                await GuardedMutateAsync(user.Id, () => user.IsAdmin = request.IsAdmin, ct);
            }
            else // promotion: no invariant to protect, plain save
            {
                user.IsAdmin = request.IsAdmin;
                await _db.SaveChangesAsync(ct);
            }
        }

        await LogAdminActionAsync(request.IsAdmin ? "promote_admin" : "demote_admin",
            user.Id, user.Email, ct: ct);

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

        await LogAdminActionAsync("set_teams", user.Id, user.Email,
            "teamCount={TeamCount}", extraArg0: desired.Count, ct: ct);

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
            // Blocking the last active admin would leave zero usable admins (INV-2). The guard COUNT,
            // the block, and the session purge run in ONE serializable, retriable transaction so a
            // concurrent demote/block of the other final admin cannot also pass the guard (SEC-1
            // TOCTOU). Re-checking inside the same tx makes count+mutate atomic (ASR-2, INV-3, R-9).
            await GuardedMutateAsync(user.Id, async () =>
            {
                user.IsBlocked = true;
                var sessions = await _db.Sessions.Where(s => s.UserId == user.Id).ToListAsync(ct);
                if (sessions.Count > 0)
                    _db.Sessions.RemoveRange(sessions);
            }, ct);

            await LogAdminActionAsync("block_user", user.Id, user.Email, ct: ct);
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

        await LogAdminActionAsync("unblock_user", user.Id, user.Email, ct: ct);

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

        // Audit the reset (SEC-3). NEVER log the generated password or its hash.
        await LogAdminActionAsync("reset_password", user.Id, user.Email, ct: ct);

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
    /// Runs the last-admin-guarded <paramref name="mutate"/> atomically and race-safely (SEC-1/SEC-2).
    /// The guard COUNT and the mutation execute inside ONE <see cref="IsolationLevel.Serializable"/>
    /// transaction, run through the provider execution strategy (so Npgsql's retry policy can replay a
    /// serialization failure — the Npgsql constraint behind fix 14e4424). Two concurrent demote/block
    /// requests targeting the two final admins therefore cannot both pass the guard:
    /// <list type="bullet">
    /// <item>PostgreSQL: serializable isolation turns the write-skew (both read "1 other admin", both
    /// demote) into a serialization failure on the second commit; the execution strategy retries it,
    /// the re-COUNT now sees 0 other admins and it throws 409 last_admin_required.</item>
    /// <item>SQLite (tests, EnsureCreated): a single writer serializes the transactions, so the second
    /// reads the first's committed state, the re-COUNT sees 0 and it throws 409.</item>
    /// </list>
    /// External behavior is unchanged: a guard violation still surfaces as 409 last_admin_required.
    /// </summary>
    private Task GuardedMutateAsync(Guid targetUserId, Action mutate, CancellationToken ct)
        => GuardedMutateAsync(targetUserId, () => { mutate(); return Task.CompletedTask; }, ct);

    private async Task GuardedMutateAsync(Guid targetUserId, Func<Task> mutate, CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            // Re-evaluate the invariant INSIDE the transaction, immediately before mutating, so the
            // check and the write commit together — no TOCTOU window between COUNT and UPDATE.
            await EnsureNotLastActiveAdminAsync(targetUserId, ct);

            await mutate();

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>
    /// Guards INV-2: the operation must not remove the only ACTIVE admin. Active admin =
    /// is_admin AND NOT is_blocked AND email_verified (the live predicate; ADR-0008 states
    /// is_admin AND NOT is_blocked — see ПРИПУЩЕННЯ in handoff). If <paramref name="targetUserId"/>
    /// is the sole such admin, throws 409 last_admin_required. Must be called inside the guard
    /// transaction (see <see cref="GuardedMutateAsync(Guid, Func{Task}, CancellationToken)"/>).
    /// </summary>
    private async Task EnsureNotLastActiveAdminAsync(Guid targetUserId, CancellationToken ct)
    {
        var otherActiveAdmins = await _db.Users.CountAsync(
            u => u.Id != targetUserId && u.IsAdmin && !u.IsBlocked && u.EmailVerified, ct);
        if (otherActiveAdmins == 0)
            throw new ServiceException(ServiceErrorCode.LastAdminRequired,
                "The system must keep at least one active administrator.");
    }

    /// <summary>
    /// Emits a structured INFORMATION audit record for a privileged admin action (SEC-3): the acting
    /// admin (id + email from <see cref="ICurrentUser"/>), the action, and the target (id + email).
    /// NEVER logs passwords, hashes, or tokens. <paramref name="extra"/>/<paramref name="extraArgs"/>
    /// append optional non-secret structured fields (e.g. team counts).
    /// </summary>
    private async Task LogAdminActionAsync(
        string action, Guid targetUserId, string targetEmail,
        string? extra = null, object? extraArg0 = null, object? extraArg1 = null,
        CancellationToken ct = default)
    {
        var actorId = _currentUser.UserId;
        var actorEmail = actorId is null ? "(unknown)" : await ResolveActorEmailAsync(actorId.Value, ct);

        // Keep a single consistent message shape across all actions; structured args stay queryable.
        const string baseTemplate =
            "Admin audit: action={Action} actorId={ActorId} actorEmail={ActorEmail} targetId={TargetId} targetEmail={TargetEmail}";

        if (extra is null)
        {
            _logger.LogInformation(baseTemplate, action, actorId, actorEmail, targetUserId, targetEmail);
        }
        else if (extraArg1 is null)
        {
            _logger.LogInformation(baseTemplate + " " + extra,
                action, actorId, actorEmail, targetUserId, targetEmail, extraArg0);
        }
        else
        {
            _logger.LogInformation(baseTemplate + " " + extra,
                action, actorId, actorEmail, targetUserId, targetEmail, extraArg0, extraArg1);
        }
    }

    /// <summary>Resolves the acting admin's email for the audit log (cheap PK lookup, no tracking).</summary>
    private async Task<string> ResolveActorEmailAsync(Guid actorId, CancellationToken ct)
        => await _db.Users.AsNoTracking()
               .Where(u => u.Id == actorId)
               .Select(u => u.Email)
               .FirstOrDefaultAsync(ct)
           ?? "(unknown)";

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
