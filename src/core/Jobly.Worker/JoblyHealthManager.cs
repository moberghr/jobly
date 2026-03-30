using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Jobly health manager will be responsible for managing the health of the Jobly worker.
///
/// </summary>
public class JoblyHealthManager<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly ILogger<JoblyHealthManager<TContext>> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly JoblyWorkerConfiguration _configuration;
    private TimeSpan _previousCpuTime;
    private DateTime _previousWallTime;

    public JoblyHealthManager(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<JoblyHealthManager<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _configuration = configuration.Value;

        var process = System.Diagnostics.Process.GetCurrentProcess();
        _previousCpuTime = process.TotalProcessorTime;
        _previousWallTime = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await UpdateHeartbeat(context);

            using (var aggregateScope = _serviceScopeFactory.CreateScope())
            {
                await AggregateCounters(aggregateScope.ServiceProvider.GetRequiredService<TContext>());
            }

            await CleanUpServers(context, _configuration.HealthCheckTimeout);
            await RequeueStaleJobs(context, _configuration.InvisibilityTimeout);
            await CleanupExpiredJobs(context);

            await Task.Delay(_configuration.HealthCheckInterval, stoppingToken);
        }

        await RemoveServer();
    }

    private async Task UpdateHeartbeat(TContext context)
    {
        var server = await context.Set<Server>()
            .FindAsync(_configuration.ServerId) ?? throw new InvalidOperationException("Server not found in the database. Another health manager removed this server due to stale heartbeat.");
        server.LastHeartbeatTime = DateTime.UtcNow;

        var process = System.Diagnostics.Process.GetCurrentProcess();
        server.MemoryWorkingSetBytes = process.WorkingSet64;

        var currentCpuTime = process.TotalProcessorTime;
        var currentWallTime = DateTime.UtcNow;
        var wallElapsed = (currentWallTime - _previousWallTime).TotalMilliseconds;

        if (wallElapsed > 0)
        {
            var cpuElapsed = (currentCpuTime - _previousCpuTime).TotalMilliseconds;
            server.CpuUsagePercent = Math.Round(cpuElapsed / wallElapsed / Environment.ProcessorCount * 100, 1);
        }

        _previousCpuTime = currentCpuTime;
        _previousWallTime = currentWallTime;

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Aggregates pending Counter rows into the Statistic table.
    /// Uses a transaction to atomically read, merge, and delete counter rows.
    /// Safe for multiple servers — each batch is locked with FOR UPDATE SKIP LOCKED.
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task AggregateCounters<TCtx>(TCtx context)
        where TCtx : DbContext
    {
        var counters = await context.Set<Counter>().ToListAsync();

        if (counters.Count == 0)
        {
            return;
        }

        var grouped = counters
            .GroupBy(c => c.Key)
            .Select(g => new { Key = g.Key, Sum = g.Sum(c => c.Value) });

        foreach (var group in grouped)
        {
            var stat = await context.Set<Statistic>().FindAsync(group.Key);
            if (stat != null)
            {
                stat.Value += group.Sum;
            }
            else
            {
                context.Set<Statistic>().Add(new Statistic { Key = group.Key, Value = group.Sum });
            }
        }

        context.Set<Counter>().RemoveRange(counters);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Removes servers that have not sent a heartbeat within the timeout.
    /// Also removes their worker records. Job recovery is handled separately by RequeueStaleJobs.
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

    /// <summary>
    /// Finds jobs stuck in Processing with stale LastKeepAlive and requeues them.
    /// Uses transaction + row lock to prevent concurrent health managers from double-requeuing.
    /// Does NOT increment RetriedTimes (crash requeue is not a real failure).
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<int> RequeueStaleJobs<TCtx>(TCtx context, TimeSpan invisibilityTimeout)
        where TCtx : DbContext
    {
        var cutoff = DateTime.UtcNow - invisibilityTimeout;

        await using var transaction = await context.Database.BeginTransactionAsync();
        var staleJobs = await context.Set<Job>()
            .Where(x => x.CurrentState == State.Processing)
            .Where(x => x.LastKeepAlive != null && x.LastKeepAlive < cutoff)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .ToListAsync();

        foreach (var job in staleJobs)
        {
            job.CurrentState = State.Enqueued;
            job.CurrentWorkerId = null;
            job.LastKeepAlive = null;

            context.Set<JobLog>().Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Requeued",
                Timestamp = DateTime.UtcNow,
                Level = "Warning",
                Message = "Requeued by crash recovery — worker stopped responding",
            });
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return staleJobs.Count;
    }

    private async Task CleanupExpiredJobs(TContext context)
    {
        var count = await RunCleanup(context, _configuration.ExpirationBatchSize);
        if (count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired jobs", count);
        }
    }

    /// <summary>
    /// Deletes expired jobs, their logs/states, and expired messages.
    /// Returns the number of jobs deleted. Public static so tests can call it directly.
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

        await context.Set<Message>()
            .Where(x => x.ExpireAt != null && x.ExpireAt < DateTime.UtcNow)
            .Take(batchSize)
            .ExecuteDeleteAsync();

        // Cleanup old hourly stats (older than 7 days)
        var oldHourPrefix = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
        await context.Set<Statistic>()
            .Where(x => (x.Key.StartsWith("stats:succeeded:") || x.Key.StartsWith("stats:failed:"))
                        && x.Key.CompareTo($"stats:failed:{oldHourPrefix}") < 0)
            .ExecuteDeleteAsync();

        return expiredJobIds.Count;
    }

    private async Task RemoveServer()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var server = await context.Set<Server>()
            .FindAsync(_configuration.ServerId);

        if (server == null)
        {
            // This should only happen if this server has stalled and other server has deleted it.
            // All its jobs may have been failed.
            _logger.LogWarning("Server {ServerId} not found in the database. Skipping removal.", _configuration.ServerId);
            return;
        }

        // Remove workers for this server
        var workers = await context.Set<Jobly.Core.Data.Entities.Worker>()
            .Where(w => w.ServerId == server.Id)
            .ToListAsync();
        context.Set<Jobly.Core.Data.Entities.Worker>().RemoveRange(workers);

        context.Set<Server>().Remove(server);
        await context.SaveChangesAsync();
    }
}
