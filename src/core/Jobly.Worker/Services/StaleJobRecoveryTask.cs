using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Interceptors;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

public class StaleJobRecoveryTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    public StaleJobRecoveryTask(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleJobRecoveryTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IDistributedLockProvider lockProvider,
        TimeProvider timeProvider)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:stale-job-recovery", lockProvider)
    {
    }

    protected override string TaskName => "StaleJobRecovery";

    protected override TimeSpan DefaultInterval => Configuration.StaleJobRecoveryInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var count = await RequeueStaleJobs(context, TimeProvider, Configuration.InvisibilityTimeout);
        return count > 0 ? $"Requeued {count} stale jobs" : null;
    }

    /// <summary>
    /// Finds jobs stuck in Processing with stale LastKeepAlive and requeues them.
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<int> RequeueStaleJobs<TCtx>(TCtx context, TimeProvider timeProvider, TimeSpan invisibilityTimeout)
        where TCtx : DbContext
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - invisibilityTimeout;

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
                Timestamp = now,
                Level = "Warning",
                Message = "Requeued by crash recovery — worker stopped responding",
            });
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return staleJobs.Count;
    }
}
