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
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly List<BackgroundService> _workers = [];

    public JoblyWorkerSetup(
        IOptions<JoblyWorkerConfiguration> configuration,
        IServiceScopeFactory serviceScopeFactory,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        PauseStateHolder pauseStateHolder)
    {
        _serviceProvider = serviceProvider;
        _serviceScopeFactory = serviceScopeFactory;
        _configuration = configuration.Value;
        _timeProvider = timeProvider;
        _pauseStateHolder = pauseStateHolder;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workerGroups = _configuration.GetEffectiveWorkerGroups();
        var totalWorkerCount = workerGroups.Sum(g => g.WorkerCount);

        // Build worker IDs and group entity IDs per group
        var workerIdsPerGroup = new List<(WorkerGroupConfiguration Group, List<Guid> WorkerIds, Guid GroupEntityId)>();
        foreach (var group in workerGroups)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < group.WorkerCount; i++)
            {
                ids.Add(Guid.NewGuid());
            }

            workerIdsPerGroup.Add((group, ids, Guid.Empty));
        }

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

            for (var g = 0; g < workerIdsPerGroup.Count; g++)
            {
                var (group, ids, _) = workerIdsPerGroup[g];
                var workerGroup = new Jobly.Core.Data.Entities.WorkerGroup
                {
                    ServerId = _configuration.ServerId,
                    WorkerCount = group.WorkerCount,
                    Queues = string.Join(",", group.Queues),
                    PollingIntervalMs = group.PollingInterval.TotalMilliseconds,
                };
                await context.Set<Jobly.Core.Data.Entities.WorkerGroup>().AddAsync(workerGroup, cancellationToken);

                // Capture the generated WorkerGroup entity ID
                workerIdsPerGroup[g] = (group, ids, workerGroup.Id);

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

            // Initialize PauseStateHolder from DB before starting workers,
            // so workers never see a stale "not paused" default on startup.
            var groupPauseStates = workerIdsPerGroup
                .ToDictionary(x => x.GroupEntityId, _ => false);
            _pauseStateHolder.Update(false, groupPauseStates);
        }

        // Start worker background services per group
        var workerIndex = 0;
        foreach (var (group, workerIds, groupEntityId) in workerIdsPerGroup)
        {
            if (_configuration.UseDispatcher)
            {
                var dispatcher = new JoblyDispatcher<TContext>(
                    _serviceScopeFactory,
                    _serviceProvider.GetRequiredService<ILogger<JoblyDispatcher<TContext>>>(),
                    _serviceProvider.GetRequiredService<IOptions<JoblyWorkerConfiguration>>(),
                    group,
                    _timeProvider,
                    _pauseStateHolder,
                    groupEntityId);

                await dispatcher.StartAsync(cancellationToken);
                _workers.Add(dispatcher);

                for (var i = 0; i < group.WorkerCount; i++)
                {
                    var worker = new JoblyDispatcherWorker<TContext>(
                        workerIds[i],
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
                        workerIds[i],
                        _serviceScopeFactory,
                        _serviceProvider.GetRequiredService<ILogger<JoblyWorkerService<TContext>>>(),
                        _serviceProvider.GetRequiredService<IOptions<JoblyWorkerConfiguration>>(),
                        group,
                        _timeProvider,
                        _serviceProvider.GetRequiredService<IDistributedLockProvider>());

                    var worker = new JoblyWorker<TContext>(
                        workerService,
                        _serviceProvider.GetRequiredService<ILogger<JoblyWorker<TContext>>>(),
                        group,
                        _pauseStateHolder,
                        groupEntityId);

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
