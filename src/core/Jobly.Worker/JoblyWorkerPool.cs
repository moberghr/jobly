using System.Collections.Concurrent;
using Jobly.Core;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Jobly.Worker;

public class JoblyWorkerPool<TContext> : BackgroundService where TContext : DbContext
{
    private readonly ILogger<JoblyWorkerPool<TContext>> _logging;
    private readonly IServiceProvider _serviceProvider;

    private Task _notifyTask;
    private readonly List<(Task task, CancellationTokenSource cancellationTokenSource)> _services = new();
    private readonly object _ctsLock = new();
    private CancellationTokenSource _cancellationTokenSource;
    
    // should be part of health check, if _lastTick is older then lets say 3 polling intervals, then return unhealthy
    private DateTime _lastTick = DateTime.UtcNow;
    
    private readonly JoblyWorkerConfiguration _configuration;
    
    public JoblyWorkerPool(IServiceProvider serviceProvider, ILogger<JoblyWorkerPool<TContext>> logging, IConfigureOptions<JoblyWorkerConfiguration> configuration)
    {
        _configuration = configuration.ConfigureDefault();
        _serviceProvider = serviceProvider;
        _logging = logging;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        // Starts to listen for notifications of new jobs, todo: move this to somewhere else with more robustness and health checks.
        // Yes, I don't want to 
        _notifyTask = ListenForUpdatesNotifications(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            _lastTick = DateTime.UtcNow;
            foreach (var (task, cancellationTokenSource) in _services.ToList())
            {
                // if (service.ExecuteTask is {IsCompleted: false}) continue;
                // todo: handle errors
                // Check if task is running, the not do anything.
                if (task is {IsFaulted: false, IsCanceled: false, IsCompleted: false}) continue;
                
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
            
            if (_notifyTask.IsFaulted || _notifyTask.IsCanceled || _notifyTask.IsCompleted)
            {
                _logging.LogError("Notification task failed: {0}", _notifyTask.Exception?.Message);
                _notifyTask = ListenForUpdatesNotifications(stoppingToken);
            }
            
            _logging.LogInformation("Worker count: {0}", _services.Count);
            try
            {
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
        await ListenForNotifications(stoppingToken, e =>
        {
            _logging.LogInformation("Received notification: {0}", e.Payload);
            _cancellationTokenSource.Cancel();
            ResetCancellationTokenSource(stoppingToken);
        });
        
    }
    
    // Listens for postgresql notification, this should be part of some notify abstraction and not here.
    private async Task ListenForNotifications(CancellationToken cancellationToken, Action<NpgsqlNotificationEventArgs> onNotification)
    {
        string channelName = "job_added"; // take from configuration
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();

        if (dbContext.Database.GetDbConnection() is not NpgsqlConnection npgsqlConnection)
            throw new InvalidOperationException("Database connection must be of type NpgsqlConnection");

        // Ensure the connection is open
        if (npgsqlConnection.State != System.Data.ConnectionState.Open)
            await npgsqlConnection.OpenAsync(cancellationToken);
        
        npgsqlConnection.Notification += (o, e) => onNotification(e);

        await using (var command = new NpgsqlCommand($"LISTEN {channelName};", npgsqlConnection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await npgsqlConnection.WaitAsync(cancellationToken);
        }
    }

    // This is probably a bottleneck, should be optimized, only resetting the token if we are in waiting state.
    private void ResetCancellationTokenSource(CancellationToken stoppingToken)
    {
        lock (_ctsLock)
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }
    }
    
    private void StartWorker()
    {
        _logging.LogInformation("Starting worker");
        
        var cts = new CancellationTokenSource();
        var task = Task.Run(async () =>
        {
            var workerService = _serviceProvider.GetRequiredService<IJoblyWorkerService>();
            await workerService.GetAndProcessJobs(cts.Token);
        }, cts.Token);
        // var task = workerService.GetAndProcessJobs(cts.Token);
        _services.Add((task, cts));
        // IJoblyWorkerService
        // var service = ActivatorUtilities.CreateInstance<JoblyWorker<TContext>>(_serviceProvider);
        // service.StartAsync(cts.Token).ConfigureAwait(false);
        // _services.Add((service, cts));
    }
}