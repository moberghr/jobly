using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public interface IHandfireService
{
    Task<int> GetPendingJobs();

    Task<int> GetTotalJobs();

    Task<int> GetScheduledJobs();

    Task<int> GetCreatedCount();

    Task<int> GetCompletedCount();

    Task<int> GetFailedCount();

    Task<PagedList<JobModel>> GetCreatedJobs(BaseListRequest request);

    Task<PagedList<JobModel>> GetCompetedJobs(BaseListRequest request);

    Task<PagedList<JobModel>> GetFailedJobs(BaseListRequest request);

    Task<PagedList<JobModel>> GetScheduledJobs(BaseListRequest request);

    Task<PagedList<JobStateModel>> GetJobStates(JobStateRequest request);
}

public class HandfireService<TContext> : IHandfireService
    where TContext : DbContext
{
    private readonly TContext _context;

    public HandfireService(TContext context)
    {
        _context = context;
    }

    public async Task<int> GetTotalJobs()
    {

        var counter = await _context.Set<Job>()
            .CountAsync();

        return counter;
    }

    public async Task<int> GetPendingJobs()
    {

        var counter = await _context.Set<Job>()
            .Where(x => x.ProcessedTime == null)
            .CountAsync();

        return counter;
    }

    public async Task<int> GetScheduledJobs()
    {

        var counter = await _context.Set<Job>()
            .Where(x => x.ProcessedTime == null)
            .Where(x => x.ScheduleTime != null)
            .CountAsync();

        return counter;
    }

    public async Task<int> GetCreatedCount()
    {
        var counter = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Created)
            .CountAsync();

        return counter;
    }

    public async Task<int> GetCompletedCount()
    {
        var counter = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Completed)
            .CountAsync();

        return counter;
    }

    public async Task<int> GetFailedCount()
    {
        var counter = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Failed)
            .CountAsync();

        return counter;
    }

    public async Task<PagedList<JobModel>> GetCreatedJobs(BaseListRequest request)
    {
        return await GetJobsByState(request, State.Created);
    }

    public async Task<PagedList<JobModel>> GetCompetedJobs(BaseListRequest request)
    {
        return await GetJobsByState(request, State.Completed);
    }

    public async Task<PagedList<JobModel>> GetFailedJobs(BaseListRequest request)
    {
        return await GetJobsByState(request, State.Failed);
    }

    public async Task<PagedList<JobModel>> GetScheduledJobs(BaseListRequest request)
    {
        var jobs = await _context.Set<Job>()
            .Where(x => x.ProcessedTime == null)
            .Where(x => x.ScheduleTime > DateTime.UtcNow)
            .Select(x =>
                new JobModel
                {
                    Id = x.Id,
                    CurrentState = x.CurrentState,
                    CreateTime = x.CreateTime,
                    Message = x.Message,
                    ProcessedTime = x.ProcessedTime,
                    ScheduleTime = x.ScheduleTime,
                    Type = x.Type
                })
            .ToPagedListAsync(request);

        return jobs;
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

    private async Task<PagedList<JobModel>> GetJobsByState(BaseListRequest request, State state)
    {
        var jobs = await _context.Set<Job>()
            .Where(x => x.CurrentState == state)
            .Select(x =>
                new JobModel
                {
                    Id = x.Id,
                    CurrentState = x.CurrentState,
                    CreateTime = x.CreateTime,
                    Message = x.Message,
                    ProcessedTime = x.ProcessedTime,
                    ScheduleTime = x.ScheduleTime,
                    Type = x.Type
                })
            .ToPagedListAsync(request);

        return jobs;
    }
}
