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
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<Session> Sessions { get; }
    DbSet<Team> Teams { get; }
    DbSet<Epic> Epics { get; }
    DbSet<Ticket> Tickets { get; }
    DbSet<Comment> Comments { get; }
    DbSet<WipLimit> WipLimits { get; }
    DbSet<UserTeam> UserTeams { get; }
    DbSet<TicketAssignee> TicketAssignees { get; }

    // Wave 2 (ADR-0012/0013): the event backbone's persistence targets.
    DbSet<TicketWatcher> TicketWatchers { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ActivityEntry> ActivityEntries { get; }

    // Wave 2 (ADR-0016): labels/tags.
    DbSet<Label> Labels { get; }
    DbSet<TicketLabel> TicketLabels { get; }

    // Wave 3 (ADR-0018): file attachments on tickets (metadata; blob on the storage volume).
    DbSet<Attachment> Attachments { get; }

    // Wave 3 (ADR-0021): outbound webhooks (subscriptions + delivery outbox) and API keys.
    DbSet<WebhookSubscription> WebhookSubscriptions { get; }
    DbSet<WebhookDelivery> WebhookDeliveries { get; }
    DbSet<ApiKey> ApiKeys { get; }

    /// <summary>Access to the underlying provider for transactions / connectivity checks.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
