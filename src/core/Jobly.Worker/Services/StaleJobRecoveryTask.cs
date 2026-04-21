using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.NoRestart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

public class StaleJobRecoveryTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    private readonly IJoblySqlQueries<TContext> _sqlQueries;

    public StaleJobRecoveryTask(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleJobRecoveryTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IJoblyLockProvider lockProvider,
        TimeProvider timeProvider,
        IJoblySqlQueries<TContext> sqlQueries)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:stale-job-recovery", lockProvider)
    {
        _sqlQueries = sqlQueries;
    }

    protected override string TaskName => "StaleJobRecovery";

    protected override bool RerunImmediately => false;

    protected override TimeSpan DefaultInterval => Configuration.StaleJobRecoveryInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var result = await RecoverStaleJobsAsync(context, ct);
        if (result.Total == 0)
        {
            return null;
        }

        return $"Recovered {result.Total} stale jobs ({result.Requeued} requeued, {result.Failed} failed, {result.Deleted} deleted)";
    }

    public async Task<StaleJobRecoveryResult> RecoverStaleJobsAsync(TContext context, CancellationToken ct)
    {
        var now = TimeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - Configuration.InvisibilityTimeout;
        var restartByDefault = Configuration.RestartStaleJobsByDefault;

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var staleJobs = await _sqlQueries.LockStaleProcessingJobsAsync(context, cutoff, ct);

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

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

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
