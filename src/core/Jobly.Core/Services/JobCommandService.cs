using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Interceptors;
using Jobly.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Services;

public interface IJobCommandService
{
    Task DeleteJob(Guid jobId);

    Task RequeueJob(Guid jobId);

    Task<BulkResultModel> BulkDeleteJobs(Guid[] jobIds);

    Task<BulkResultModel> BulkRequeueJobs(Guid[] jobIds);
}

public class JobCommandService<TContext> : IJobCommandService
    where TContext : DbContext
{
    private readonly TContext _context;

    public JobCommandService(TContext context)
    {
        _context = context;
    }

    public async Task DeleteJob(Guid jobId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var job = await _context.Set<Job>()
            .Where(x => x.Id == jobId)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .FirstOrDefaultAsync();

        if (job == null)
        {
            await transaction.RollbackAsync();
            throw new ArgumentException("Job not found.", nameof(jobId));
        }

        if (job.CurrentState == State.Deleted)
        {
            await transaction.RollbackAsync();
            return;
        }

        await DecrementStatForState(job.CurrentState);

        job.CurrentState = State.Deleted;
        job.ExpireAt = DateTime.UtcNow.AddDays(1);

        await _context.Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Value, p => p.Value + 1));

        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = job.Id,
            EventType = "Deleted",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = $"Job {job.Id} was deleted",
        });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task RequeueJob(Guid jobId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var job = await _context.Set<Job>()
            .Where(x => x.Id == jobId)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .FirstOrDefaultAsync();

        if (job == null)
        {
            await transaction.RollbackAsync();
            throw new ArgumentException("Job not found.", nameof(jobId));
        }

        if (job.CurrentState == State.Enqueued)
        {
            await transaction.RollbackAsync();
            return;
        }

        await DecrementStatForState(job.CurrentState);

        job.CurrentState = State.Enqueued;
        job.HandlerType = null;
        job.ExpireAt = null;

        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = job.Id,
            EventType = "Requeued",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = $"Job {job.Id} was requeued",
        });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<BulkResultModel> BulkDeleteJobs(Guid[] jobIds)
    {
        var result = new BulkResultModel();
        foreach (var jobId in jobIds)
        {
            try
            {
                await DeleteJob(jobId);
                result.Succeeded++;
            }
            catch
            {
                result.Skipped++;
            }
        }

        return result;
    }

    public async Task<BulkResultModel> BulkRequeueJobs(Guid[] jobIds)
    {
        var result = new BulkResultModel();
        foreach (var jobId in jobIds)
        {
            try
            {
                await RequeueJob(jobId);
                result.Succeeded++;
            }
            catch
            {
                result.Skipped++;
            }
        }

        return result;
    }

    private async Task DecrementStatForState(State state)
    {
        var key = state switch
        {
            State.Completed => "stats:succeeded",
            State.Failed => "stats:failed",
            State.Deleted => "stats:deleted",
            _ => null,
        };

        if (key != null)
        {
            await _context.Set<Statistic>()
                .Where(x => x.Key == key)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Value, p => p.Value - 1));
        }
    }
}
