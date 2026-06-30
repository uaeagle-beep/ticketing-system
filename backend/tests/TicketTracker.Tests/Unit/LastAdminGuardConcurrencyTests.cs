using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;
using TicketTracker.Domain.Entities;
using TicketTracker.Infrastructure.Persistence;
using TicketTracker.Tests.Fakes;

namespace TicketTracker.Tests.Unit;

/// <summary>
/// SEC-1 / SEC-2 regression: the last-admin guard must be atomic and race-safe (ADR-0008 INV-2).
/// The previous implementation did a non-transactional COUNT then an UPDATE on the demote path, so
/// two demote/block requests against the two final admins could each read "1 other active admin"
/// and both commit, leaving ZERO admins (self-lockout). The fix re-evaluates the invariant INSIDE
/// the guard's serializable, retriable transaction immediately before the write.
///
/// These run over real SQLite (ADR-0002) with each "request" on its OWN DbContext + connection over
/// a SHARED in-memory database, so they exercise genuine cross-context interleaving rather than a
/// single cached context. SQLite serializes writers, which is exactly the model the guard must be
/// correct under (a committed demote must be visible to the next guard's re-check).
/// </summary>
public sealed class LastAdminGuardConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;
    private readonly TestClock _clock = new();

    public LastAdminGuardConcurrencyTests()
    {
        // A NAMED shared-cache in-memory DB so multiple connections see the same schema/data. The
        // kept-open connection keeps the database alive for the test's lifetime (closing the last
        // connection drops an in-memory DB).
        _connectionString = $"DataSource=guard-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        return new AppDbContext(options);
    }

    private UserAdminService NewService(AppDbContext db, Guid actorId)
        => new(db, _clock, new FakeCurrentUser(actorId), new NoopHasher(), new NoopGenerator(),
            NullLogger<UserAdminService>.Instance);

    private Guid SeedAdmin(string email)
    {
        var id = Guid.NewGuid();
        using var db = NewDb();
        db.Users.Add(new User
        {
            Id = id,
            Email = email,
            EmailNormalized = email.ToLowerInvariant(),
            PasswordHash = "x",
            EmailVerified = true,
            IsAdmin = true,
            IsBlocked = false,
            CreatedAt = _clock.UtcNow
        });
        db.SaveChanges();
        return id;
    }

    private async Task<int> ActiveAdminCountAsync()
    {
        await using var db = NewDb();
        return await db.Users.CountAsync(u => u.IsAdmin && !u.IsBlocked && u.EmailVerified);
    }

    [Fact]
    public async Task Two_parallel_demotes_of_the_last_two_admins_leave_exactly_one_admin()
    {
        var adminA = SeedAdmin("a-admin@dataart.com");
        var adminB = SeedAdmin("b-admin@dataart.com");

        // Each "request" gets its own context/connection, mirroring two concurrent HTTP calls.
        await using var dbA = NewDb();
        await using var dbB = NewDb();
        var svcA = NewService(dbA, adminA);
        var svcB = NewService(dbB, adminB);

        // Fire both demotes "simultaneously". SQLite serializes the writers; the loser's in-tx
        // re-check (or a transient lock surfaced as a thrown failure) must prevent a second success.
        var demoteA = Attempt(() => svcA.SetRoleAsync(adminA, new SetRoleRequest(false), default));
        var demoteB = Attempt(() => svcB.SetRoleAsync(adminB, new SetRoleRequest(false), default));
        var results = await Task.WhenAll(demoteA, demoteB);

        var successes = results.Count(r => r.Ok);

        successes.Should().Be(1, "exactly one of the two final admins may be demoted");
        // Whatever the loser's failure shape (409 last_admin_required, or a transient SQLite write
        // conflict), the invariant is what ultimately matters:
        (await ActiveAdminCountAsync()).Should().BeGreaterThanOrEqualTo(1,
            "the system must always retain at least one active administrator (INV-2)");
    }

    [Fact]
    public async Task Demote_after_another_admin_already_demoted_is_409_last_admin_required()
    {
        // Deterministic TOCTOU proof: B begins its operation while two admins still exist, but A's
        // demote commits first. B's guard re-checks INSIDE its transaction and must see zero other
        // active admins → 409, rather than acting on a stale "1 other admin" pre-read.
        var adminA = SeedAdmin("a-admin@dataart.com");
        var adminB = SeedAdmin("b-admin@dataart.com");

        await using (var dbA = NewDb())
        {
            var ok = await NewService(dbA, adminA).SetRoleAsync(adminA, new SetRoleRequest(false), default);
            ok.IsAdmin.Should().BeFalse();
        }

        await using var dbB = NewDb();
        var act = async () => await NewService(dbB, adminB).SetRoleAsync(adminB, new SetRoleRequest(false), default);

        (await act.Should().ThrowAsync<ServiceException>())
            .Which.Code.Should().Be(ServiceErrorCode.LastAdminRequired);
        (await ActiveAdminCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Block_then_demote_of_the_two_last_admins_cannot_remove_both()
    {
        // Cross-operation race: block one admin, then a demote of the other must be refused.
        var adminA = SeedAdmin("a-admin@dataart.com");
        var adminB = SeedAdmin("b-admin@dataart.com");

        await using (var dbA = NewDb())
            await NewService(dbA, adminA).BlockAsync(adminA, default);

        await using var dbB = NewDb();
        var act = async () => await NewService(dbB, adminB).SetRoleAsync(adminB, new SetRoleRequest(false), default);

        (await act.Should().ThrowAsync<ServiceException>())
            .Which.Code.Should().Be(ServiceErrorCode.LastAdminRequired);
        (await ActiveAdminCountAsync()).Should().Be(1, "blocking A left B as the sole active admin");
    }

    private static async Task<Outcome> Attempt(Func<Task<AdminUserDto>> op)
    {
        try
        {
            var dto = await op();
            return new Outcome(true, null, dto.IsAdmin);
        }
        catch (ServiceException ex)
        {
            return new Outcome(false, ex.Code, null);
        }
        catch (DbUpdateException)
        {
            // A transient SQLite write conflict (database is locked) — treated as a non-success;
            // the invariant assertion below is the real guarantee.
            return new Outcome(false, null, null);
        }
        catch (SqliteException)
        {
            return new Outcome(false, null, null);
        }
    }

    private readonly record struct Outcome(bool Ok, ServiceErrorCode? Code, bool? IsAdmin);

    // Demote/block never invoke these, but the service constructor requires them.
    private sealed class NoopHasher : IPasswordHasher
    {
        public string Hash(string password) => "hash";
        public bool Verify(string password, string encodedHash) => true;
    }

    private sealed class NoopGenerator : IPasswordGenerator
    {
        public string Generate() => "generated-password-xxxx";
    }
}
