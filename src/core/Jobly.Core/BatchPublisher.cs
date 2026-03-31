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

    public BatchPublisher(TContext context, IOptions<JoblyConfiguration> configuration)
    {
        _context = context;
        _joblyConfiguration = configuration.Value;
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

        // Create the batch job (replaces both the old Batch entity and placeholder job)
        var batchJob = new Job
        {
            Kind = JobKind.Batch,
            Type = name,
            CreateTime = DateTime.UtcNow,
            CurrentState = State.Awaiting,
            Queue = _joblyConfiguration.DefaultQueue ?? "default",
            ParentJobId = parentId,
            JobCount = batchJobMessages.Count,
            ContinuationOptions = options,
        };

        var batchChildJobs = batchJobMessages.ConvertAll(x => JobHelper.CreateJob(x, 0, null, null, _joblyConfiguration.DefaultQueue, batchJob.Id, batchJobsState));

        // Propagate trace from execution context
        var executionContext = JobExecutionContext.Current;
        foreach (var childJob in batchChildJobs)
        {
            if (executionContext != null)
            {
                childJob.TraceId = executionContext.TraceId;
                childJob.SpawnedByJobId = executionContext.JobId;
            }
            else
            {
                childJob.TraceId = batchJob.Id; // Batch root trace
            }
        }

        // Batch job also gets the trace
        if (executionContext != null)
        {
            batchJob.TraceId = executionContext.TraceId;
            batchJob.SpawnedByJobId = executionContext.JobId;
        }
        else
        {
            batchJob.TraceId = batchJob.Id;
        }

        var logs = new List<JobLog>();
        foreach (var job in batchChildJobs)
        {
            logs.Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Created",
                Level = "Information",
                Timestamp = DateTime.UtcNow,
                Message = $"Job created in queue \"{job.Queue}\"",
            });
        }

        logs.Add(new JobLog
        {
            JobId = batchJob.Id,
            EventType = "Created",
            Level = "Information",
            Timestamp = DateTime.UtcNow,
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
