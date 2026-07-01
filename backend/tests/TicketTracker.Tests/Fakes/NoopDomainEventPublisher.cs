using TicketTracker.Application.Abstractions;

namespace TicketTracker.Tests.Fakes;

/// <summary>
/// No-op <see cref="IDomainEventPublisher"/> for the service-level unit tests that construct
/// <c>TicketService</c>/<c>CommentService</c> directly (they assert modified_at semantics, not event
/// emission — the event backbone is exercised end-to-end by the integration tests). Captures nothing.
/// </summary>
public sealed class NoopDomainEventPublisher : IDomainEventPublisher
{
    public Task PublishAsync(IReadOnlyList<TicketEvent> events, CancellationToken ct) => Task.CompletedTask;
}
