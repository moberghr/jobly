using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jobly.Worker;

public class JoblyWorkerPool<TContext> : BackgroundService where TContext : DbContext
{
    private readonly ILogger<JoblyWorkerPool<TContext>> _logging;
    private readonly IServiceProvider _serviceProvider;
    // private readonly List<(JoblyWorker<TContext> service, CancellationTokenSource cancellationTokenSource)> _services = new();
    private readonly List<(Task task, CancellationTokenSource cancellationTokenSource)> _services = new();
    
    // TODO: get from configuration
    private int _maxWorkers = 50;
    private DateTime _lastTick = DateTime.UtcNow;
    
    public JoblyWorkerPool(IServiceProvider serviceProvider, ILogger<JoblyWorkerPool<TContext>> logging)
    {
        _serviceProvider = serviceProvider;
        _logging = logging;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _lastTick = DateTime.UtcNow;
            foreach (var (task, cancellationTokenSource) in _services.ToList())
            {
                // if (service.ExecuteTask is {IsCompleted: false}) continue;
                // todo: handle errors
                if (!task.IsCompleted) continue;
                
                // If the service is not running, scale down, this means that the queue was empty when the service 
                // was polling for a job to process.
                cancellationTokenSource.Cancel();
                _services.Remove((task, cancellationTokenSource));
            }
            
            // The problem with this approach is that it will take a while to scale up the workers.
            if (_services.Count < _maxWorkers)
            {
                StartWorker();
            }
            _logging.LogInformation("Worker count: {0}", _services.Count);
            
            await Task.Delay(300, stoppingToken);
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