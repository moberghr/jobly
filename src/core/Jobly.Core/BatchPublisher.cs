using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Jobly.Core;

public interface IBatchPublisher
{
    Task<Guid> StartNew<T>(List<T> batchJobMessages) where T : class, IJob;

    Task<Guid> ContinueBatchWith<T>(List<T> batchJobMessages, Guid parentId) where T : class, IJob;
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

    public async Task<Guid> StartNew<T>(List<T> batchJobMessages) where T : class, IJob
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Enqueued, null);
    }

    public async Task<Guid> ContinueBatchWith<T>(List<T> batchJobMessages, Guid parentId) where T : class, IJob
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Awaiting, parentId);
    }

    private async Task<Guid> BaseCreateBatch<T>(List<T> batchJobMessages, Enums.State batchJobsState, Guid? parentId) where T : class, IJob
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
        };

        var batchJobs = batchJobMessages.Select(x => JobHelper.CreateJob(x, 0, null, null, _joblyConfiguration.DefaultQueue, null, batchJobsState))
            .ToList();

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
