using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.NoRestart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Finds jobs in <see cref="State.Processing"/> whose worker stopped refreshing
/// <c>LastKeepAlive</c> past <see cref="JoblyWorkerConfiguration.InvisibilityTimeout"/>
/// and either requeues them, fails them, or honors a pending cancellation.
/// </summary>
public sealed class StaleJobRecovery<TContext> : IServerTask
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _time;
    private readonly IJoblySqlQueries<TContext> _sqlQueries;
    private readonly JoblyWorkerConfiguration _configuration;

    public StaleJobRecovery(
        TContext context,
        TimeProvider time,
        IJoblySqlQueries<TContext> sqlQueries,
        IOptions<JoblyWorkerConfiguration> configuration)
    {
        _context = context;
        _time = time;
        _sqlQueries = sqlQueries;
        _configuration = configuration.Value;
    }

    public string Name => "StaleJobRecovery";

    public string? LockKey => "jobly:stale-job-recovery";

    public TimeSpan? DefaultInterval => _configuration.StaleJobRecoveryInterval;

    public bool RerunImmediately => false;

    public async Task<string?> ExecuteAsync(CancellationToken ct)
    {
        var result = await RecoverStaleJobsAsync(ct);
        if (result.Total == 0)
        {
            return null;
        }

        return $"Recovered {result.Total} stale jobs ({result.Requeued} requeued, {result.Failed} failed, {result.Deleted} deleted)";
    }

    internal async Task<StaleJobRecoveryResult> RecoverStaleJobsAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var cutoff = now - _configuration.InvisibilityTimeout;
        var restartByDefault = _configuration.RestartStaleJobsByDefault;

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        var staleJobs = await _sqlQueries.LockStaleProcessingJobsAsync(_context, cutoff, ct);

        var requeued = 0;
        var failed = 0;
        var deleted = 0;

        foreach (var job in staleJobs)
        {
            job.CurrentWorkerId = null;
            job.LastKeepAlive = null;

            if (job.CancellationMode != CancellationMode.None)
            {
                job.CurrentState = State.Deleted;
                job.CancellationMode = CancellationMode.None;
                job.ExpireAt = now.AddDays(1);
                _context.Set<Counter>().Add(new Counter { Key = "stats:deleted", Value = 1 });
                _context.Set<JobLog>().Add(new JobLog
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
                _context.Set<JobLog>().Add(new JobLog
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
                _context.Set<Counter>().Add(new Counter { Key = "stats:failed", Value = 1 });
                _context.Set<JobLog>().Add(new JobLog
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

        await _context.SaveChangesAsync(ct);
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
