using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Jobly.Core;

public interface IBatchPublisher
{
    Task<Guid> StartNew<T>(List<T> batchJobMessages, BatchContinuationOptions options = BatchContinuationOptions.OnlyOnSucceeded) where T : class, IJob;

    Task<Guid> ContinueBatchWith<T>(List<T> batchJobMessages, Guid parentId, BatchContinuationOptions options = BatchContinuationOptions.OnlyOnSucceeded) where T : class, IJob;
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

    public async Task<Guid> StartNew<T>(List<T> batchJobMessages, BatchContinuationOptions options = BatchContinuationOptions.OnlyOnSucceeded) where T : class, IJob
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Enqueued, null, options);
    }

    public async Task<Guid> ContinueBatchWith<T>(List<T> batchJobMessages, Guid parentId, BatchContinuationOptions options = BatchContinuationOptions.OnlyOnSucceeded) where T : class, IJob
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Awaiting, parentId, options);
    }

    private async Task<Guid> BaseCreateBatch<T>(List<T> batchJobMessages, Enums.State batchJobsState, Guid? parentId, BatchContinuationOptions options) where T : class, IJob
    {
        if (batchJobMessages.IsNullOrEmpty())
        {
            throw new Exception("List cannot be empty");
        }

        var placeholderJob = JobHelper.CreateJob(batchJobMessages[0], 0, null, null, _joblyConfiguration.DefaultQueue, parentId, State.Awaiting);

        var newBatch = new Batch
        {
            Id = placeholderJob.Id,
            Counter = batchJobMessages.Count,
            ContinuationOptions = options,
        };

        var batchJobs = batchJobMessages.Select(x => JobHelper.CreateJob(x, 0, null, null, _joblyConfiguration.DefaultQueue, null, batchJobsState))
            .ToList();

        // Propagate trace from execution context
        var executionContext = JobExecutionContext.Current;
        foreach (var batchJob in batchJobs)
        {
            if (executionContext != null)
            {
                batchJob.TraceId = executionContext.TraceId;
                batchJob.SpawnedByJobId = executionContext.JobId;
            }
            else
            {
                batchJob.TraceId = placeholderJob.Id; // Batch root trace
            }
        }

        // Placeholder job also gets the trace
        if (executionContext != null)
        {
            placeholderJob.TraceId = executionContext.TraceId;
            placeholderJob.SpawnedByJobId = executionContext.JobId;
        }
        else
        {
            placeholderJob.TraceId = placeholderJob.Id;
        }

        newBatch.Jobs = batchJobs;

        var logs = new List<JobLog>();
        foreach (var job in batchJobs)
        {
            logs.Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Created",
                Level = "Information",
                Timestamp = DateTime.UtcNow,
                Message = $"Job created in queue \"{job.Queue}\""
            });
        }
        logs.Add(new JobLog
        {
            JobId = placeholderJob.Id,
            EventType = "Created",
            Level = "Information",
            Timestamp = DateTime.UtcNow,
            Message = $"Batch placeholder job created in queue \"{placeholderJob.Queue}\""
        });

        _context.Set<Job>().AddRange(batchJobs);
        await _context.Set<Job>().AddAsync(placeholderJob);
        await _context.Set<JobLog>().AddRangeAsync(logs);
        await _context.Set<Batch>().AddAsync(newBatch);

        return newBatch.Id;
    }
}
