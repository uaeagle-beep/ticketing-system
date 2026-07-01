using Microsoft.Extensions.Logging;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Application.Events;

/// <summary>
/// Fans a batch of application events to every in-process <see cref="ITicketEventHandler"/>
/// synchronously within the caller's scope (ADR-0012). Called AFTER the mutation commits. A handler
/// failure is logged and swallowed here (belt-and-suspenders on top of each handler's own try/catch)
/// so an activity/notification write can never roll back the user's real edit. Handlers run in
/// registration order; one failing handler does not prevent the others from running.
/// </summary>
public sealed class DomainEventPublisher : IDomainEventPublisher
{
    private readonly IEnumerable<ITicketEventHandler> _handlers;
    private readonly ILogger<DomainEventPublisher> _logger;

    public DomainEventPublisher(IEnumerable<ITicketEventHandler> handlers, ILogger<DomainEventPublisher> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async Task PublishAsync(IReadOnlyList<TicketEvent> events, CancellationToken ct)
    {
        if (events is null || events.Count == 0)
            return;

        foreach (var handler in _handlers)
        {
            try
            {
                await handler.HandleAsync(events, ct);
            }
            catch (Exception ex)
            {
                // Never rethrow: the user's mutation already committed (at-most-once, R-2).
                _logger.LogError(ex,
                    "Domain-event handler {Handler} failed for {Count} event(s); the user's mutation is unaffected.",
                    handler.GetType().Name, events.Count);
            }
        }
    }
}
