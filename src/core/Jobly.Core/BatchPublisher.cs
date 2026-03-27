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

        var placeholderJobForBatch = JobHelper.CreateJobAndJobState(batchJobMessages[0], 0, null, null, _joblyConfiguration.DefaultQueue, parentId, State.Awaiting);

        var newBatch = new Batch
        {
            Id = placeholderJobForBatch.Job.Id,
            Counter = batchJobMessages.Count,
        };

        var batchStateJobs = batchJobMessages.Select(x => JobHelper.CreateJobAndJobState(x, 0, null, null, _joblyConfiguration.DefaultQueue, null, batchJobsState))
            .ToList();

        var batchJobs = batchStateJobs.Select(x => x.Job).ToList();

        newBatch.Jobs = batchJobs;

        await _context.Set<JobState>().AddRangeAsync(batchStateJobs);
        await _context.Set<JobState>().AddAsync(placeholderJobForBatch);
        await _context.Set<Batch>().AddAsync(newBatch);

        return newBatch.Id;
    }
}
