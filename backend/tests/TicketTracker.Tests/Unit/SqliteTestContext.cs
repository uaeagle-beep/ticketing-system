using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Infrastructure.Persistence;

namespace TicketTracker.Tests.Unit;

/// <summary>
/// A standalone, in-memory SQLite-backed <see cref="AppDbContext"/> for service-level unit tests
/// (ADR-0002), independent of the web host. Same recipe as the web factory: one kept-open
/// connection, PRAGMA foreign_keys=ON, schema via EnsureCreated(). Dispose drops the database.
/// </summary>
public sealed class SqliteTestContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }

    public SqliteTestContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>A fresh context over the SAME database connection (verifies real persistence).</summary>
    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
