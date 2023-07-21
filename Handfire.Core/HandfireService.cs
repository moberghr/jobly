using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public interface IHandfireService
{
    Task<int> GetPendingJobsCount();

    Task<int> GetTotalJobsCount();

    Task<int> GetScheduledJobsCount();

    Task<int> GetJobsCount(State state);

    Task<PagedList<JobModel>> GetJobsList(BaseListRequest request, State state);

    Task<PagedList<JobModel>> GetScheduledJobs(BaseListRequest request);

    Task<PagedList<JobStateModel>> GetJobStates(JobStateRequest request);

    Task SetRetry(string jobId);

    Task<PagedList<BatchModel>> GetBatchList(BaseListRequest request);
}

public class HandfireService<TContext> : IHandfireService
    where TContext : DbContext
{
    private readonly TContext _context;

    public HandfireService(TContext context)
    {
        _context = context;
    }

    public async Task<int> GetTotalJobsCount()
    {

        var counter = await _context.Set<Job>()
            .CountAsync();

        return counter;
    }

    public async Task<int> GetPendingJobsCount()
    {

        var counter = await GetPendingJobs()
            .CountAsync();

        return counter;
    }

    public async Task<int> GetScheduledJobsCount()
    {
        return await GetScheduledJobs()
            .CountAsync();
    }

    public async Task<int> GetJobsCount(State state)
    {
        return await GetJobsByState(state)
            .CountAsync();
    }

    public async Task<PagedList<JobModel>> GetJobsList(BaseListRequest request, State state)
    {
        return await GetJobsByState(state)
            .ToPagedListAsync(request);
    }

    public async Task<PagedList<JobModel>> GetScheduledJobs(BaseListRequest request)
    {
        var jobs = await GetScheduledJobs()
            .ToPagedListAsync(request);

        return jobs;
    }

    public async Task SetRetry(string jobId)
    {
        var job = _context.Set<Job>()
            .Where(x => x.Id == jobId)
            .Where(x => x.CurrentState == State.Failed)
            .FirstOrDefault();

        if (job == null)
        {
            throw new ArgumentException("Invalid job id.");
        }

        job.CurrentState = State.Enqueued;

        var jobState = new JobState
        {
            Job = job,
            DateTime = DateTime.UtcNow,
            State = State.Enqueued
        };

        _context.Set<Job>().Update(job);
        await _context.Set<JobState>().AddAsync(jobState);

        await _context.SaveChangesAsync();
    }

    public async Task<PagedList<JobStateModel>> GetJobStates(JobStateRequest request)
    {
        var history = await _context.Set<JobState>()
            .Where(x => x.JobId == request.JobId)
            .Select(x =>
                new JobStateModel
                {
                    Id = x.Id,
                    JobId = x.JobId,
                    DateTime = x.DateTime,
                    Message = x.Message,
                    State = x.State,
                })
            .ToPagedListAsync(request);

        return history;
    }

    public async Task<PagedList<BatchModel>> GetBatchList(BaseListRequest request)
    {
        var batches = await _context.Set<Batch>()
            .Select(x =>
                new BatchModel
                {
                    BatchId = x.Id,
                    NonFinished = $"{x.Counter}/{x.Jobs.Count}",
                })
            .ToPagedListAsync(request);

        return batches;
    }

    private IQueryable<JobModel> GetScheduledJobs()
    {
        var query = _context.Set<Job>()
            .Where(x => x.ScheduleTime > DateTime.UtcNow)
            .Select(x =>
                new JobModel
                {
                    Id = x.Id,
                    CurrentState = x.CurrentState,
                    CreateTime = x.CreateTime,
                    Message = x.Message,
                    ScheduleTime = x.ScheduleTime,
                    Type = x.Type
                })
            .AsQueryable();

        return query;
    }

    private IQueryable<JobModel> GetPendingJobs()
    {
        var query = _context.Set<Job>()
            .Where(x => x.ScheduleTime < DateTime.UtcNow)
            .Select(x =>
                new JobModel
                {
                    Id = x.Id,
                    CurrentState = x.CurrentState,
                    CreateTime = x.CreateTime,
                    Message = x.Message,
                    ScheduleTime = x.ScheduleTime,
                    Type = x.Type
                })
            .AsQueryable();

        return query;
    }

    private IQueryable<JobModel> GetJobsByState(State state)
    {
        var query = _context.Set<Job>()
            .Where(x => x.CurrentState == state)
            .Select(x =>
                new JobModel
                {
                    Id = x.Id,
                    CurrentState = x.CurrentState,
                    CreateTime = x.CreateTime,
                    Message = x.Message,
                    ScheduleTime = x.ScheduleTime,
                    Type = x.Type
                })
            .AsQueryable();

        return query;
    }
}
