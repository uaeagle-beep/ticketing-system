using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Persistence port exposed to the Application services so the concrete EF Core
/// <c>AppDbContext</c> (and its provider) can be swapped in tests (ADR-0002).
/// Exposes the aggregate DbSets, SaveChanges and the Database facade (the latter so
/// services can open transactions for atomic operations such as verify and resend).
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<EmailVerificationToken> EmailVerificationTokens { get; }
    DbSet<Session> Sessions { get; }
    DbSet<Team> Teams { get; }
    DbSet<Epic> Epics { get; }
    DbSet<Ticket> Tickets { get; }
    DbSet<Comment> Comments { get; }
    DbSet<WipLimit> WipLimits { get; }
    DbSet<UserTeam> UserTeams { get; }

    /// <summary>Access to the underlying provider for transactions / connectivity checks.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
