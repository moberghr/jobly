using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

public class JoblyWorkerPool<TContext> : BackgroundService where TContext : DbContext
{
    private readonly ILogger<JoblyWorkerPool<TContext>> _logging;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWakeupProvider? _wakeupProvider;

    private Task? _notifyTask;

    // Contains all the services that are running.
    private readonly List<(Task task, CancellationTokenSource cancellationTokenSource)> _services = new();
    private readonly object _ctsLock = new();
    private CancellationTokenSource _cancellationTokenSource;

    // should be part of health check, if _lastTick is older then lets say 3 polling intervals, then return unhealthy
    private DateTime _lastTick = DateTime.UtcNow;

    private readonly JoblyWorkerConfiguration _configuration;

    public JoblyWorkerPool(IServiceProvider serviceProvider, ILogger<JoblyWorkerPool<TContext>> logging,
        IConfigureOptions<JoblyWorkerConfiguration> configuration)
    {
        _configuration = configuration.ConfigureDefault();
        _serviceProvider = serviceProvider;
        _logging = logging;
        _wakeupProvider = serviceProvider.GetService<IWakeupProvider>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Create a linked token source that will be used to cancel the worker when a notification is received.
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // Starts to listen for notifications of new jobs, todo: move this to somewhere else with more robustness and health checks.
        _notifyTask = _wakeupProvider != null ? ListenForUpdatesNotifications(stoppingToken) : null;
        while (!stoppingToken.IsCancellationRequested)
        {
            _lastTick = DateTime.UtcNow;
            foreach (var (task, cancellationTokenSource) in _services.ToList())
            {
                // Check if task is running, the not do anything.
                if (task is {IsFaulted: false, IsCanceled: false, IsCompleted: false}) continue;

                // todo: should we log the exception or should that be part of the worker service?

                // If the service is not running, scale down, this means that the queue was empty when the service 
                // was polling for a job to process.
                cancellationTokenSource.Cancel();
                _services.Remove((task, cancellationTokenSource));
            }

            // The problem with this approach is that it will take a while to scale up the workers.
            if (_services.Count < _configuration.WorkerCount)
            {
                StartWorker();
            }

            // Restart the wakeupProvider if it has failed.
            if (_wakeupProvider != null &&
                (_notifyTask!.IsFaulted || _notifyTask.IsCanceled || _notifyTask.IsCompleted))
            {
                _logging.LogError("Notification task failed: {0}", _notifyTask.Exception?.Message);
                _notifyTask = ListenForUpdatesNotifications(stoppingToken);
            }

            _logging.LogInformation("Worker count: {0}", _services.Count);
            try
            {
                ResetCancellationTokenSource(stoppingToken);
                await Task.Delay(_configuration.PollingInterval, _cancellationTokenSource.Token);
            }
            catch (TaskCanceledException e)
            {
                // When we cancel the token because of a notification, Delay will throw a TaskCanceledException, that is 
                // expected.
                _logging.LogDebug("Task was canceled: {0}", e.Message);
            }
        }
    }

    /// <summary>
    /// Listens for notifications that will trigger a new tick, this should use notify abstraction.
    /// </summary>
    /// <param name="stoppingToken"></param>
    private async Task ListenForUpdatesNotifications(CancellationToken stoppingToken)
    {
        // ListenForNotification should be part of some notify abstraction/interface.
        if (_wakeupProvider is null)
        {
            return;
        }

        await _wakeupProvider.ListenForUpdatesNotifications(stoppingToken, () =>
        {
            _logging.LogInformation("Received notification");
            _cancellationTokenSource.Cancel();
        });
    }

    private void ResetCancellationTokenSource(CancellationToken stoppingToken)
    {
        if (_wakeupProvider is null)
        {
            return;
        }

        lock (_ctsLock)
        {
            _logging.LogDebug("Resetting token source");
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }
    }

    /// <summary>
    /// Starts a worker that will process jobs. When the worker hasn't any jobs, it will self terminate.
    /// </summary>
    private void StartWorker()
    {
        _logging.LogInformation("Starting worker");

        var cts = new CancellationTokenSource();
        var task = Task.Run(async () =>
        {
            var workerService = _serviceProvider.GetRequiredService<IJoblyWorkerService>();
            await workerService.GetAndProcessJobs(cts.Token);
        }, cts.Token);
        _services.Add((task, cts));
    }
}