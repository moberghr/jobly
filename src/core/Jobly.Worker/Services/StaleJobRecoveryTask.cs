using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Interceptors;
using Jobly.Core.NoRestart;
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
        IJoblyLockProvider lockProvider,
        TimeProvider timeProvider)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:stale-job-recovery", lockProvider)
    {
    }

    protected override string TaskName => "StaleJobRecovery";

    protected override bool RerunImmediately => false;

    protected override TimeSpan DefaultInterval => Configuration.StaleJobRecoveryInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var result = await RecoverStaleJobs(context, TimeProvider, Configuration.InvisibilityTimeout, Configuration.RestartStaleJobsByDefault);
        if (result.Total == 0)
        {
            return null;
        }

        return $"Recovered {result.Total} stale jobs ({result.Requeued} requeued, {result.Failed} failed, {result.Deleted} deleted)";
    }

    /// <summary>
    /// Finds jobs stuck in Processing with stale LastKeepAlive and recovers them —
    /// requeueing, failing (per <see cref="ICanBeRestartedMetadata.CanBeRestarted"/>),
    /// or deleting (when cancellation was pending). Public static so tests can call it directly.
    /// </summary>
    public static async Task<StaleJobRecoveryResult> RecoverStaleJobs<TCtx>(
        TCtx context,
        TimeProvider timeProvider,
        TimeSpan invisibilityTimeout,
        bool restartByDefault = true)
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

        var requeued = 0;
        var failed = 0;
        var deleted = 0;

        foreach (var job in staleJobs)
        {
            job.CurrentWorkerId = null;
            job.LastKeepAlive = null;

            if (job.CancellationMode != CancellationMode.None)
            {
                // User intended to cancel this job — honor the intent
                job.CurrentState = State.Deleted;
                job.CancellationMode = CancellationMode.None;
                job.ExpireAt = now.AddDays(1);
                context.Set<Counter>().Add(new Counter { Key = "stats:deleted", Value = 1 });
                context.Set<JobLog>().Add(new JobLog
                {
                    JobId = job.Id,
                    EventType = "Deleted",
                    Timestamp = now,
                    Level = "Warning",
                    Message = "Cancelled by crash recovery — cancellation was pending when worker stopped",
                });
                deleted++;

                continue;
            }

            var canRestart = ReadCanBeRestarted(job.Metadata) ?? restartByDefault;

            if (canRestart)
            {
                job.CurrentState = State.Enqueued;
                context.Set<JobLog>().Add(new JobLog
                {
                    JobId = job.Id,
                    EventType = "Requeued",
                    Timestamp = now,
                    Level = "Warning",
                    Message = "Requeued by crash recovery — worker stopped responding",
                });
                requeued++;
            }
            else
            {
                job.CurrentState = State.Failed;
                job.ExpireAt = null;
                context.Set<Counter>().Add(new Counter { Key = "stats:failed", Value = 1 });
                context.Set<JobLog>().Add(new JobLog
                {
                    JobId = job.Id,
                    EventType = "Failed",
                    Timestamp = now,
                    Level = "Error",
                    Message = "Failed by crash recovery — job opted out of restart",
                });
                failed++;
            }
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new StaleJobRecoveryResult(requeued, failed, deleted);
    }

    private static bool? ReadCanBeRestarted(string? metadataJson)
    {
        if (string.IsNullOrEmpty(metadataJson))
        {
            return null;
        }

        var dict = MetadataSerializer.Deserialize(metadataJson);
        var meta = MetadataFactory.Create<ICanBeRestartedMetadata>(dict);

        return meta.CanBeRestarted;
    }
}
