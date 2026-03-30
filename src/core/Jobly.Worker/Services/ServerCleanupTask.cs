using Jobly.Core.Data.Entities;
using Jobly.Core.Interceptors;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

public class ServerCleanupTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    public ServerCleanupTask(
        IServiceScopeFactory scopeFactory,
        ILogger<ServerCleanupTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IDistributedLockProvider lockProvider)
        : base(scopeFactory, logger, configuration, "jobly:server-cleanup", lockProvider)
    {
    }

    protected override string TaskName => "ServerCleanup";

    protected override TimeSpan DefaultInterval => Configuration.ServerCleanupInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var count = await CleanUpServers(context, Configuration.HealthCheckTimeout);
        return count > 0 ? $"Removed {count} stale servers" : null;
    }

    /// <summary>
    /// Removes servers that have not sent a heartbeat within the timeout.
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<int> CleanUpServers<TCtx>(TCtx context, TimeSpan healthCheckTimeout)
        where TCtx : DbContext
    {
        var removedCount = 0;
        await using var transaction = await context.Database.BeginTransactionAsync();
        var servers = await context.Set<Server>()
            .TagWith(InterceptorConstants.RowLockTableBatch)
            .ToListAsync();
        foreach (var server in servers)
        {
            if (DateTime.UtcNow - server.LastHeartbeatTime > healthCheckTimeout)
            {
                context.Set<Server>().Remove(server);

                var workers = await context.Set<Jobly.Core.Data.Entities.Worker>()
                    .Where(w => w.ServerId == server.Id)
                    .ToListAsync();
                context.Set<Jobly.Core.Data.Entities.Worker>().RemoveRange(workers);

                removedCount++;
            }
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return removedCount;
    }
}
