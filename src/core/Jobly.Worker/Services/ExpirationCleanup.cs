using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Deletes expired jobs + logs, trims old hourly stats, deletes server logs past their
/// per-task retention, and caps RecurringJobLog history. Also handles count-based
/// cleanup when <see cref="JoblyWorkerConfiguration.MaxExpirableJobCount"/> is set.
/// </summary>
public sealed class ExpirationCleanup<TContext> : IServerTask
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _time;
    private readonly JoblyWorkerConfiguration _configuration;

    public ExpirationCleanup(
        TContext context,
        TimeProvider time,
        IOptions<JoblyWorkerConfiguration> configuration)
    {
        _context = context;
        _time = time;
        _configuration = configuration.Value;
    }

    public string Name => "ExpirationCleanup";

    public string? LockKey => "jobly:expiration-cleanup";

    public TimeSpan? DefaultInterval => _configuration.ExpirationCleanupInterval;

    public async Task<string?> ExecuteAsync(CancellationToken ct)
    {
        var timeExpired = await RunCleanupAsync(ct);
        var countCleaned = _configuration.MaxExpirableJobCount.HasValue
            ? await RunCountBasedCleanupAsync(_configuration.MaxExpirableJobCount.Value, _configuration.ExpirationBatchSize, ct)
            : 0;

        await CleanupRecurringJobLogsAsync(ct);

        var total = timeExpired + countCleaned;
        if (total == 0)
        {
            return null;
        }

        return countCleaned > 0
            ? $"Cleaned up {timeExpired} expired + {countCleaned} over-threshold jobs"
            : $"Cleaned up {timeExpired} expired jobs";
    }

    internal async Task<int> RunCleanupAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var batchSize = _configuration.ExpirationBatchSize;

        var expiredJobIds = await _context.Set<Job>()
            .Where(x => x.ExpireAt != null && x.ExpireAt < now)
            .Where(x => !x.ChildJobs.Any(c => c.ExpireAt == null || c.ExpireAt >= now))
            .Select(x => x.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (expiredJobIds.Count == 0)
        {
            return 0;
        }

        await _context.Set<JobLog>()
            .Where(x => expiredJobIds.Contains(x.JobId))
            .ExecuteDeleteAsync(ct);

        await _context.Set<Job>()
            .Where(x => expiredJobIds.Contains(x.Id))
            .ExecuteDeleteAsync(ct);

        var oldHourPrefix = now.AddDays(-7).ToString("yyyy-MM-dd");
        await _context.Set<Statistic>()
            .Where(x => (x.Key.StartsWith("stats:succeeded:") || x.Key.StartsWith("stats:failed:"))
                        && x.Key.CompareTo($"stats:failed:{oldHourPrefix}") < 0)
            .ExecuteDeleteAsync(ct);

        var serverTasks = await _context.Set<ServerTask>()
            .Select(x => new { x.Id, x.IntervalSeconds })
            .ToListAsync(ct);

        foreach (var task in serverTasks)
        {
            var retentionSeconds = (task.IntervalSeconds ?? 60) * 300;
            var cutoff = now.AddSeconds(-retentionSeconds);
            await _context.Set<ServerLog>()
                .Where(x => x.ServerTaskId == task.Id && x.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);
        }

        await _context.Set<ServerLog>()
            .Where(x => x.ServerTaskId == null && x.Timestamp < now.AddDays(-1))
            .ExecuteDeleteAsync(ct);

        return expiredJobIds.Count;
    }

    internal async Task<int> RunCountBasedCleanupAsync(int maxCount, int batchSize, CancellationToken ct)
    {
        var totalDeleted = 0;

        while (true)
        {
            var expirableCount = await _context.Set<Job>()
                .Where(x => x.ExpireAt != null)
                .CountAsync(ct);

            if (expirableCount <= maxCount)
            {
                break;
            }

            var toDelete = Math.Min(expirableCount - maxCount, batchSize);
            var now = _time.GetUtcNow().UtcDateTime;

            var jobIds = await _context.Set<Job>()
                .Where(x => x.ExpireAt != null)
                .Where(x => !x.ChildJobs.Any(c => c.ExpireAt == null || c.ExpireAt >= now))
                .OrderBy(x => x.ExpireAt)
                .Select(x => x.Id)
                .Take(toDelete)
                .ToListAsync(ct);

            if (jobIds.Count == 0)
            {
                break;
            }

            await _context.Set<JobLog>()
                .Where(x => jobIds.Contains(x.JobId))
                .ExecuteDeleteAsync(ct);

            await _context.Set<Job>()
                .Where(x => jobIds.Contains(x.Id))
                .ExecuteDeleteAsync(ct);

            totalDeleted += jobIds.Count;
        }

        return totalDeleted;
    }

    internal async Task CleanupRecurringJobLogsAsync(CancellationToken ct)
    {
        var recurringJobIds = await _context.Set<RecurringJobLog>()
            .GroupBy(l => l.RecurringJobId)
            .Where(g => g.Count() > 100)
            .Select(g => g.Key)
            .ToListAsync(ct);

        foreach (var recurringJobId in recurringJobIds)
        {
            var idsToKeep = await _context.Set<RecurringJobLog>()
                .Where(l => l.RecurringJobId == recurringJobId)
                .OrderByDescending(l => l.CreatedAt)
                .Take(100)
                .Select(l => l.Id)
                .ToListAsync(ct);

            await _context.Set<RecurringJobLog>()
                .Where(l => l.RecurringJobId == recurringJobId && !idsToKeep.Contains(l.Id))
                .ExecuteDeleteAsync(ct);
        }
    }
}
