using Jobly.Core.Data.Entities;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Registers the server and workers in the database, then starts the worker background services.
/// </summary>
public class JoblyWorkerSetup<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly List<BackgroundService> _workers = [];

    public JoblyWorkerSetup(
        IOptions<JoblyWorkerConfiguration> configuration,
        IServiceScopeFactory serviceScopeFactory,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _serviceScopeFactory = serviceScopeFactory;
        _configuration = configuration.Value;
        _timeProvider = timeProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workerGroups = _configuration.GetEffectiveWorkerGroups();
        var totalWorkerCount = workerGroups.Sum(g => g.WorkerCount);

        // Build worker IDs per group
        var workerIdsPerGroup = new List<(WorkerGroupConfiguration Group, List<Guid> Ids)>();
        foreach (var group in workerGroups)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < group.WorkerCount; i++)
            {
                ids.Add(Guid.NewGuid());
            }

            workerIdsPerGroup.Add((group, ids));
        }

        var allWorkerIds = workerIdsPerGroup.SelectMany(g => g.Ids).ToList();

        // Register server and all workers in one transaction
        using (var scope = _serviceScopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var server = new Server
            {
                Id = _configuration.ServerId,
                ServerName = _configuration.ServerName ?? $"{Environment.MachineName}.{_configuration.ServerId}",
                StartedTime = now,
                LastHeartbeatTime = now,
                ServiceCount = totalWorkerCount,
            };
            await context.Set<Server>().AddAsync(server, cancellationToken);

            foreach (var (group, ids) in workerIdsPerGroup)
            {
                var workerGroup = new Jobly.Core.Data.Entities.WorkerGroup
                {
                    ServerId = _configuration.ServerId,
                    WorkerCount = group.WorkerCount,
                    Queues = string.Join(",", group.Queues),
                    PollingIntervalMs = group.PollingInterval.TotalMilliseconds,
                };
                await context.Set<Jobly.Core.Data.Entities.WorkerGroup>().AddAsync(workerGroup, cancellationToken);

                foreach (var workerId in ids)
                {
                    await context.Set<Jobly.Core.Data.Entities.Worker>().AddAsync(
                        new Jobly.Core.Data.Entities.Worker
                        {
                            Id = workerId,
                            ServerId = _configuration.ServerId,
                            StartedTime = now,
                            LastHeartbeatTime = now,
                            WorkerGroupId = workerGroup.Id,
                        },
                        cancellationToken);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // Start worker background services per group
        var workerIndex = 0;
        foreach (var group in workerGroups)
        {
            if (_configuration.UseDispatcher)
            {
                var dispatcher = new JoblyDispatcher<TContext>(
                    _serviceScopeFactory,
                    _serviceProvider.GetRequiredService<ILogger<JoblyDispatcher<TContext>>>(),
                    _serviceProvider.GetRequiredService<IOptions<JoblyWorkerConfiguration>>(),
                    group,
                    _timeProvider);

                await dispatcher.StartAsync(cancellationToken);
                _workers.Add(dispatcher);

                for (var i = 0; i < group.WorkerCount; i++)
                {
                    var worker = new JoblyDispatcherWorker<TContext>(
                        allWorkerIds[workerIndex],
                        dispatcher.JobReader,
                        _serviceScopeFactory,
                        _serviceProvider.GetRequiredService<ILogger<JoblyDispatcherWorker<TContext>>>(),
                        _serviceProvider.GetRequiredService<IOptions<JoblyWorkerConfiguration>>(),
                        _timeProvider);

                    await worker.StartAsync(cancellationToken);
                    _workers.Add(worker);
                    workerIndex++;
                }
            }
            else
            {
                for (var i = 0; i < group.WorkerCount; i++)
                {
                    var workerService = new JoblyWorkerService<TContext>(
                        allWorkerIds[workerIndex],
                        _serviceScopeFactory,
                        _serviceProvider.GetRequiredService<ILogger<JoblyWorkerService<TContext>>>(),
                        _serviceProvider.GetRequiredService<IOptions<JoblyWorkerConfiguration>>(),
                        group,
                        _timeProvider,
                        _serviceProvider.GetRequiredService<IDistributedLockProvider>());

                    var worker = new JoblyWorker<TContext>(
                        workerService,
                        _serviceProvider.GetRequiredService<ILogger<JoblyWorker<TContext>>>(),
                        group);

                    await worker.StartAsync(cancellationToken);
                    _workers.Add(worker);
                    workerIndex++;
                }
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var tasks = _workers.Select(worker => worker.StopAsync(cancellationToken));
        await Task.WhenAll(tasks);

        // Remove server and workers from the database on graceful shutdown
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var server = await context.Set<Server>().FindAsync([_configuration.ServerId], cancellationToken);
        if (server != null)
        {
            var workers = await context.Set<Jobly.Core.Data.Entities.Worker>()
                .Where(w => w.ServerId == server.Id)
                .ToListAsync(cancellationToken);
            context.Set<Jobly.Core.Data.Entities.Worker>().RemoveRange(workers);
            context.Set<Server>().Remove(server);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
