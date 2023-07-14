using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Helper;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public interface IBatchPublisher
{
    Task AddBatchAndBatchContinuationJobs<T>(List<T> batchJobs, List<T> batchContinationJobs) where T : class;
}

public class BatchPublisher<TContext> : IBatchPublisher
    where TContext : DbContext
{
    private readonly TContext _context;

    public BatchPublisher(TContext context)
    {
        _context = context;
    }

    public async Task AddBatchAndBatchContinuationJobs<T>(List<T> batchJobMessages, List<T> batchContinuationJobMessages) where T : class
    {
        var createdTime = DateTime.UtcNow;

        var newBatchJobs = new List<Job>();
        var newBatchContinuationJobs = new List<Job>();

        foreach (var batchJobMessage in batchJobMessages)
        {
            var newJobState = JobHelper.CreateJobAndJobState(batchJobMessage, 0, string.Empty, null, null, null, Enums.State.Enqueued);

            await _context.Set<JobState>().AddAsync(newJobState);

            newBatchJobs.Add(newJobState.Job);
        }

        foreach (var batchContinuationJobMessage in batchContinuationJobMessages)
        {
            var newJobState = JobHelper.CreateJobAndJobState(batchContinuationJobMessage, 0, string.Empty, null, null, null, Enums.State.Awaiting);

            await _context.Set<JobState>().AddAsync(newJobState);

            newBatchContinuationJobs.Add(newJobState.Job);
        }

        var newBatchContinuations = newBatchContinuationJobs.Select(x =>
            new BatchContinuation
            {
                Job = x,
            })
            .ToList();

        var newBatch = new Batch
        {
            BatchStatus = Enums.State.Enqueued,
            Counter = newBatchJobs.Count,
            Jobs = newBatchJobs,
            BatchContinuations = newBatchContinuations,
        };

        await _context.Set<Batch>().AddAsync(newBatch);

        await _context.SaveChangesAsync();
    }
}
