using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Events;
using TicketTracker.Application.Services;

namespace TicketTracker.Application;

/// <summary>Registers the Application aggregate services (one per aggregate, ARCHITECTURE §3.2).</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<TeamService>();
        services.AddScoped<EpicService>();
        services.AddScoped<TicketService>();
        services.AddScoped<CommentService>();
        services.AddScoped<UserAdminService>();

        // Wave 2 labels/tags (ADR-0016).
        services.AddScoped<LabelService>();

        // Wave 3 attachments (ADR-0018). The IAttachmentStorage binding is provided by the host
        // (Program.cs binds the local-filesystem impl; the test factory binds an in-memory impl).
        services.AddScoped<AttachmentService>();

        // Wave 3 analytics (ADR-0020): read-only composite dashboard aggregated live over existing tables
        // + activity_entries (no new tables, no new infra binding).
        services.AddScoped<AnalyticsService>();

        // Wave 2 notifications subsystem (ADR-0012/0013/0014).
        services.AddScoped<WatchService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<NotificationEmailDispatcher>();

        // Wave 3 webhooks + API keys (ADR-0021). ISecretProtector / IWebhookSender / IWebhookUrlValidator
        // are bound by the host (Program.cs prod impls; the test factory swaps IWebhookSender for a fake).
        services.AddScoped<WebhookService>();
        services.AddScoped<WebhookDeliveryDispatcher>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<ApiKeyAuthenticator>();

        // Wave 3 real-time (ADR-0019): the testable push seam. A no-op default binding lives here so the
        // event backbone always has a notifier even if the SignalR transport is not wired. The API host
        // ADDS SignalRRealtimeNotifier AFTER this call (Program.cs) so the last registration wins when a
        // single IRealtimeNotifier is resolved; the test factory REPLACES it with a recording fake. TryAdd
        // means a null default is present but never clobbers a real binding the host installs.
        services.TryAddScoped<IRealtimeNotifier, NullRealtimeNotifier>();

        // Event backbone (ADR-0012): explicit after-commit publisher + in-process handlers.
        // Every handler is registered so the publisher's IEnumerable<ITicketEventHandler> consumes each.
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        services.AddScoped<ITicketEventHandler, ActivityRecorder>();
        services.AddScoped<ITicketEventHandler, NotificationFanout>();
        // Wave 3 (ADR-0021): the 4th consumer — enqueues a webhook delivery per matching active subscription.
        services.AddScoped<ITicketEventHandler, WebhookEnqueuer>();
        // Wave 3 (ADR-0019): the 5th consumer — pushes thin real-time signals to the right SignalR groups.
        services.AddScoped<ITicketEventHandler, RealtimeNotifier>();

        return services;
    }
}
