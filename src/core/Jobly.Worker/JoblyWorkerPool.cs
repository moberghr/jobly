using Jobly.Worker.Enums;
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

    // Task that listens for notifications from the wakeup provider.
    private Task? _notifyTask;
    private WakeupType? _wakeupType;

    // Contains all the services that are running.
    private readonly List<(Task task, CancellationTokenSource cancellationTokenSource)> _services = new();
    private readonly object _ctsLock = new();
    private CancellationTokenSource _cancellationTokenSource = null!;

    // should be part of health check, if _lastTick is older than lets say 3 polling intervals, then return unhealthy
    private DateTime _lastTick = DateTime.UtcNow;

    private readonly JoblyWorkerConfiguration _configuration;

    public JoblyWorkerPool(IServiceProvider serviceProvider, ILogger<JoblyWorkerPool<TContext>> logging,
        IConfigureOptions<JoblyWorkerConfiguration> configuration)
    {
        _configuration = configuration.ConfigureDefault();
        _serviceProvider = serviceProvider;
        _logging = logging;
        _wakeupProvider = serviceProvider.GetService<IWakeupProvider>();
        _wakeupType = WakeupType.Startup;
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
                if (task.IsFaulted)
                {
                    _logging.LogError("Worker task failed: {0}", task.Exception?.Message);
                }

                // todo: should we log the exception or should that be part of the worker service?

                // If the service is not running, scale down, this means that the queue was empty when the service 
                // was polling for a job to process.
                cancellationTokenSource.Cancel();
                _services.Remove((task, cancellationTokenSource));
            }

            // If the worker count is less than the configured worker count, start a new worker so that we can
            // always process at least one job.
            if (_services.Count < _configuration.WorkerCount)
            {
                // If a batch was added, then we should scale up to the configured worker count.
                if (_wakeupType is WakeupType.BatchAdded or WakeupType.Startup) // todo: maybe startup should start at 50% of worker count?
                {
                    while (_configuration.WorkerCount > _services.Count)
                    {
                        StartWorker();
                    }
                }
                else
                {
                    StartWorker();
                }
            }
            
            _wakeupType = null;

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
        if (_wakeupProvider is null)
        {
            return;
        }

        await _wakeupProvider.ListenForUpdatesNotifications(stoppingToken, (wakeupType) =>
        {
            _logging.LogInformation("Received notification");
            _cancellationTokenSource.Cancel();
        });
    }

    /// <summary>
    /// ResetCancellationTokenSource will dispose the old token source and create a new one.
    /// It is uesed to combine the stoppingToken with the wakeupProvider token.
    /// If the wakeupProvider will trigger a notification we cancel the cancellationTokenSource
    /// so that the worker will start a new tick.
    /// </summary>
    /// <param name="stoppingToken">Stopping token from the background service</param>
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
    /// Starts a worker on a background thread that will process jobs. When the worker hasn't any more jobs, it will self terminate.
    /// </summary>
    private void StartWorker()
    {
        // _logging.LogInformation("Starting worker");

        var cts = new CancellationTokenSource();
        var task = Task.Run(async () =>
        {
            var workerService = _serviceProvider.GetRequiredService<IJoblyWorkerService>();
            await workerService.GetAndProcessJobs(cts.Token);
        }, cts.Token);
        _services.Add((task, cts));
    }
}