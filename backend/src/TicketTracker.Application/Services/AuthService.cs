using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Options;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Services;

/// <summary>
/// Authentication &amp; email-verification business logic (E1, ADR-0001, ADR-0006).
/// Owns: signup (non-enumerating), login (verified-only, anti-enumeration), logout,
/// verify-email (single-use, atomic), resend (invalidate prior unused), me, and bearer-token
/// session resolution used by the API auth middleware.
/// </summary>
public sealed class AuthService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenGenerator _tokens;
    private readonly IEmailSender _email;
    private readonly IClock _clock;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;

    // Equal-cost anti-enumeration (AUTH-001): a fixed dummy Argon2id hash verified against on the
    // null-user login branch so an unknown email costs the same as a wrong password. Computed once
    // per process (lazily, thread-safe) using the real hasher so the parameters/cost match exactly.
    private static volatile string? _dummyPasswordHash;
    private static readonly object DummyHashLock = new();

    public AuthService(
        IAppDbContext db,
        IPasswordHasher hasher,
        ITokenGenerator tokens,
        IEmailSender email,
        IClock clock,
        IOptions<AuthOptions> options,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _email = email;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    // ----- Signup (API_CONTRACT §3.1) -----

    public async Task<MessageResponse> SignupAsync(SignupRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var email = Normalization.Trim(request.Email);
        if (Normalization.IsBlank(email))
            errors["email"] = new[] { "Email is required." };
        else if (!EmailValidator.IsValid(email))
            errors["email"] = new[] { "Email is not a valid email address." };

        var password = request.Password ?? string.Empty;
        if (password.Length < FieldLimits.PasswordMin)
            errors["password"] = new[] { $"Password must be at least {FieldLimits.PasswordMin} characters." };
        else if (password.Length > FieldLimits.PasswordMax)
            errors["password"] = new[] { $"Password must be at most {FieldLimits.PasswordMax} characters." };

        if (errors.Count > 0)
            throw ServiceException.Validation("One or more fields are invalid.", errors);

        var normalized = Normalization.NormalizeKey(email);
        var existing = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.EmailNormalized == normalized, ct);

        // Anti-enumeration: if the email already exists, do NOT create a second account and
        // do NOT leak existence — return the same 201 message (API_CONTRACT §3.1, A8).
        if (existing is null)
        {
            var now = _clock.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                EmailNormalized = normalized,
                PasswordHash = _hasher.Hash(password),
                EmailVerified = false,
                CreatedAt = now
            };
            _db.Users.Add(user);

            var rawToken = await IssueVerificationTokenAsync(user, now, ct);
            await _db.SaveChangesAsync(ct);

            // SMTP failure must NOT roll back account creation (ADR-0004). The account is
            // already persisted; the user can resend if the email never arrives.
            await TrySendVerificationEmailAsync(user.Email, rawToken, ct);
        }

        return new MessageResponse(
            "Account created. Please check your email to verify your account before logging in.");
    }

    // ----- Login (API_CONTRACT §3.2) -----

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalized = Normalization.NormalizeKey(request.Email);
        var password = request.Password ?? string.Empty;

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.EmailNormalized == normalized, ct);

        // Wrong password OR unknown email => identical 401 invalid_credentials (A3).
        if (user is null)
        {
            // Equal-cost path (AUTH-001): run a real Argon2id verify against a fixed dummy hash and
            // discard the result, so the unknown-email branch costs the same as a wrong-password
            // branch and cannot be distinguished by response latency.
            _hasher.Verify(password, GetDummyPasswordHash());
            throw new ServiceException(ServiceErrorCode.InvalidCredentials,
                "Invalid email or password.");
        }

        if (!_hasher.Verify(password, user.PasswordHash))
            throw new ServiceException(ServiceErrorCode.InvalidCredentials,
                "Invalid email or password.");

        // Blocked accounts cannot log in even with correct creds (ASR-2). Checked BEFORE the verified
        // branch so a blocked-and-unverified account reports blocked (§4.9). 401 account_blocked keeps
        // "blocked == not authenticated" uniform across login and mid-session (ADR-0007, §5).
        if (user.IsBlocked)
            throw new ServiceException(ServiceErrorCode.AccountBlocked,
                "This account has been blocked. Contact an administrator.");

        // Correct creds but unverified => 403 + resend hint; no session issued (A1, A4).
        if (!user.EmailVerified)
            throw new ServiceException(ServiceErrorCode.AccountNotVerified,
                "Your account is not verified. Check your email or request a new verification link.");

        var now = _clock.UtcNow;
        var rawSession = _tokens.GenerateRawToken();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = _tokens.Hash(rawSession),
            CreatedAt = now,
            ExpiresAt = now.AddHours(_options.SessionTtlHours)
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var userDto = await BuildUserDtoAsync(user, ct);
        return new LoginResponse(rawSession, userDto, session.ExpiresAt);
    }

    // ----- Logout (API_CONTRACT §3.3) -----

    public async Task LogoutAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rawToken)) return;
        var hash = _tokens.Hash(rawToken);
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is not null)
        {
            _db.Sessions.Remove(session);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ----- Verify email (API_CONTRACT §3.4, ADR-0006) -----

    public async Task<MessageResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ServiceException(ServiceErrorCode.InvalidOrExpiredToken,
                "This verification link is invalid or has expired. Request a new one.");

        var hash = _tokens.Hash(request.Token.Trim());
        var now = _clock.UtcNow;

        // Single-use + atomic: do consumption inside a transaction so a concurrent second
        // verify cannot both succeed (V3). Run via the provider execution strategy because
        // Npgsql's retry strategy forbids user-initiated transactions outside one.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var token = await _db.EmailVerificationTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

            // Unknown, already consumed, or expired (now >= expires_at, A31) => 400.
            if (token is null || token.ConsumedAt is not null || now >= token.ExpiresAt)
                throw new ServiceException(ServiceErrorCode.InvalidOrExpiredToken,
                    "This verification link is invalid or has expired. Request a new one.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
            if (user is null)
                throw new ServiceException(ServiceErrorCode.InvalidOrExpiredToken,
                    "This verification link is invalid or has expired. Request a new one.");

            token.ConsumedAt = now;
            user.EmailVerified = true;

            // Self-registered member joins the configurable default team on first verification (req 8,
            // ASR-6). Matched by normalized name; if absent, the user gets no team + a warning. Done in
            // the SAME transaction as the verify so the two effects are atomic.
            await GrantDefaultTeamMembershipAsync(user.Id, now, ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new MessageResponse("Email verified — your account is ready to use.");
    }

    // ----- Resend verification (API_CONTRACT §3.5) -----

    public async Task<MessageResponse> ResendVerificationAsync(ResendVerificationRequest request, CancellationToken ct)
    {
        var normalized = Normalization.NormalizeKey(request.Email);
        var nonCommittal = new MessageResponse("If an account needs verification, a new email has been sent.");

        if (Normalization.IsBlank(normalized))
            return nonCommittal; // do not leak that the field was empty differently

        var user = await _db.Users.FirstOrDefaultAsync(u => u.EmailNormalized == normalized, ct);

        // Non-committal for unknown or already-verified accounts (A8): no usable token issued.
        // Also non-committal for BLOCKED accounts (ASR-2, §4.9): a blocked user must not be able to use
        // verification to regain access, so no token is issued — same 202 to avoid leaking the state.
        if (user is null || user.EmailVerified || user.IsBlocked)
            return nonCommittal;

        var now = _clock.UtcNow;

        // Invalidate all prior unused tokens then issue a new one, atomically (V4). Run via the
        // provider execution strategy: Npgsql's retry strategy forbids user-initiated
        // transactions unless they execute as a retriable unit.
        string rawToken = string.Empty;
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var priorUnused = await _db.EmailVerificationTokens
                .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
                .ToListAsync(ct);
            foreach (var t in priorUnused)
                t.ConsumedAt = now;

            rawToken = await IssueVerificationTokenAsync(user, now, ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        await TrySendVerificationEmailAsync(user.Email, rawToken, ct);

        return nonCommittal;
    }

    // ----- Me (API_CONTRACT §3.6) -----

    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ServiceException.Unauthorized();
        return await BuildUserDtoAsync(user, ct);
    }

    // ----- Forgot password (API_CONTRACT §4.4, F-01) -----

    /// <summary>
    /// Issue+email a password-reset token, non-committally (F-01, ADR-0010). Always returns the same
    /// 202 message regardless of whether the email is unknown, unverified, verified, or blocked
    /// (non-enumeration, §6.1). A token is issued ONLY for an existing, verified, non-blocked user;
    /// otherwise it is a silent no-op. Prior unused reset tokens are invalidated and the new one
    /// inserted atomically inside the execution strategy + transaction (Npgsql-retry-safe).
    /// </summary>
    public async Task<MessageResponse> RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var normalized = Normalization.NormalizeKey(request.Email);
        var nonCommittal = new MessageResponse(
            "If an account exists for that address, a password reset link has been sent.");

        if (Normalization.IsBlank(normalized))
            return nonCommittal;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.EmailNormalized == normalized, ct);

        // Silent no-op for unknown / unverified / blocked accounts (§6.1, ASSUMPTION W1-RESET-*):
        // no token issued, no email sent, identical response.
        if (user is null || !user.EmailVerified || user.IsBlocked)
            return nonCommittal;

        var now = _clock.UtcNow;

        // Invalidate all prior unused reset tokens then issue a new one atomically (at most one live
        // reset token per account, mirroring V4). Execution strategy: Npgsql forbids a bare tx.
        string rawToken = string.Empty;
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var priorUnused = await _db.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
                .ToListAsync(ct);
            foreach (var t in priorUnused)
                t.ConsumedAt = now;

            rawToken = _tokens.GenerateRawToken();
            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = _tokens.Hash(rawToken),
                CreatedAt = now,
                ExpiresAt = now.AddHours(_options.PasswordResetTtlHours),
                ConsumedAt = null
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        await TrySendPasswordResetEmailAsync(user.Email, rawToken, ct);

        return nonCommittal;
    }

    // ----- Reset password (API_CONTRACT §4.4, F-01) -----

    /// <summary>
    /// Consume a reset token and set a new password (F-01, ADR-0010). Validates the new password first
    /// (cheap, no DB), then finds+validates the token, sets the Argon2id hash, marks the token consumed
    /// (single-use), and purges ALL of the user's sessions — all inside one execution-strategy
    /// transaction (atomic, retry-safe). A token that is unknown/consumed/expired, or whose owner is now
    /// blocked (defence-in-depth), is rejected as invalid_or_expired_token (§4.4). No 401/403/404 — the
    /// endpoint is public and non-enumerating (the token itself is the secret).
    /// </summary>
    public async Task<MessageResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        // Validate the new password format first (§4.4 chosen ordering).
        var password = request.Password ?? string.Empty;
        if (password.Length < FieldLimits.PasswordMin)
            throw ServiceException.Validation("password", $"Password must be at least {FieldLimits.PasswordMin} characters.");
        if (password.Length > FieldLimits.PasswordMax)
            throw ServiceException.Validation("password", $"Password must be at most {FieldLimits.PasswordMax} characters.");

        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ServiceException(ServiceErrorCode.InvalidOrExpiredToken,
                "This password reset link is invalid or has expired. Request a new one.");

        var hash = _tokens.Hash(request.Token.Trim());
        var now = _clock.UtcNow;
        var newHash = _hasher.Hash(password);

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var token = await _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

            // Unknown, already consumed, or expired (now >= expires_at, A31) => invalid.
            if (token is null || token.ConsumedAt is not null || now >= token.ExpiresAt)
                throw new ServiceException(ServiceErrorCode.InvalidOrExpiredToken,
                    "This password reset link is invalid or has expired. Request a new one.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
            // Owner missing or blocked (defence-in-depth) => treat the token as invalid.
            if (user is null || user.IsBlocked)
                throw new ServiceException(ServiceErrorCode.InvalidOrExpiredToken,
                    "This password reset link is invalid or has expired. Request a new one.");

            user.PasswordHash = newHash;
            token.ConsumedAt = now;

            // A reset implies possible compromise / forgotten-everywhere => purge ALL sessions (§6.1).
            var sessions = await _db.Sessions.Where(s => s.UserId == user.Id).ToListAsync(ct);
            if (sessions.Count > 0)
                _db.Sessions.RemoveRange(sessions);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new MessageResponse("Your password has been reset. Please log in with your new password.");
    }

    // ----- Self-service profile (API_CONTRACT §4.5, F-04) -----

    /// <summary>
    /// Set or clear the caller's own display name (F-04) and preferred UI/email locale (Wave 3 i18n,
    /// §5.7/ADR-0022). Reuses the exact name normalization + bound as the admin path
    /// (ASSUMPTION W1-PROFILE-NAME): trim; blank/whitespace ⇒ null; &gt; 100 ⇒ 400 keyed <c>name</c>.
    /// Locale is validated to <c>uk|en</c> (blank/whitespace ⇒ null = "unset"; anything else ⇒ 400 keyed
    /// <c>locale</c>). Idempotent no-op when neither changed. Returns the updated <see cref="UserDto"/>.
    /// </summary>
    public async Task<UserDto> UpdateOwnProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ServiceException.Unauthorized();

        var name = NormalizeName(request.Name);
        var locale = NormalizeLocale(request.Locale);

        var changed = false;
        if (!string.Equals(user.Name, name, StringComparison.Ordinal)) // idempotent no-op when unchanged
        {
            user.Name = name;
            changed = true;
        }
        if (!string.Equals(user.Locale, locale, StringComparison.Ordinal))
        {
            user.Locale = locale;
            changed = true;
        }
        if (changed)
            await _db.SaveChangesAsync(ct);

        return await BuildUserDtoAsync(user, ct);
    }

    // ----- Self-service password change (API_CONTRACT §4.5, F-04) -----

    /// <summary>
    /// Change the caller's own password with current-password re-auth (F-04, ADR-0010). Verifies the
    /// current password (mismatch ⇒ 401 invalid_credentials), validates the new password, sets the
    /// Argon2id hash, and purges all OTHER sessions while KEEPING the current one (identified by hashing
    /// the presented bearer token) — all inside one execution-strategy transaction.
    /// </summary>
    public async Task ChangeOwnPasswordAsync(
        Guid userId, string? currentRawToken, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ServiceException.Unauthorized();

        // Current-password re-auth (§6.2): a re-auth failure is a credentials failure — do not leak more.
        var currentPassword = request.CurrentPassword ?? string.Empty;
        if (!_hasher.Verify(currentPassword, user.PasswordHash))
            throw new ServiceException(ServiceErrorCode.InvalidCredentials, "The current password is incorrect.");

        var newPassword = request.NewPassword ?? string.Empty;
        if (newPassword.Length < FieldLimits.PasswordMin)
            throw ServiceException.Validation("newPassword", $"Password must be at least {FieldLimits.PasswordMin} characters.");
        if (newPassword.Length > FieldLimits.PasswordMax)
            throw ServiceException.Validation("newPassword", $"Password must be at most {FieldLimits.PasswordMax} characters.");

        var newHash = _hasher.Hash(newPassword);
        var currentTokenHash = string.IsNullOrWhiteSpace(currentRawToken) ? null : _tokens.Hash(currentRawToken.Trim());

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            user.PasswordHash = newHash;

            // Purge OTHER sessions, keep the current one (ASSUMPTION W1-PROFILE-PWD-SESSIONS). When the
            // token is unknown (should not happen for an authenticated call), all sessions are purged.
            var others = await _db.Sessions
                .Where(s => s.UserId == user.Id && (currentTokenHash == null || s.TokenHash != currentTokenHash))
                .ToListAsync(ct);
            if (others.Count > 0)
                _db.Sessions.RemoveRange(others);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    // ----- Bearer-token session resolution (used by API auth middleware, ADR-0001) -----

    /// <summary>
    /// Resolve a raw bearer token to its authenticated principal (ADR-0007). Returns null on any miss
    /// (unknown/expired token, missing/unverified/BLOCKED user). Lazily deletes an expired session.
    /// The result carries the user's <c>IsAdmin</c> flag and membership team ids so the middleware can
    /// populate <see cref="ICurrentUser"/> in one round-trip.
    /// </summary>
    public async Task<CurrentPrincipal?> ResolveSessionUserAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        var hash = _tokens.Hash(rawToken.Trim());
        var session = await _db.Sessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct);

        if (session is null) return null;

        if (_clock.UtcNow >= session.ExpiresAt)
        {
            // Lazy cleanup of an expired session (ADR-0001).
            _db.Sessions.Remove(session);
            await _db.SaveChangesAsync(ct);
            return null;
        }

        var user = session.User;
        // Unverified OR blocked => treated as no valid session (ASR-2: blocked == not authenticated).
        if (user is null || !user.EmailVerified || user.IsBlocked) return null;

        var teamIds = await _db.UserTeams.AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .Select(m => m.TeamId)
            .ToListAsync(ct);

        return new CurrentPrincipal(user.Id, user.IsAdmin, teamIds);
    }

    // ----- helpers -----

    /// <summary>Builds the /me + login user payload, projecting the user's team memberships (id + name).</summary>
    private async Task<UserDto> BuildUserDtoAsync(User user, CancellationToken ct)
    {
        var teams = await _db.UserTeams.AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .OrderBy(m => m.Team!.NameNormalized)
            .Select(m => new TeamRefDto(m.TeamId, m.Team!.Name))
            .ToListAsync(ct);
        return new UserDto(user.Id, user.Email, user.Name, user.EmailVerified, user.IsAdmin, user.IsBlocked, teams, user.Locale);
    }

    /// <summary>
    /// Grants the verifying user membership in the configured default team, AUTO-CREATING that team if
    /// it is missing (F-10, ADR-0011 — supersedes the ADR-0008 warn-and-skip clause). Runs inside the
    /// verify transaction (already wrapped in the execution strategy). Race-safe (TOCTOU, §6.3): the
    /// unique index on <c>name_normalized</c> is the backstop — if a concurrent verification created the
    /// team first, the losing insert throws <see cref="DbUpdateException"/>; we re-query and join the
    /// winner's team. A blank config value degrades to the old skip-with-warning path. The membership
    /// insert is idempotent (<c>AnyAsync</c> guard). The tracked <see cref="UserTeam"/> is persisted by
    /// the caller's SaveChanges; the team insert (if any) is committed here inside the same transaction
    /// so a unique violation surfaces now and can be handled.
    /// </summary>
    private async Task GrantDefaultTeamMembershipAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var normalizedTeamName = Normalization.NormalizeKey(_options.DefaultSignupTeamName);
        if (Normalization.IsBlank(normalizedTeamName))
        {
            _logger.LogWarning(
                "DEFAULT_SIGNUP_TEAM_NAME is blank; verified user joins no team (auto-provisioning disabled).");
            return;
        }

        var teamId = await ResolveOrCreateDefaultTeamAsync(normalizedTeamName, now, ct);

        var alreadyMember = await _db.UserTeams
            .AnyAsync(m => m.UserId == userId && m.TeamId == teamId, ct);
        if (alreadyMember)
            return;

        _db.UserTeams.Add(new UserTeam
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = teamId,
            CreatedAt = now
        });
    }

    /// <summary>
    /// Returns the id of the default team, creating it if absent (F-10, ADR-0011). Handles the create
    /// race: on a unique-index violation from a concurrent create, re-queries by normalized name and
    /// returns the now-existing row. Called only inside the verify transaction/execution strategy.
    /// </summary>
    private async Task<Guid> ResolveOrCreateDefaultTeamAsync(string normalizedTeamName, DateTime now, CancellationToken ct)
    {
        var existingId = await _db.Teams
            .Where(t => t.NameNormalized == normalizedTeamName)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId is not null)
            return existingId.Value;

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = Normalization.Trim(_options.DefaultSignupTeamName),
            NameNormalized = normalizedTeamName,
            CreatedAt = now,
            ModifiedAt = now
        };
        _db.Teams.Add(team);
        try
        {
            // Persist the team NOW (inside the shared transaction) so a concurrent-create collision
            // surfaces here as a unique-index violation we can recover from.
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Auto-created default signup team '{TeamName}' at first self-signup verification (F-10).",
                team.Name);
            return team.Id;
        }
        catch (DbUpdateException)
        {
            // Race lost: another verification created the team first. Untrack our failed insert (Remove
            // on an Added entity detaches it) so the caller's later SaveChanges does not retry it, then
            // use the committed row (both users converge on one team, §6.3).
            _db.Teams.Remove(team);
            var winnerId = await _db.Teams
                .Where(t => t.NameNormalized == normalizedTeamName)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);
            if (winnerId is null)
                throw; // no committed row means a different failure — do not swallow it
            return winnerId.Value;
        }
    }

    /// <summary>
    /// Returns a process-wide dummy Argon2id hash (computed once via the real hasher so its cost
    /// matches a genuine verify). Used to equalize the null-user login branch (AUTH-001).
    /// </summary>
    private string GetDummyPasswordHash()
    {
        var cached = _dummyPasswordHash;
        if (cached is not null) return cached;

        lock (DummyHashLock)
        {
            return _dummyPasswordHash ??= _hasher.Hash("anti-enumeration-dummy-password");
        }
    }

    private async Task<string> IssueVerificationTokenAsync(User user, DateTime now, CancellationToken ct)
    {
        var rawToken = _tokens.GenerateRawToken();
        var token = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = _tokens.Hash(rawToken),
            CreatedAt = now,
            ExpiresAt = now.AddHours(_options.TokenTtlHours),
            ConsumedAt = null
        };
        _db.EmailVerificationTokens.Add(token);
        await Task.CompletedTask;
        return rawToken;
    }

    private async Task TrySendVerificationEmailAsync(string email, string rawToken, CancellationToken ct)
    {
        var link = BuildVerificationLink(rawToken);
        try
        {
            await _email.SendVerificationEmailAsync(email, link, ct);
        }
        catch (Exception ex)
        {
            // Never log the token or credentials; the account stands and resend remains available.
            _logger.LogWarning(ex, "Failed to send verification email to a user; account creation stands.");
        }
    }

    private async Task TrySendPasswordResetEmailAsync(string email, string rawToken, CancellationToken ct)
    {
        var link = BuildResetLink(rawToken);
        try
        {
            await _email.SendPasswordResetEmailAsync(email, link, ct);
        }
        catch (Exception ex)
        {
            // Never log the token; the token stands (ADR-0004). The user can request a new one.
            _logger.LogWarning(ex, "Failed to send password reset email to a user; token issuance stands.");
        }
    }

    private string BuildVerificationLink(string rawToken)
    {
        var baseUrl = _options.FrontendUrl.TrimEnd('/');
        var encoded = Uri.EscapeDataString(rawToken);
        return $"{baseUrl}/verify-email?token={encoded}";
    }

    private string BuildResetLink(string rawToken)
    {
        var baseUrl = _options.FrontendUrl.TrimEnd('/');
        var encoded = Uri.EscapeDataString(rawToken);
        return $"{baseUrl}/reset-password?token={encoded}";
    }

    /// <summary>
    /// Optional display-name normalization identical to <c>UserAdminService.SetNameAsync</c>
    /// (ASSUMPTION W1-PROFILE-NAME): trim; blank/whitespace ⇒ null; overflow ⇒ 400 keyed "name".
    /// </summary>
    private static string? NormalizeName(string? name)
    {
        var trimmed = Normalization.NormalizeOptionalText(name);
        if (trimmed is not null && trimmed.Length > FieldLimits.NameMax)
            throw ServiceException.Validation("name", $"Name must be at most {FieldLimits.NameMax} characters.");
        return trimmed;
    }

    /// <summary>
    /// Validate the optional i18n locale (Wave 3, §5.7/ADR-0022). Blank/whitespace ⇒ null ("unset" ⇒ client
    /// detection / the <c>uk</c> default); otherwise must be one of the supported codes (<c>uk|en</c>),
    /// else a 400 keyed <c>locale</c>. The backend keeps the localization concern on the client (ADR-0022);
    /// this column only records the persisted preference for cross-device bootstrap + email locale.
    /// </summary>
    private static string? NormalizeLocale(string? locale)
    {
        var trimmed = Normalization.NormalizeOptionalText(locale);
        if (trimmed is null) return null;
        if (trimmed is not ("uk" or "en"))
            throw ServiceException.Validation("locale", "Locale must be one of: uk, en.");
        return trimmed;
    }
}
