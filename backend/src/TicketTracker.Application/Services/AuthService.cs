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
        return new UserDto(user.Id, user.Email, user.EmailVerified, user.IsAdmin, user.IsBlocked, teams);
    }

    /// <summary>
    /// Grants the verifying user membership in the configured default team if it exists and they are
    /// not already a member (req 8, ASR-6). Adds a tracked <see cref="UserTeam"/> (persisted by the
    /// caller's SaveChanges inside the verify transaction). Missing team ⇒ warning + no membership.
    /// </summary>
    private async Task GrantDefaultTeamMembershipAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var normalizedTeamName = Normalization.NormalizeKey(_options.DefaultSignupTeamName);
        if (Normalization.IsBlank(normalizedTeamName))
            return;

        var teamId = await _db.Teams
            .Where(t => t.NameNormalized == normalizedTeamName)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (teamId is null)
        {
            _logger.LogWarning(
                "Default signup team '{TeamName}' not found; verified user joins no team (req 8).",
                _options.DefaultSignupTeamName);
            return;
        }

        var alreadyMember = await _db.UserTeams
            .AnyAsync(m => m.UserId == userId && m.TeamId == teamId.Value, ct);
        if (alreadyMember)
            return;

        _db.UserTeams.Add(new UserTeam
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = teamId.Value,
            CreatedAt = now
        });
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

    private string BuildVerificationLink(string rawToken)
    {
        var baseUrl = _options.FrontendUrl.TrimEnd('/');
        var encoded = Uri.EscapeDataString(rawToken);
        return $"{baseUrl}/verify-email?token={encoded}";
    }
}
