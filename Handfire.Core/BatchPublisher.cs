using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Handfire.Core;

public interface IBatchPublisher
{
    Task CreateBatchJobs<T>(List<T> firstBatchJobMessages, List<T> secondBatchJobMessages) where T : class;
}

public class BatchPublisher<TContext> : IBatchPublisher
    where TContext : DbContext
{
    private readonly TContext _context;

    public BatchPublisher(TContext context)
    {
        _context = context;
    }

    public async Task CreateBatchJobs<T>(List<T> firstBatchJobMessages, List<T> secondBatchJobMessages) where T : class
    {
        if (firstBatchJobMessages.IsNullOrEmpty() || secondBatchJobMessages.IsNullOrEmpty())
        {
            return;
        }

        var newFirstBatchJobs = new List<Job>();
        var newSecondBatchJobs = new List<Job>();

        var placeholderJobForFirstBatch = JobHelper.CreateJobAndJobState(firstBatchJobMessages[0], 0, string.Empty, null, null, null, Enums.State.Awaiting, null);

        var newFirstBatch = new Batch
        {
            BatchStatus = Enums.State.Enqueued,
            JobId = placeholderJobForFirstBatch.Job.Id,
            Counter = firstBatchJobMessages.Count,
        };

        foreach (var batchJobMessage in firstBatchJobMessages)
        {
            var newJobState = JobHelper.CreateJobAndJobState(batchJobMessage, 0, string.Empty, null, null, null, Enums.State.Enqueued, newFirstBatch.Id);

            await _context.Set<JobState>().AddAsync(newJobState);

            newFirstBatchJobs.Add(newJobState.Job);
        }

        newFirstBatch.Jobs = newFirstBatchJobs;

        var placeholderJobForSecondBatch = JobHelper.CreateJobAndJobState(secondBatchJobMessages[0], 0, string.Empty, null, null, newFirstBatch.JobId, Enums.State.Awaiting, null);

        var newSecondBatch = new Batch
        {
            BatchStatus = Enums.State.Awaiting,
            JobId = placeholderJobForSecondBatch.Job.Id,
            Counter = secondBatchJobMessages.Count,
        };

        foreach (var batchJobMessage in secondBatchJobMessages)
        {
            var newJobState = JobHelper.CreateJobAndJobState(batchJobMessage, 0, string.Empty, null, null, null, Enums.State.Awaiting, newSecondBatch.Id);

            await _context.Set<JobState>().AddAsync(newJobState);

            newSecondBatchJobs.Add(newJobState.Job);
        }

        newSecondBatch.Jobs = newSecondBatchJobs;

        await _context.Set<JobState>().AddAsync(placeholderJobForFirstBatch);
        await _context.Set<JobState>().AddAsync(placeholderJobForSecondBatch);
        await _context.Set<Batch>().AddAsync(newFirstBatch);
        await _context.Set<Batch>().AddAsync(newSecondBatch);

        await _context.SaveChangesAsync();
    }
}
