using Microsoft.Extensions.DependencyInjection;
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
        return services;
    }
}
