using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Core;

public interface IBatchPublisher
{
    Task<Guid> StartNew<T>(List<T> batchJobMessages, string? name = null, ContinuationOptions options = ContinuationOptions.OnlyOnSucceeded)
        where T : class, IJob;

    Task<Guid> ContinueBatchWith<T>(List<T> batchJobMessages, Guid parentId, string? name = null, ContinuationOptions options = ContinuationOptions.OnlyOnSucceeded)
        where T : class, IJob;

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class BatchPublisher<TContext> : IBatchPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly JoblyConfiguration _joblyConfiguration;
    private readonly TimeProvider _timeProvider;

    public BatchPublisher(TContext context, IOptions<JoblyConfiguration> configuration, TimeProvider timeProvider)
    {
        _context = context;
        _joblyConfiguration = configuration.Value;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> StartNew<T>(List<T> batchJobMessages, string? name = null, ContinuationOptions options = ContinuationOptions.OnlyOnSucceeded)
        where T : class, IJob
    {
        return await BaseCreateBatch(batchJobMessages, State.Enqueued, null, name, options);
    }

    public async Task<Guid> ContinueBatchWith<T>(List<T> batchJobMessages, Guid parentId, string? name = null, ContinuationOptions options = ContinuationOptions.OnlyOnSucceeded)
        where T : class, IJob
    {
        return await BaseCreateBatch(batchJobMessages, State.Awaiting, parentId, name, options);
    }

    private async Task<Guid> BaseCreateBatch<T>(List<T> batchJobMessages, State batchJobsState, Guid? parentId, string? name, ContinuationOptions options)
        where T : class, IJob
    {
        if (batchJobMessages == null || batchJobMessages.Count == 0)
        {
            throw new ArgumentException("List cannot be empty", nameof(batchJobMessages));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Create the batch job (replaces both the old Batch entity and placeholder job)
        // StartNew (no parent) → Processing immediately; continuation → Awaiting until parent finishes
        var batchJob = new Job
        {
            Kind = JobKind.Batch,
            Type = name,
            CreateTime = now,
            CurrentState = parentId != null ? State.Awaiting : State.Processing,
            Queue = _joblyConfiguration.DefaultQueue ?? "default",
            ParentJobId = parentId,
            JobCount = batchJobMessages.Count,
            ContinuationOptions = options,
        };

        var batchChildJobs = batchJobMessages.ConvertAll(x => JobHelper.CreateJob(x, 0, null, null, _joblyConfiguration.DefaultQueue, batchJob.Id, batchJobsState, now));

        // Propagate trace: execution context > parent's trace > self
        var executionContext = JobExecutionContext.Current;
        Guid? traceId = null;
        Guid? spawnedBy = null;

        if (executionContext != null)
        {
            traceId = executionContext.TraceId;
            spawnedBy = executionContext.JobId;
        }
        else if (parentId != null)
        {
            // Inherit trace from parent — check change tracker first (parent may not be committed yet)
            var trackedParent = _context.ChangeTracker.Entries<Job>()
                .FirstOrDefault(e => e.Entity.Id == parentId);
            traceId = trackedParent?.Entity.TraceId
                ?? await _context.Set<Job>()
                    .Where(x => x.Id == parentId)
                    .Select(x => x.TraceId)
                    .FirstOrDefaultAsync();
        }

        batchJob.TraceId = traceId ?? batchJob.Id;
        batchJob.SpawnedByJobId = spawnedBy;

        foreach (var childJob in batchChildJobs)
        {
            childJob.TraceId = batchJob.TraceId;
            childJob.SpawnedByJobId = spawnedBy;
        }

        var logs = new List<JobLog>();
        foreach (var job in batchChildJobs)
        {
            logs.Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Created",
                Level = "Information",
                Timestamp = now,
                Message = $"Job created in queue \"{job.Queue}\"",
            });
        }

        logs.Add(new JobLog
        {
            JobId = batchJob.Id,
            EventType = "Created",
            Level = "Information",
            Timestamp = now,
            Message = $"Batch job created in queue \"{batchJob.Queue}\"",
        });

        _context.Set<Job>().AddRange(batchChildJobs);
        _context.Set<Job>().Add(batchJob);
        _context.Set<JobLog>().AddRange(logs);

        return batchJob.Id;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
