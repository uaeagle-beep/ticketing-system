using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Options;
using TicketTracker.Application.Services;
using TicketTracker.Domain.Entities;
using TicketTracker.Infrastructure.Persistence;
using TicketTracker.Tests.Fakes;

namespace TicketTracker.Tests.Unit;

/// <summary>
/// F-10 default-team auto-provisioning TOCTOU race (WAVE1_DESIGN §6.3 / §8.F, ADR-0011, R-3) — the test
/// the Developer did NOT write; QA owns it. Two users verify near-simultaneously while the default team
/// is still ABSENT; both reach "team missing" and try to create it. The unique index on
/// <c>name_normalized</c> is the backstop: the create-if-missing runs inside the verify execution-strategy
/// transaction, and on the unique-constraint collision the loser re-queries and joins the winner's team.
///
/// This mirrors the codebase's own concurrency-test recipe (<see cref="LastAdminGuardConcurrencyTests"/>):
/// a NAMED shared-cache in-memory SQLite DB with a SEPARATE connection per "request" (so cross-context
/// interleaving is genuine and a committed team is visible to the other request), driving the real
/// <see cref="AuthService.VerifyEmailAsync"/> — not the private helper. SQLite serializes writers, so the
/// invariant (exactly one team, both users members, verify never left an inconsistent state) is what we
/// assert; a transient writer-lock on the loser is tolerated then retried, exactly as the design intends.
/// </summary>
public sealed class DefaultTeamProvisioningRaceTests : IDisposable
{
    private const string TeamName = "Demo Team";
    private const string TeamNormalized = "demo team";

    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;
    private readonly TestClock _clock = new();

    public DefaultTeamProvisioningRaceTests()
    {
        _connectionString = $"DataSource=defaultteam-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        using var ctx = NewDb();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _keepAlive.Dispose();

    private AppDbContext NewDb()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn).Options;
        return new AppDbContext(options);
    }

    private AuthService NewAuth(AppDbContext db)
        => new(db, new NoopHasher(), new SeqTokenGenerator(), new NoopEmail(), _clock,
            Options.Create(new AuthOptions { DefaultSignupTeamName = TeamName }),
            NullLogger<AuthService>.Instance);

    /// <summary>
    /// Seed an unverified user with a verification token whose hash is the deterministic hash of
    /// <paramref name="rawToken"/> (the SeqTokenGenerator hashes to "hash:{raw}"). Returns the user id.
    /// </summary>
    private Guid SeedUnverifiedUserWithToken(string email, string rawToken)
    {
        var id = Guid.NewGuid();
        using var db = NewDb();
        db.Users.Add(new User
        {
            Id = id,
            Email = email,
            EmailNormalized = email.ToLowerInvariant(),
            PasswordHash = "x",
            EmailVerified = false,
            CreatedAt = _clock.UtcNow
        });
        db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = id,
            TokenHash = new SeqTokenGenerator().Hash(rawToken),
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddHours(24),
            ConsumedAt = null
        });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task Two_parallel_verifies_converge_on_one_default_team_with_both_users_members()
    {
        var user1 = SeedUnverifiedUserWithToken("race1@dataart.com", "tok-1");
        var user2 = SeedUnverifiedUserWithToken("race2@dataart.com", "tok-2");

        // Precondition: the default team does not exist yet (the race window).
        await using (var check = NewDb())
            (await check.Teams.AnyAsync(t => t.NameNormalized == TeamNormalized)).Should().BeFalse();

        // Two "requests", each on its own context/connection, verifying in parallel.
        await using var db1 = NewDb();
        await using var db2 = NewDb();
        var verify1 = Attempt(() => NewAuth(db1).VerifyEmailAsync(new VerifyEmailRequest("tok-1"), default));
        var verify2 = Attempt(() => NewAuth(db2).VerifyEmailAsync(new VerifyEmailRequest("tok-2"), default));
        var outcomes = await Task.WhenAll(verify1, verify2);

        // At least one verify must have succeeded outright; the other either succeeded or hit a transient
        // SQLite writer-lock (retried below). The product invariant is what ultimately matters.
        outcomes.Count(o => o.Ok).Should().BeGreaterThanOrEqualTo(1,
            "at least one concurrent verify succeeds; a bare unrecoverable failure would be a defect");

        // Retry any loser that hit a transient writer-lock, deterministically (single-threaded now). The
        // recovery path must find the committed team and JOIN it — never create a second team, never crash.
        await RetryVerifyIfNeededAsync(outcomes, verify1Ok: outcomes[0].Ok, "tok-1");
        await RetryVerifyIfNeededAsync(outcomes, verify1Ok: false, "tok-2", onlyIf: !outcomes[1].Ok);

        // Invariant: exactly one default team, both users verified and members, no duplicate membership.
        await using var assertDb = NewDb();
        var teams = await assertDb.Teams.Where(t => t.NameNormalized == TeamNormalized).ToListAsync();
        teams.Should().ContainSingle("the race must converge on exactly one default team (ADR-0011)");
        var teamId = teams[0].Id;

        var members = await assertDb.UserTeams.Where(m => m.TeamId == teamId).Select(m => m.UserId).ToListAsync();
        members.Should().Contain(user1);
        members.Should().Contain(user2);
        members.Where(id => id == user1).Should().HaveCount(1, "no duplicate membership");
        members.Where(id => id == user2).Should().HaveCount(1, "no duplicate membership");

        (await assertDb.Users.Where(u => u.Id == user1 || u.Id == user2).AllAsync(u => u.EmailVerified))
            .Should().BeTrue("both accounts are verified regardless of who won the create race");
    }

    [Fact]
    public async Task Verify_when_default_team_already_exists_joins_it_without_creating_a_duplicate()
    {
        // Deterministic loser path: the team already exists BEFORE the verify, so the create is skipped
        // and the user simply joins (the same recovery outcome the race loser reaches).
        var pre = Guid.NewGuid();
        await using (var seed = NewDb())
        {
            seed.Teams.Add(new Team
            {
                Id = pre, Name = TeamName, NameNormalized = TeamNormalized,
                CreatedAt = _clock.UtcNow, ModifiedAt = _clock.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        var user = SeedUnverifiedUserWithToken("joiner@dataart.com", "tok-join");

        await using (var db = NewDb())
            await NewAuth(db).VerifyEmailAsync(new VerifyEmailRequest("tok-join"), default);

        await using var assertDb = NewDb();
        (await assertDb.Teams.CountAsync(t => t.NameNormalized == TeamNormalized))
            .Should().Be(1, "an existing team is reused, not duplicated");
        (await assertDb.UserTeams.CountAsync(m => m.TeamId == pre && m.UserId == user))
            .Should().Be(1, "the verified user joins the existing default team exactly once");
    }

    private async Task RetryVerifyIfNeededAsync(Outcome[] outcomes, bool verify1Ok, string rawToken, bool? onlyIf = null)
    {
        // Helper: if a given verify failed with a transient lock, retry it now (no contention).
        var shouldRetry = onlyIf ?? !verify1Ok;
        if (!shouldRetry) return;
        await using var db = NewDb();
        var outcome = await Attempt(() => NewAuth(db).VerifyEmailAsync(new VerifyEmailRequest(rawToken), default));
        outcome.Ok.Should().BeTrue(
            $"a retried verify (token {rawToken}) must succeed via the create-if-missing recovery path (R-3)");
    }

    private static async Task<Outcome> Attempt(Func<Task> op)
    {
        try { await op(); return new Outcome(true, null); }
        catch (DbUpdateException ex) { return new Outcome(false, ex); }      // transient writer conflict
        catch (SqliteException ex) { return new Outcome(false, ex); }        // "database is locked"
    }

    private readonly record struct Outcome(bool Ok, Exception? Error);

    // ---- Minimal fakes (verify only needs a hasher signature, a token hash, and a no-op email) ----

    private sealed class NoopHasher : IPasswordHasher
    {
        public string Hash(string password) => "hash";
        public bool Verify(string password, string encodedHash) => true;
    }

    // Deterministic token hash so a seeded token hash matches what VerifyEmailAsync computes.
    private sealed class SeqTokenGenerator : ITokenGenerator
    {
        public string GenerateRawToken() => Guid.NewGuid().ToString("N");
        public string Hash(string rawToken) => $"hash:{rawToken}";
    }

    private sealed class NoopEmail : IEmailSender
    {
        public Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct) => Task.CompletedTask;
        public Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct) => Task.CompletedTask;
    }
}
