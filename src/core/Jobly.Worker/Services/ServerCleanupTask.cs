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
        var count = await CleanUpServersAsync(context, ct);
        return count > 0 ? $"Removed {count} stale servers" : null;
    }

    public async Task<int> CleanUpServersAsync(TContext context, CancellationToken ct)
    {
        var now = TimeProvider.GetUtcNow().UtcDateTime;
        var removedCount = 0;

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var servers = await _sqlQueries.LockAllServersAsync(context, ct);
        foreach (var server in servers)
        {
            if (now - server.LastHeartbeatTime > Configuration.HealthCheckTimeout)
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
