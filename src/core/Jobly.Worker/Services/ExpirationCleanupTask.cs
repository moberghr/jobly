using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
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
        IJoblyLockProvider lockProvider,
        TimeProvider timeProvider)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:expiration-cleanup", lockProvider)
    {
    }

    protected override string TaskName => "ExpirationCleanup";

    protected override TimeSpan DefaultInterval => Configuration.ExpirationCleanupInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var timeExpired = await RunCleanup(context, TimeProvider, Configuration.ExpirationBatchSize);
        var countCleaned = Configuration.MaxExpirableJobCount.HasValue
            ? await RunCountBasedCleanup(context, Configuration.MaxExpirableJobCount.Value, Configuration.ExpirationBatchSize)
            : 0;

        await CleanupRecurringJobLogs(context);

        var total = timeExpired + countCleaned;
        if (total == 0)
        {
            return null;
        }

        return countCleaned > 0
            ? $"Cleaned up {timeExpired} expired + {countCleaned} over-threshold jobs"
            : $"Cleaned up {timeExpired} expired jobs";
    }

    /// <summary>
    /// Deletes expired jobs, their logs, old hourly stats, and old server logs.
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<int> RunCleanup<TCtx>(TCtx context, TimeProvider timeProvider, int batchSize = 1000)
        where TCtx : DbContext
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Fetch expired jobs, excluding parents whose children haven't expired yet (FK safety)
        var expiredJobIds = await context.Set<Job>()
            .Where(x => x.ExpireAt != null && x.ExpireAt < now)
            .Where(x => !x.ChildJobs.Any(c => c.ExpireAt == null || c.ExpireAt >= now))
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
        var oldHourPrefix = now.AddDays(-7).ToString("yyyy-MM-dd");
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
            var cutoff = now.AddSeconds(-retentionSeconds);
            await context.Set<ServerLog>()
                .Where(x => x.ServerTaskId == task.Id && x.Timestamp < cutoff)
                .ExecuteDeleteAsync();
        }

        // Cleanup orphaned server logs (no task) older than 1 day
        await context.Set<ServerLog>()
            .Where(x => x.ServerTaskId == null && x.Timestamp < now.AddDays(-1))
            .ExecuteDeleteAsync();

        // ServerTasks and ServerLogs for dead servers are cleaned up via cascade delete
        // when ServerCleanupTask removes the Server record.
        return expiredJobIds.Count;
    }

    /// <summary>
    /// If the number of jobs with non-null ExpireAt exceeds maxCount, delete the oldest
    /// by ExpireAt until at the threshold. Failed jobs are naturally excluded (null ExpireAt).
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<int> RunCountBasedCleanup<TCtx>(TCtx context, int maxCount, int batchSize)
        where TCtx : DbContext
    {
        var totalDeleted = 0;

        while (true)
        {
            var expirableCount = await context.Set<Job>()
                .Where(x => x.ExpireAt != null)
                .CountAsync();

            if (expirableCount <= maxCount)
            {
                break;
            }

            var toDelete = Math.Min(expirableCount - maxCount, batchSize);

            var jobIds = await context.Set<Job>()
                .Where(x => x.ExpireAt != null)
                .Where(x => !x.ChildJobs.Any(c => c.ExpireAt == null || c.ExpireAt >= DateTime.UtcNow))
                .OrderBy(x => x.ExpireAt)
                .Select(x => x.Id)
                .Take(toDelete)
                .ToListAsync();

            if (jobIds.Count == 0)
            {
                break;
            }

            await context.Set<JobLog>()
                .Where(x => jobIds.Contains(x.JobId))
                .ExecuteDeleteAsync();

            await context.Set<Job>()
                .Where(x => jobIds.Contains(x.Id))
                .ExecuteDeleteAsync();

            totalDeleted += jobIds.Count;
        }

        return totalDeleted;
    }

    /// <summary>
    /// Keeps only the last 100 RecurringJobLog entries per recurring job.
    /// </summary>
    public static async Task CleanupRecurringJobLogs<TCtx>(TCtx context)
        where TCtx : DbContext
    {
        var recurringJobIds = await context.Set<RecurringJobLog>()
            .GroupBy(l => l.RecurringJobId)
            .Where(g => g.Count() > 100)
            .Select(g => g.Key)
            .ToListAsync();

        foreach (var recurringJobId in recurringJobIds)
        {
            var idsToKeep = await context.Set<RecurringJobLog>()
                .Where(l => l.RecurringJobId == recurringJobId)
                .OrderByDescending(l => l.CreatedAt)
                .Take(100)
                .Select(l => l.Id)
                .ToListAsync();

            await context.Set<RecurringJobLog>()
                .Where(l => l.RecurringJobId == recurringJobId && !idsToKeep.Contains(l.Id))
                .ExecuteDeleteAsync();
        }
    }
}
