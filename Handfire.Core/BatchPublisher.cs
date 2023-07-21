using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Handfire.Core;

public interface IBatchPublisher
{
    Task<string> StartNew<T>(List<T> batchJobMessages) where T : class;

    Task<string> ContinueBatchWith<T>(List<T> batchJobMessages, string placeholderJobId) where T : class;
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

    public async Task<string> ContinueBatchWith<T>(List<T> batchJobMessages, string placeholderJobId) where T : class
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Awaiting, placeholderJobId);
    }

    private async Task<string> BaseCreateBatch<T>(List<T> batchJobMessages, Enums.State batchJobsState, string? placeholderJobId) where T : class
    {
        if (batchJobMessages.IsNullOrEmpty())
        {
            throw new Exception("List cannot be empty");
        }

        var placeholderJobForBatch = JobHelper.CreateJobAndJobState(batchJobMessages[0], 0, string.Empty, null, null, placeholderJobId, Enums.State.Awaiting, null);

        var newBatch = new Batch
        {
            Id = placeholderJobForBatch.Job.Id,
            Counter = batchJobMessages.Count,
        };

        var batchJobs = batchJobMessages.Select(batchJobMessage =>
        {
            var newJobState = JobHelper.CreateJobAndJobState(batchJobMessage, 0, string.Empty, null, null, null, batchJobsState, newBatch.Id);
            _context.Set<JobState>().AddAsync(newJobState);
            return newJobState.Job;
        }).ToList();

        newBatch.Jobs = batchJobs;

        await _context.Set<JobState>().AddAsync(placeholderJobForBatch);
        await _context.Set<Batch>().AddAsync(newBatch);

        return newBatch.Id;
    }
}
