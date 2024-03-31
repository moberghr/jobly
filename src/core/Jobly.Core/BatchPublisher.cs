using Jobly.Core.Data;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Jobly.Core;

public interface IBatchPublisher
{
    Task<string> StartNew<T>(List<T> batchJobMessages) where T : class;

    Task<string> ContinueBatchWith<T>(List<T> batchJobMessages, string parentId) where T : class;
}

public class BatchPublisher<TContext> : IBatchPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IJoblyNotifer? _notifier;

    public BatchPublisher(TContext context, IServiceProvider serviceProvider)
    {
        _context = context;
        _notifier = serviceProvider.GetService<IJoblyNotifer>();
    }

    public async Task<string> StartNew<T>(List<T> batchJobMessages) where T : class
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Enqueued,  Priority.Low, null);
    }

    public async Task<string> ContinueBatchWith<T>(List<T> batchJobMessages, string parentId) where T : class
    {
        return await BaseCreateBatch(batchJobMessages, Enums.State.Awaiting, Priority.Low, parentId);
    }

    private async Task<string> BaseCreateBatch<T>(List<T> batchJobMessages, Enums.State batchJobsState, Priority priority, string? parentId) where T : class
    {
        if (batchJobMessages.IsNullOrEmpty())
        {
            throw new Exception("List cannot be empty");
        }

        var placeholderJobForBatch = JobHelper.CreateJobAndJobState(batchJobMessages[0], 0, string.Empty, null,  priority: priority, null, parentId, Enums.State.Awaiting, null);

        var newBatch = new Batch
        {
            Id = placeholderJobForBatch.Job.Id,
            Counter = batchJobMessages.Count,
        };

        var batchStateJobs = batchJobMessages.Select(x => JobHelper.CreateJobAndJobState(x, 0, string.Empty, null,  priority, null, null, batchJobsState, newBatch.Id))
            .ToList();

        var batchJobs = batchStateJobs.Select(x => x.Job).ToList();

        newBatch.Jobs = batchJobs;
        
        await _context.Set<JobState>().AddRangeAsync(batchStateJobs);
        await _context.Set<JobState>().AddAsync(placeholderJobForBatch);
        await _context.Set<Batch>().AddAsync(newBatch);

        await Notify();

        return newBatch.Id;
    }
    
    private async Task Notify()
    {
        if (_notifier != null)
        {
            await _notifier!.NotifyAsync();
        }
    }
}
