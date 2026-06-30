using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Infrastructure.Persistence;

/// <summary>
/// Single, swappable persistence registration (ADR-0002). Production reads the connection
/// string and registers <see cref="AppDbContext"/> over Npgsql/PostgreSQL. Tests use their
/// own <c>WebApplicationFactory</c> to REMOVE this registration and re-add the context over
/// an in-memory SQLite connection — which is why all provider config is funneled through here.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddAppPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? "Host=db;Port=5432;Database=ticketing;Username=ticketing;Password=change-me-local";

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure();
            });
        });

        // Expose the context through the application port so services depend only on IAppDbContext.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        return services;
    }
}
