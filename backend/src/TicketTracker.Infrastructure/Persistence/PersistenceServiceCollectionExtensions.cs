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
        // Provider switch (ADR-0002 extended for local dev): default Postgres (prod/Docker).
        // DATABASE_PROVIDER=Sqlite selects a file-based SQLite DB for a no-Docker local run.
        var provider = configuration.GetValue<string>("DATABASE_PROVIDER") ?? "Postgres";
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            // Schema is created via EnsureCreated in DatabaseInitializer because the Npgsql
            // migrations are provider-specific. The model is SQLite-compatible (proven by tests).
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(connectionString ?? "Data Source=ticketing-dev.db"));
        }
        else
        {
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(
                    connectionString ?? "Host=db;Port=5432;Database=ticketing;Username=ticketing;Password=change-me-local",
                    npgsql =>
                    {
                        npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                        npgsql.EnableRetryOnFailure();
                    });
            });
        }

        // Expose the context through the application port so services depend only on IAppDbContext.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        return services;
    }
}
