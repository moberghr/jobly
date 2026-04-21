using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Registers the Server, WorkerGroup, and Worker rows in the database on startup and removes
/// them on graceful shutdown. Populates <see cref="ServerRegistrationState"/> with the generated
/// IDs so the worker host services can find them without re-querying. Also initializes
/// <see cref="PauseStateHolder"/>.
/// </summary>
public class JoblyServerRegistration<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly ServerRegistrationState _state;

    public JoblyServerRegistration(
        IOptions<JoblyWorkerConfiguration> configuration,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider,
        PauseStateHolder pauseStateHolder,
        ServerRegistrationState state)
    {
        _configuration = configuration.Value;
        _serviceScopeFactory = serviceScopeFactory;
        _timeProvider = timeProvider;
        _pauseStateHolder = pauseStateHolder;
        _state = state;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workerGroups = _configuration.GetEffectiveWorkerGroups();
        var totalWorkerCount = workerGroups.Sum(g => g.WorkerCount);

        using var scope = _serviceScopeFactory.CreateScope();
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

        var registrations = new List<ServerRegistrationState.GroupRegistration>();
        foreach (var group in workerGroups)
        {
            var workerGroup = new Jobly.Core.Data.Entities.WorkerGroup
            {
                ServerId = _configuration.ServerId,
                WorkerCount = group.WorkerCount,
                Queues = string.Join(",", group.Queues),
                PollingIntervalMs = group.PollingInterval.TotalMilliseconds,
            };
            await context.Set<Jobly.Core.Data.Entities.WorkerGroup>().AddAsync(workerGroup, cancellationToken);

            var workerIds = new List<Guid>(group.WorkerCount);
            for (var i = 0; i < group.WorkerCount; i++)
            {
                var workerId = Guid.NewGuid();
                workerIds.Add(workerId);

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

            registrations.Add(new ServerRegistrationState.GroupRegistration(group, workerGroup.Id, workerIds));
        }

        await context.SaveChangesAsync(cancellationToken);

        _state.Set(registrations);

        // Initialize PauseStateHolder from DB before workers start, so they never see a stale
        // "not paused" default on startup.
        var groupPauseStates = registrations.ToDictionary(x => x.GroupEntityId, _ => false);
        _pauseStateHolder.Update(false, groupPauseStates);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var server = await context.Set<Server>().FindAsync([_configuration.ServerId], cancellationToken);
        if (server == null)
        {
            return;
        }

        var workers = await context.Set<Jobly.Core.Data.Entities.Worker>()
            .Where(x => x.ServerId == server.Id)
            .ToListAsync(cancellationToken);
        context.Set<Jobly.Core.Data.Entities.Worker>().RemoveRange(workers);

        // WorkerGroup rows are FK-linked to Server without OnDelete(Cascade) — we must remove
        // them ourselves on graceful shutdown. The crash-recovery path is covered by
        // ServerCleanup, which cleans up WorkerGroup rows for timed-out servers.
        var workerGroups = await context.Set<Jobly.Core.Data.Entities.WorkerGroup>()
            .Where(x => x.ServerId == server.Id)
            .ToListAsync(cancellationToken);
        context.Set<Jobly.Core.Data.Entities.WorkerGroup>().RemoveRange(workerGroups);

        context.Set<Server>().Remove(server);
        await context.SaveChangesAsync(cancellationToken);
    }
}
