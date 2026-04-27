using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Warp.Core.Data.Entities;
using Warp.Core.Data.Queries;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Models;
using Warp.Core.Notifications;

namespace Warp.Core.Services;

public interface IJobCommandService
{
    Task DeleteJob(Guid jobId);

    Task RequeueJob(Guid jobId);

    Task<BulkResultModel> BulkDeleteJobs(Guid[] jobIds);

    Task<BulkResultModel> BulkRequeueJobs(Guid[] jobIds);

    Task<BulkResultModel> DeleteFailedJobsByType(string type);

    Task<BulkResultModel> RequeueFailedJobsByType(string type);
}

public class JobCommandService<TContext> : IJobCommandService
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly WarpConfiguration _configuration;
    private readonly IWarpNotificationTransport _notificationTransport;
    private readonly IWarpSqlQueries<TContext> _sqlQueries;

    public JobCommandService(TContext context, TimeProvider timeProvider, IOptions<WarpConfiguration> configuration, IWarpNotificationTransport notificationTransport, IWarpSqlQueries<TContext> sqlQueries)
    {
        _context = context;
        _timeProvider = timeProvider;
        _configuration = configuration.Value;
        _notificationTransport = notificationTransport;
        _sqlQueries = sqlQueries;
    }

    public async Task DeleteJob(Guid jobId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var job = await _sqlQueries.LockJobByIdAsync(_context, jobId, default);

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

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Processing jobs: signal graceful cancellation instead of immediate state change.
        // The worker will detect this via RunJobMonitor and set the final state.
        if (job.CurrentState == State.Processing)
        {
            job.CancellationMode = CancellationMode.Graceful;

            await _context.Set<JobLog>().AddAsync(new JobLog
            {
                JobId = job.Id,
                EventType = "CancellationRequested",
                Timestamp = now,
                Level = "Information",
                Message = $"Graceful cancellation requested for job {job.Id}",
            });
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return;
        }

        DecrementStatForState(job.CurrentState);

        job.CurrentState = State.Deleted;
        job.ExpireAt = now.Add(_configuration.JobExpirationTimeout);

        _context.Set<Counter>().Add(new Counter { Key = "stats:deleted", Value = 1 });

        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = job.Id,
            EventType = "Deleted",
            Timestamp = now,
            Level = "Information",
            Message = $"Job {job.Id} was deleted",
        });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task RequeueJob(Guid jobId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var job = await _sqlQueries.LockJobByIdAsync(_context, jobId, default);

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

        // Can't requeue a Processing job — worker is still executing it.
        // Use DeleteJob to cancel it instead.
        if (job.CurrentState == State.Processing)
        {
            await transaction.RollbackAsync();
            return;
        }

        DecrementStatForState(job.CurrentState);

        job.CurrentState = State.Enqueued;
        job.ScheduleTime = _timeProvider.GetUtcNow().UtcDateTime;
        job.ExpireAt = null;

        // Restore parent counters so they wait for this job again
        Job? parent = null;
        if (job.ParentJobId != null)
        {
            parent = await _sqlQueries.LockJobByIdWaitAsync(_context, job.ParentJobId.Value, default);
            if (parent != null)
            {
                parent.JobCount++;
                if (parent.CurrentState == State.Completed || parent.CurrentState == State.Failed)
                {
                    parent.CurrentState = parent.Kind == JobKind.Batch ? State.Awaiting : State.Processing;
                    parent.ExpireAt = null;
                }
            }
        }

        // Only clear HandlerType for direct jobs — message-spawned jobs need it to re-execute the correct handler
        if (parent == null || parent.Kind != JobKind.Message)
        {
            job.HandlerType = null;
        }

        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = job.Id,
            EventType = "Requeued",
            Timestamp = _timeProvider.GetUtcNow().UtcDateTime,
            Level = "Information",
            Message = $"Job {job.Id} was requeued",
        });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        // Requeue lands the row in Enqueued with ScheduleTime=now — wake dispatcher immediately.
        var queue = string.IsNullOrEmpty(job.Queue) ? "default" : job.Queue;
        await NotificationDispatch.FireAsync(
            _notificationTransport,
            [new Notification(NotificationKind.JobEnqueued, queue)]);
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

    public async Task<BulkResultModel> DeleteFailedJobsByType(string type)
    {
        var result = new BulkResultModel();
        while (true)
        {
            var ids = await _context.Set<Job>()
                .Where(x => x.Kind == JobKind.Job && x.CurrentState == State.Failed && x.Type == type)
                .Select(x => x.Id)
                .Take(1000)
                .ToListAsync();

            if (ids.Count == 0)
            {
                break;
            }

            var batchResult = await BulkDeleteJobs([.. ids]);
            result.Succeeded += batchResult.Succeeded;
            result.Skipped += batchResult.Skipped;
        }

        return result;
    }

    public async Task<BulkResultModel> RequeueFailedJobsByType(string type)
    {
        var result = new BulkResultModel();
        while (true)
        {
            var ids = await _context.Set<Job>()
                .Where(x => x.Kind == JobKind.Job && x.CurrentState == State.Failed && x.Type == type)
                .Select(x => x.Id)
                .Take(1000)
                .ToListAsync();

            if (ids.Count == 0)
            {
                break;
            }

            var batchResult = await BulkRequeueJobs([.. ids]);
            result.Succeeded += batchResult.Succeeded;
            result.Skipped += batchResult.Skipped;
        }

        return result;
    }

    private void DecrementStatForState(State state)
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
            _context.Set<Counter>().Add(new Counter { Key = key, Value = -1 });
        }
    }
}
