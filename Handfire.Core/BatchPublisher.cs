using System.Text.Json;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public interface IBatchPublisher
{
    Task AddBatchAndBatchContinationJobs<T>(List<T> batchJobs, List<T> batchContinationJobs) where T : class;

    Task UpdateBatch(Batch batch);
}

public class BatchPublisher<TContext> : IBatchPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly int _retries;

    public BatchPublisher(TContext context, int retries)
    {
        _context = context;
        _retries = retries;
    }

    public async Task AddBatchAndBatchContinationJobs<T>(List<T> batchJobs, List<T> batchContinationJobs) where T : class
    {
        var createdTime = DateTime.UtcNow;

        var newBatchJobs = batchJobs.Select(
            x => new Job
            {
                Id = Guid.NewGuid().ToString(),
                CreateTime = createdTime,
                Message = JsonSerializer.Serialize(x),
                Type = x.GetType().AssemblyQualifiedName!,
                CurrentState = Enums.State.Enqueued,
                MaxRetries = _retries,
            })
            .ToList();

        var newBatchContinations = batchContinationJobs.Select(
            x => new BatchContinuation
            {
                Job = new Job
                {
                    Id = Guid.NewGuid().ToString(),
                    CreateTime = createdTime,
                    Message = JsonSerializer.Serialize(x),
                    Type = x.GetType().AssemblyQualifiedName!,
                    CurrentState = Enums.State.Awaiting,
                    MaxRetries = _retries,
                },
            })
            .ToList();

        var newBatch = new Batch
        {
            BatchStatus = Enums.State.Enqueued,
            Counter = newBatchJobs.Count,
            Jobs = newBatchJobs,
            BatchContinuations = newBatchContinations,
        };

        await _context.Set<Batch>().AddAsync(newBatch);

        await _context.SaveChangesAsync();
    }

    public async Task UpdateBatch(Batch batch)
    {
        batch.Counter = batch.Jobs.Where(x => x.CurrentState != Enums.State.Completed).Count();

        if (batch.Counter == 0)
        {
            foreach (var batchContination in batch.BatchContinuations)
            {
                batchContination.Job.CurrentState = Enums.State.Enqueued;
            }
        }

        await _context.SaveChangesAsync();
    }
}
