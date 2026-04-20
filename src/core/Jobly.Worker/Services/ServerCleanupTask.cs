using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

public class ServerCleanupTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    private readonly IJoblySqlQueries<TContext> _sqlQueries;

    public ServerCleanupTask(
        IServiceScopeFactory scopeFactory,
        ILogger<ServerCleanupTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IJoblyLockProvider lockProvider,
        TimeProvider timeProvider,
        IJoblySqlQueries<TContext> sqlQueries)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:server-cleanup", lockProvider)
    {
        _sqlQueries = sqlQueries;
    }

    protected override string TaskName => "ServerCleanup";

    protected override bool RerunImmediately => false;

    protected override TimeSpan DefaultInterval => Configuration.ServerCleanupInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var count = await CleanUpServers(context, TimeProvider, Configuration.HealthCheckTimeout, _sqlQueries, ct);
        return count > 0 ? $"Removed {count} stale servers" : null;
    }

    /// <summary>
    /// Removes servers that have not sent a heartbeat within the timeout. Public static so tests
    /// can call it directly; pass <paramref name="sqlQueries"/> to reuse a cached instance,
    /// otherwise one is built from the context on every invocation.
    /// </summary>
    public static async Task<int> CleanUpServers<TCtx>(
        TCtx context,
        TimeProvider timeProvider,
        TimeSpan healthCheckTimeout,
        IJoblySqlQueries<TCtx>? sqlQueries = null,
        CancellationToken ct = default)
        where TCtx : DbContext
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var removedCount = 0;
        var queries = sqlQueries ?? JoblySqlQueriesFactory.Create(context);

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var servers = await queries.LockAllServersAsync(context, ct);
        foreach (var server in servers)
        {
            if (now - server.LastHeartbeatTime > healthCheckTimeout)
            {
                context.Set<Server>().Remove(server);

                var workers = await context.Set<Jobly.Core.Data.Entities.Worker>()
                    .Where(w => w.ServerId == server.Id)
                    .ToListAsync(ct);
                context.Set<Jobly.Core.Data.Entities.Worker>().RemoveRange(workers);

                removedCount++;
            }
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return removedCount;
    }
}
