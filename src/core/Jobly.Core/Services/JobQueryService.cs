using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Services;

public interface IJobQueryService
{
    Task<PagedList<JobModel>> GetJobsList(BaseListRequest request, State state);

    Task<PagedList<JobModel>> GetScheduledJobs(BaseListRequest request);

    Task<PagedList<JobModel>> GetJobStatesInProcess(BaseListRequest request);

    Task<PagedList<JobModel>> GetAwaitingJobs(BaseListRequest request);

    Task<JobDetailModel?> GetJobById(Guid jobId);

    Task<PagedList<JobModel>> GetSiblingJobs(Guid jobId, BaseListRequest request);

    Task<PagedList<JobModel>> GetChildJobs(Guid jobId, BaseListRequest request);

    Task<PagedList<JobModel>> GetTraceJobs(Guid jobId, BaseListRequest request);

    Task<int> CountProcessingJobs();
}

public class JobQueryService<TContext> : IJobQueryService
    where TContext : DbContext
{
    private readonly TContext _context;

    public JobQueryService(TContext context)
    {
        _context = context;
    }

    public async Task<PagedList<JobModel>> GetJobsList(BaseListRequest request, State state)
    {
        return await GetJobsByState(state)
            .ToPagedListAsync(request);
    }

    public async Task<PagedList<JobModel>> GetScheduledJobs(BaseListRequest request)
    {
        var jobs = await GetScheduledJobsQuery()
            .ToPagedListAsync(request);

        return jobs;
    }

    public async Task<int> CountProcessingJobs()
    {
        return await Jobs()
            .Where(x => x.CurrentState == State.Processing)
            .CountAsync();
    }

    public async Task<PagedList<JobModel>> GetJobStatesInProcess(BaseListRequest request)
    {
        var jobs = await Jobs()
            .Where(x => x.CurrentState == State.Processing)
            .Select(x => new JobModel
            {
                Id = x.Id,
                CurrentState = x.CurrentState,
                CreateTime = x.CreateTime,
                Message = x.Message,
                ScheduleTime = x.ScheduleTime,
                Type = x.Type,
            })
            .AsQueryable().ToPagedListAsync(request);
        return jobs;
    }

    public async Task<PagedList<JobModel>> GetAwaitingJobs(BaseListRequest request)
    {
        return await GetJobsByState(State.Awaiting).ToPagedListAsync(request);
    }

    public async Task<JobDetailModel?> GetJobById(Guid jobId)
    {
        var job = await _context.Set<Job>()
            .Where(x => x.Id == jobId)
            .Select(x => new JobDetailModel
            {
                Id = x.Id,
                Type = x.Type,
                Message = x.Message,
                CreateTime = x.CreateTime,
                ScheduleTime = x.ScheduleTime,
                CurrentState = x.CurrentState,
                HandlerType = x.HandlerType,
                MessageId = x.MessageId,
                ParentJobId = x.ParentJobId,
                BatchId = x.BatchId,
                RetriedTimes = x.RetriedTimes,
                MaxRetries = x.MaxRetries,
                TraceId = x.TraceId,
                SpawnedByJobId = x.SpawnedByJobId,
            })
            .FirstOrDefaultAsync();

        if (job == null)
        {
            return null;
        }

        job.Logs = await _context.Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .Select(x => new JobLogModel
            {
                Id = x.Id,
                EventType = x.EventType,
                Timestamp = x.Timestamp,
                Level = x.Level,
                Message = x.Message,
                Exception = x.Exception,
            })
            .ToListAsync();

        // Counts for related jobs
        if (job.MessageId != null)
        {
            job.SiblingJobCount = await _context.Set<Job>()
                .Where(x => x.MessageId == job.MessageId && x.Id != jobId)
                .CountAsync();
        }

        job.ChildJobCount = await _context.Set<Job>()
            .Where(x => x.ParentJobId == jobId)
            .CountAsync();

        if (job.TraceId != null)
        {
            job.TraceJobCount = await _context.Set<Job>()
                .Where(x => x.TraceId == job.TraceId && x.Id != jobId)
                .CountAsync();
        }

        return job;
    }

    public async Task<PagedList<JobModel>> GetSiblingJobs(Guid jobId, BaseListRequest request)
    {
        var messageId = await _context.Set<Job>()
            .Where(x => x.Id == jobId)
            .Select(x => x.MessageId)
            .FirstOrDefaultAsync();

        if (messageId == null)
        {
            return new PagedList<JobModel>(0, [], 0);
        }

        return await _context.Set<Job>()
            .Where(x => x.MessageId == messageId && x.Id != jobId)
            .OrderByDescending(x => x.CreateTime)
            .Select(x => new JobModel
            {
                Id = x.Id,
                Type = x.Type,
                Message = x.Message,
                CreateTime = x.CreateTime,
                ScheduleTime = x.ScheduleTime,
                CurrentState = x.CurrentState,
            })
            .ToPagedListAsync(request);
    }

    public async Task<PagedList<JobModel>> GetChildJobs(Guid jobId, BaseListRequest request)
    {
        return await _context.Set<Job>()
            .Where(x => x.ParentJobId == jobId)
            .OrderByDescending(x => x.CreateTime)
            .Select(x => new JobModel
            {
                Id = x.Id,
                Type = x.Type,
                Message = x.Message,
                CreateTime = x.CreateTime,
                ScheduleTime = x.ScheduleTime,
                CurrentState = x.CurrentState,
            })
            .ToPagedListAsync(request);
    }

    public async Task<PagedList<JobModel>> GetTraceJobs(Guid jobId, BaseListRequest request)
    {
        var traceId = await _context.Set<Job>()
            .Where(x => x.Id == jobId)
            .Select(x => x.TraceId)
            .FirstOrDefaultAsync();

        if (traceId == null)
        {
            return new PagedList<JobModel>(0, [], 0);
        }

        return await _context.Set<Job>()
            .Where(x => x.TraceId == traceId && x.Id != jobId)
            .OrderBy(x => x.CreateTime)
            .Select(x => new JobModel
            {
                Id = x.Id,
                Type = x.Type,
                Message = x.Message,
                CreateTime = x.CreateTime,
                ScheduleTime = x.ScheduleTime,
                CurrentState = x.CurrentState,
            })
            .ToPagedListAsync(request);
    }

    /// <summary>
    /// Base query that excludes batch placeholder jobs from results.
    /// </summary>
    private IQueryable<Job> Jobs()
    {
        var batchIds = _context.Set<Batch>().Select(b => b.Id);
        return _context.Set<Job>().Where(j => !batchIds.Contains(j.Id));
    }

    private IQueryable<JobModel> GetScheduledJobsQuery()
    {
        var query = Jobs()
            .Where(x => x.ScheduleTime > DateTime.UtcNow)
            .Select(x =>
                new JobModel
                {
                    Id = x.Id,
                    CurrentState = x.CurrentState,
                    CreateTime = x.CreateTime,
                    Message = x.Message,
                    ScheduleTime = x.ScheduleTime,
                    Type = x.Type,
                })
            .AsQueryable();

        return query;
    }

    private IQueryable<JobModel> GetJobsByState(State state)
    {
        var jobs = Jobs()
            .Where(x => x.CurrentState == state);

        // Enqueued: exclude future-scheduled jobs (those show under Scheduled)
        if (state == State.Enqueued)
        {
            jobs = jobs.Where(x => x.ScheduleTime <= DateTime.UtcNow);
        }

        return jobs
            .Select(x =>
                new JobModel
                {
                    Id = x.Id,
                    CurrentState = x.CurrentState,
                    CreateTime = x.CreateTime,
                    Message = x.Message,
                    ScheduleTime = x.ScheduleTime,
                    Type = x.Type,
                })
            .AsQueryable();
    }
}
