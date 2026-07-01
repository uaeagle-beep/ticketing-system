using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Options;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.HostedServices;

/// <summary>
/// Thin timer over <see cref="WebhookDeliveryDispatcher.DrainOnceAsync"/> (Wave 3, ADR-0021, §8). Owns ONLY
/// timing + scope + error-logging — zero business logic (all correctness is in the dispatcher). A copy of
/// <see cref="NotificationEmailWorker"/>: each tick opens a DI scope, resolves the dispatcher + the clock and
/// calls <c>DrainOnceAsync(clock.UtcNow, ...)</c>. A bad tick is logged and swallowed so it never kills the
/// host. When <c>WEBHOOKS_ENABLED=false</c> the worker returns immediately. The test factory REMOVES this
/// hosted service so no timer fires during tests (tests call <c>DrainOnceAsync</c> directly with the fake
/// clock + fake sender, §8.4 / R-A13).
/// </summary>
public sealed class WebhookDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookDeliveryWorker> _logger;

    public WebhookDeliveryWorker(
        IServiceProvider services,
        IOptions<WebhookOptions> options,
        ILogger<WebhookDeliveryWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("WEBHOOKS_ENABLED is false; webhook delivery worker disabled.");
            return;
        }

        var pollSeconds = Math.Max(1, _options.WorkerPollSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _services.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDeliveryDispatcher>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                await dispatcher.DrainOnceAsync(clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown — not an error
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook delivery drain failed; will retry next tick.");
            }
        }
    }
}
