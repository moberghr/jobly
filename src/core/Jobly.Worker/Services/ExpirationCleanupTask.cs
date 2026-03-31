using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

public class ExpirationCleanupTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    public ExpirationCleanupTask(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpirationCleanupTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IDistributedLockProvider lockProvider)
        : base(scopeFactory, logger, configuration, "jobly:expiration-cleanup", lockProvider)
    {
    }

    protected override string TaskName => "ExpirationCleanup";

    protected override TimeSpan DefaultInterval => Configuration.ExpirationCleanupInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var count = await RunCleanup(context, Configuration.ExpirationBatchSize);
        return count > 0 ? $"Cleaned up {count} expired jobs" : null;
    }

    /// <summary>
    /// Deletes expired jobs, their logs, old hourly stats, and old server logs.
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<int> RunCleanup<TCtx>(TCtx context, int batchSize = 1000)
        where TCtx : DbContext
    {
        var expiredJobIds = await context.Set<Job>()
            .Where(x => x.ExpireAt != null && x.ExpireAt < DateTime.UtcNow)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToListAsync();

        if (expiredJobIds.Count == 0)
        {
            return 0;
        }

        await context.Set<JobLog>()
            .Where(x => expiredJobIds.Contains(x.JobId))
            .ExecuteDeleteAsync();

        await context.Set<Job>()
            .Where(x => expiredJobIds.Contains(x.Id))
            .ExecuteDeleteAsync();

        // Cleanup old hourly stats (older than 7 days)
        var oldHourPrefix = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
        await context.Set<Statistic>()
            .Where(x => (x.Key.StartsWith("stats:succeeded:") || x.Key.StartsWith("stats:failed:"))
                        && x.Key.CompareTo($"stats:failed:{oldHourPrefix}") < 0)
            .ExecuteDeleteAsync();

        // Cleanup server logs — retention = interval * 300 per task
        var serverTasks = await context.Set<ServerTask>()
            .Select(x => new { x.Id, x.IntervalSeconds })
            .ToListAsync();

        foreach (var task in serverTasks)
        {
            var retentionSeconds = (task.IntervalSeconds ?? 60) * 300;
            var cutoff = DateTime.UtcNow.AddSeconds(-retentionSeconds);
            await context.Set<ServerLog>()
                .Where(x => x.ServerTaskId == task.Id && x.Timestamp < cutoff)
                .ExecuteDeleteAsync();
        }

        // Cleanup orphaned server logs (no task) older than 1 day
        await context.Set<ServerLog>()
            .Where(x => x.ServerTaskId == null && x.Timestamp < DateTime.UtcNow.AddDays(-1))
            .ExecuteDeleteAsync();

        // ServerTasks and ServerLogs for dead servers are cleaned up via cascade delete
        // when ServerCleanupTask removes the Server record.

        return expiredJobIds.Count;
    }
}
