using Jobly.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Hosted service that consumes <see cref="IJoblyNotificationTransport.ListenAsync"/> and
/// signals the in-process background tasks (dispatcher, MessageRoutingTask, OrchestrationTask)
/// on each notification. Only registered when the user opts in via
/// <c>opt.UseDatabasePush() (inside the AddJobly/AddJoblyWorker lambda)</c>.
/// </summary>
public class NotificationListenerTask<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IJoblyNotificationTransport _transport;
    private readonly JoblyDatabasePushConfiguration _options;
    private readonly JoblyWorkerConfiguration _workerConfiguration;
    private readonly ILogger<NotificationListenerTask<TContext>> _logger;

    public NotificationListenerTask(
        IJoblyNotificationTransport transport,
        JoblyDatabasePushConfiguration options,
        IOptions<JoblyWorkerConfiguration> workerConfiguration,
        ILogger<NotificationListenerTask<TContext>> logger)
    {
        _transport = transport;
        _options = options;
        _workerConfiguration = workerConfiguration.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_transport is NullNotificationTransport)
        {
            _logger.LogWarning("NotificationListenerTask started but no real transport is registered; listener will idle.");
            return;
        }

        if (!_workerConfiguration.UseDispatcher)
        {
            _logger.LogWarning(
                "Jobly DB push is enabled but UseDispatcher=false; worker fetch will keep polling. " +
                "Enable UseDispatcher on JoblyWorkerConfiguration to get the full benefit.");
        }

        var delay = _options.ReconnectInitialDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain on (re)connect — signal every consumer once to catch up on anything
                // that may have been missed while the listener was offline.
                DrainSignals();

                await foreach (var notification in _transport.ListenAsync(stoppingToken))
                {
                    Dispatch(notification);
                }

                // Listener exited without throwing — normal termination (stoppingToken cancelled).
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (BrokerSetupFailedException ex)
            {
                const string msg = "Service Broker setup failed — Jobly DB push disabled. Falling back to polling. " +
                    "Grant the broker setup DDL permission or run the setup SQL manually.";
                _logger.LogError(ex, msg);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Notification listener failed; reconnecting in {DelaySeconds}s",
                    delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.ReconnectMaxDelay.Ticks));
        }
    }

    private static void DrainSignals()
    {
        JoblyDispatcher<TContext>.SignalAll();
        MessageRoutingTask<TContext>.SignalRouting();
        OrchestrationTask<TContext>.SignalOrchestrator();
    }

    private static void Dispatch(Notification notification)
    {
        switch (notification.Kind)
        {
            case NotificationKind.JobEnqueued:
                JoblyDispatcher<TContext>.SignalAll();
                break;
            case NotificationKind.MessageEnqueued:
                MessageRoutingTask<TContext>.SignalRouting();
                break;
            case NotificationKind.JobFinalized:
                OrchestrationTask<TContext>.SignalOrchestrator();
                break;
            default:
                break;
        }
    }
}
