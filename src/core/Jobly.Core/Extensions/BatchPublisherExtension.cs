using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Jobly.Core;

public static class BatchPublisherExtension
{
    public static async Task<Guid> StartNew<T>(this DbContext context, List<T> batchJobMessages) where T : class
    {
        return await context.BaseCreateBatch(batchJobMessages, State.Enqueued, null);
    }
    
    public static async Task<Guid> ContinueBatchWith<T>(this DbContext context, List<T> batchJobMessages, Guid parentId) where T : class
    {
        return await context.BaseCreateBatch(batchJobMessages, Enums.State.Awaiting, parentId);
    }
    
    private static async Task<Guid> BaseCreateBatch<T>(this DbContext context, List<T> batchJobMessages, Enums.State batchJobsState, Guid? parentId) where T : class
    {
        if (batchJobMessages.IsNullOrEmpty())
        {
            throw new Exception("List cannot be empty");
        }

        var placeholderJobForBatch = JobHelper.CreateJobAndJobState(batchJobMessages[0], 0, string.Empty, null, null, Priority.Low, parentId, State.Awaiting);

        var newBatch = new Batch
        {
            Id = placeholderJobForBatch.Job.Id,
            Counter = batchJobMessages.Count,
        };

        var batchStateJobs = batchJobMessages.Select(x => JobHelper.CreateJobAndJobState(x, 0, string.Empty, null, null, Priority.Low, null, batchJobsState))
            .ToList();

        var batchJobs = batchStateJobs.Select(x => x.Job).ToList();

        newBatch.Jobs = batchJobs;

        await context.Set<JobState>().AddRangeAsync(batchStateJobs);
        await context.Set<JobState>().AddAsync(placeholderJobForBatch);
        await context.Set<Batch>().AddAsync(newBatch);

        return newBatch.Id;
    }
}