using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Options;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.HostedServices;

/// <summary>
/// Thin timer over <see cref="NotificationEmailDispatcher.DrainOnceAsync"/> (Wave 2, ADR-0014). Owns
/// ONLY timing + scope + error-logging — zero business logic (all correctness is in the dispatcher).
/// Each tick opens a DI scope (like <see cref="DatabaseInitializer"/>), resolves the dispatcher + the
/// clock and calls <c>DrainOnceAsync(clock.UtcNow, ...)</c>. A bad tick is logged and swallowed so it
/// never kills the host. When <c>NOTIFICATIONS_EMAIL_ENABLED=false</c> the worker returns immediately
/// (in-app notifications still work). The test factory REMOVES this hosted service so no timer fires
/// during tests (tests call <c>DrainOnceAsync</c> directly with the fake clock, §7.5 / R-8).
/// </summary>
public sealed class NotificationEmailWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationEmailWorker> _logger;

    public NotificationEmailWorker(
        IServiceProvider services,
        IOptions<NotificationOptions> options,
        ILogger<NotificationEmailWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EmailEnabled)
        {
            _logger.LogInformation(
                "NOTIFICATIONS_EMAIL_ENABLED is false; notification email worker disabled (in-app unaffected).");
            return;
        }

        var pollSeconds = Math.Max(1, _options.WorkerPollSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _services.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationEmailDispatcher>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                await dispatcher.DrainOnceAsync(clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — not an error.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification email drain failed; will retry next tick.");
            }
        }
    }
}
