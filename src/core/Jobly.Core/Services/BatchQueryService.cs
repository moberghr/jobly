using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Services;

public interface IBatchQueryService
{
    Task<PagedList<BatchModel>> GetBatches(BaseListRequest request);

    Task<BatchDetailModel?> GetBatchById(Guid batchId);

    Task<PagedList<JobModel>> GetBatchJobs(Guid batchId, BaseListRequest request);
}

public class BatchQueryService<TContext> : IBatchQueryService
    where TContext : DbContext
{
    private readonly TContext _context;

    public BatchQueryService(TContext context)
    {
        _context = context;
    }

    public async Task<PagedList<BatchModel>> GetBatches(BaseListRequest request)
    {
        return await _context.Set<Batch>()
            .Join(_context.Set<Job>(), b => b.Id, j => j.Id, (b, j) => new { Batch = b, PlaceholderJob = j })
            .OrderByDescending(x => x.PlaceholderJob.CreateTime)
            .Select(x => new BatchModel
            {
                Id = x.Batch.Id,
                TotalJobs = _context.Set<Job>().Count(j => j.BatchId == x.Batch.Id),
                RemainingJobs = x.Batch.Counter,
                PlaceholderState = x.PlaceholderJob.CurrentState,
                CreateTime = x.PlaceholderJob.CreateTime,
            })
            .ToPagedListAsync(request);
    }

    public async Task<BatchDetailModel?> GetBatchById(Guid batchId)
    {
        var batch = await _context.Set<Batch>()
            .Where(b => b.Id == batchId)
            .FirstOrDefaultAsync();

        if (batch == null)
        {
            return null;
        }

        var placeholderJob = await _context.Set<Job>()
            .Where(j => j.Id == batchId)
            .FirstOrDefaultAsync();

        if (placeholderJob == null)
        {
            return null;
        }

        var totalJobs = await _context.Set<Job>()
            .Where(j => j.BatchId == batchId)
            .CountAsync();

        var continuationJob = await _context.Set<Job>()
            .Where(j => j.ParentJobId == batchId)
            .Select(j => j.Id)
            .FirstOrDefaultAsync();

        return new BatchDetailModel
        {
            Id = batch.Id,
            TotalJobs = totalJobs,
            RemainingJobs = batch.Counter,
            PlaceholderState = placeholderJob.CurrentState,
            CreateTime = placeholderJob.CreateTime,
            ContinuationJobId = continuationJob == Guid.Empty ? null : continuationJob,
        };
    }

    public async Task<PagedList<JobModel>> GetBatchJobs(Guid batchId, BaseListRequest request)
    {
        return await _context.Set<Job>()
            .Where(j => j.BatchId == batchId)
            .OrderByDescending(j => j.CreateTime)
            .Select(j => new JobModel
            {
                Id = j.Id,
                Type = j.Type,
                Message = j.Message,
                CreateTime = j.CreateTime,
                ScheduleTime = j.ScheduleTime,
                CurrentState = j.CurrentState,
            })
            .ToPagedListAsync(request);
    }
}
