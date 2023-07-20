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
        if (batchJobMessages.IsNullOrEmpty())
        {
            throw new Exception("List cannot be empty");
        }

        var batchJobs = new List<Job>();

        var placeholderJobForBatch = JobHelper.CreateJobAndJobState(batchJobMessages[0], 0, string.Empty, null, null, null, Enums.State.Awaiting, null);

        var newBatch = new Batch
        {
            BatchStatus = Enums.State.Enqueued,
            JobId = placeholderJobForBatch.Job.Id,
            Counter = batchJobMessages.Count,
        };

        foreach (var batchJobMessage in batchJobMessages)
        {
            var newJobState = JobHelper.CreateJobAndJobState(batchJobMessage, 0, string.Empty, null, null, null, Enums.State.Enqueued, newBatch.Id);

            await _context.Set<JobState>().AddAsync(newJobState);

            batchJobs.Add(newJobState.Job);
        }

        newBatch.Jobs = batchJobs;

        await _context.Set<JobState>().AddAsync(placeholderJobForBatch);
        await _context.Set<Batch>().AddAsync(newBatch);

        return newBatch.JobId;
    }

    public async Task<string> ContinueBatchWith<T>(List<T> batchJobMessages, string placeholderJobId) where T : class
    {
        if (batchJobMessages.IsNullOrEmpty())
        {
            throw new Exception("List cannot be empty");
        }

        var batchJobs = new List<Job>();

        var placeholderJobForBatch = JobHelper.CreateJobAndJobState(batchJobMessages[0], 0, string.Empty, null, null, placeholderJobId, Enums.State.Awaiting, null);

        var newBatch = new Batch
        {
            BatchStatus = Enums.State.Awaiting,
            JobId = placeholderJobForBatch.Job.Id,
            Counter = batchJobMessages.Count,
        };

        foreach (var batchJobMessage in batchJobMessages)
        {
            var newJobState = JobHelper.CreateJobAndJobState(batchJobMessage, 0, string.Empty, null, null, null, Enums.State.Awaiting, newBatch.Id);

            await _context.Set<JobState>().AddAsync(newJobState);

            batchJobs.Add(newJobState.Job);
        }

        newBatch.Jobs = batchJobs;

        await _context.Set<JobState>().AddAsync(placeholderJobForBatch);
        await _context.Set<Batch>().AddAsync(newBatch);

        return newBatch.JobId;
    }
}
