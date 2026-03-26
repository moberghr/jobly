using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Registers the server and workers in the database, then starts the worker background services.
/// </summary>
public class JoblyWorkerSetup<TContext> : IHostedService where TContext : DbContext
{
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<JoblyWorker<TContext>> _workers = new();

    public JoblyWorkerSetup(
        IOptions<JoblyWorkerConfiguration> configuration,
        IServiceScopeFactory serviceScopeFactory,
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _serviceScopeFactory = serviceScopeFactory;
        _configuration = configuration.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workerIds = new List<Guid>();
        for (var i = 0; i < _configuration.WorkerCount; i++)
        {
            workerIds.Add(Guid.NewGuid());
        }

        // Register server and all workers in one transaction
        using (var scope = _serviceScopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var now = DateTime.UtcNow;

            var server = new Server
            {
                Id = _configuration.ServerId,
                StartedTime = now,
                LastHeartbeatTime = now,
                ServiceCount = _configuration.WorkerCount
            };
            await context.Set<Server>().AddAsync(server, cancellationToken);

            foreach (var workerId in workerIds)
            {
                await context.Set<Jobly.Core.Data.Entities.Worker>().AddAsync(new Jobly.Core.Data.Entities.Worker
                {
                    Id = workerId,
                    ServerId = _configuration.ServerId,
                    StartedTime = now,
                    LastHeartbeatTime = now
                }, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // Start worker background services with their assigned IDs
        for (var i = 0; i < _configuration.WorkerCount; i++)
        {
            var workerService = new JoblyWorkerService<TContext>(
                workerIds[i],
                _serviceScopeFactory,
                _serviceProvider.GetRequiredService<ILogger<JoblyWorkerService<TContext>>>(),
                _serviceProvider.GetRequiredService<IOptions<JoblyWorkerConfiguration>>());

            var worker = new JoblyWorker<TContext>(workerService,
                _serviceProvider.GetRequiredService<ILogger<JoblyWorker<TContext>>>());

            await worker.StartAsync(cancellationToken);
            _workers.Add(worker);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var tasks = _workers.Select(worker => worker.StopAsync(cancellationToken));
        return Task.WhenAll(tasks);
    }
}
