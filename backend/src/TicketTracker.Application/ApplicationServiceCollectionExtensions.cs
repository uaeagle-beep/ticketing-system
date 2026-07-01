using Microsoft.Extensions.DependencyInjection;
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

        // Wave 2 notifications subsystem (ADR-0012/0013/0014).
        services.AddScoped<WatchService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<NotificationEmailDispatcher>();

        // Event backbone (ADR-0012): explicit after-commit publisher + two in-process handlers.
        // Both handlers are registered so the publisher's IEnumerable<ITicketEventHandler> consumes each.
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        services.AddScoped<ITicketEventHandler, ActivityRecorder>();
        services.AddScoped<ITicketEventHandler, NotificationFanout>();

        return services;
    }
}
