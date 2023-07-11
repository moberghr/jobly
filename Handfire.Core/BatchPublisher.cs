using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
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
    private readonly IPublisher _publisher;

    public BatchPublisher(TContext context, IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task AddBatchAndBatchContinuationJobs<T>(List<T> batchJobMessages, List<T> batchContinuationJobMessages) where T : class
    {
        var createdTime = DateTime.UtcNow;

        var newBatchJobs = new List<Job>();
        var newBatchContinuationJobs = new List<Job>();

        foreach (var batchJobMessage in batchJobMessages)
        {
            var newJobState = await _publisher.CreateJobAndJobState<T>(batchJobMessage, name: string.Empty, scheduleTime: null, maxRetries: null, null);

            newBatchJobs.Add(newJobState.Job);
        }

        foreach (var batchContinuationJobMessage in batchContinuationJobMessages)
        {
            var newJobState = await _publisher.CreateJobAndJobState<T>(batchContinuationJobMessage, name: string.Empty, scheduleTime: null, maxRetries: null, null);

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
