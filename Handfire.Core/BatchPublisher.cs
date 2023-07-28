using Handfire.Core.Entities;
using Handfire.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Handfire.Core;

public interface IBatchPublisher
{
    Task<string> StartNew<T>(List<T> batchJobMessages) where T : class;

    Task<string> ContinueBatchWith<T>(List<T> batchJobMessages, string parentId) where T : class;
}

public class BatchPublisher<TContext> : IBatchPublisher
    where TContext : DbContext
{
    private readonly TContext _context;

    public BatchPublisher(TContext context)
    {
        _context = context;
    }

    public async Task<string> StartNew<T>(List<T> batchJobMessages) where T : class
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Enqueued, null);
    }

    public async Task<string> ContinueBatchWith<T>(List<T> batchJobMessages, string parentId) where T : class
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Awaiting, parentId);
    }

    private async Task<string> BaseCreateBatch<T>(List<T> batchJobMessages, Enums.State batchJobsState, string? parentId) where T : class
    {
        if (batchJobMessages.IsNullOrEmpty())
        {
            throw new Exception("List cannot be empty");
        }

        var placeholderJobForBatch = JobHelper.CreateJobAndJobState(batchJobMessages[0], 0, string.Empty, null, null, parentId, Enums.State.Awaiting, null);

        var newBatch = new Batch
        {
            Id = placeholderJobForBatch.Job.Id,
            Counter = batchJobMessages.Count,
        };

        var batchStateJobs = batchJobMessages.Select(x => JobHelper.CreateJobAndJobState(x, 0, string.Empty, null, null, null, batchJobsState, newBatch.Id))
            .ToList();

        var batchJobs = batchStateJobs.Select(x => x.Job).ToList();

        newBatch.Jobs = batchJobs;

        await _context.Set<JobState>().AddRangeAsync(batchStateJobs);
        await _context.Set<JobState>().AddAsync(placeholderJobForBatch);
        await _context.Set<Batch>().AddAsync(newBatch);

        return newBatch.Id;
    }
}
