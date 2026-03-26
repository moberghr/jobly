using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public interface IJoblyService
{
    Task<int> GetPendingJobsCount();

    Task<int> GetTotalJobsCount();

    Task<int> GetScheduledJobsCount();

    Task<int> GetJobsCount(State state);

    Task<DashboardStatistics> GetJoblyStatus();

    Task<PagedList<JobModel>> GetJobsList(BaseListRequest request, State state);

    Task<PagedList<JobModel>> GetScheduledJobs(BaseListRequest request);

    Task<PagedList<JobStateModel>> GetJobStates(JobStateRequest request);

    Task<PagedList<JobModel>> GetJobStatesInProcess(BaseListRequest request);

    Task<int> CountProcessingJobs();

    Task SetRetry(Guid jobId);

    Task<List<ServerModel>> GetServers();

    Task<int> GetServerCount();
}

public class JoblyService<TContext> : IJoblyService
    where TContext : DbContext
{
    private readonly TContext _context;

    public JoblyService(TContext context)
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

    public async Task<DashboardStatistics> GetJoblyStatus()
    {
        var total = await GetTotalJobsCount();
        var pending = await GetPendingJobsCount();
        var scheduled = await GetScheduledJobsCount();
        var created = await GetJobsCount(State.Enqueued);
        var completed = await GetJobsCount(State.Completed);
        var failed = await GetJobsCount(State.Failed);
        var processing = await CountProcessingJobs() - completed - failed;

        var servers = await GetServerCount();

        var model = new DashboardStatistics
        {
            Total = total,
            Pending = pending,
            Scheduled = scheduled,
            Created = created,
            Completed = completed,
            Failed = failed,
            Processing = processing,
            Servers = servers
        };

        return model;
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

    public async Task SetRetry(Guid jobId)
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

    public async Task<int> CountProcessingJobs()
    {
        return await GetProcessingStates().CountAsync();
    }

    private IQueryable<Guid> GetProcessingStates()
    {
        var query = _context.Set<JobState>()
            .Where(x => x.State == State.Processing)
            .Select(x => x.JobId).AsQueryable();
        return query;
    }

    public async Task<PagedList<JobModel>> GetJobStatesInProcess(BaseListRequest request)
    {
        var jobs = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Processing)
            .Select(x => new JobModel
            {
                Id = x.Id,
                CurrentState = x.CurrentState,
                CreateTime = x.CreateTime,
                Message = x.Message,
                ScheduleTime = x.ScheduleTime,
                Type = x.Type
            })
            .AsQueryable().ToPagedListAsync(request);
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

    public async Task<int> GetServerCount()
    {
        return await _context.Set<Server>().CountAsync();
    }

    public async Task<List<ServerModel>> GetServers()
    {
        var servers = await _context.Set<Server>().ToListAsync();

        var workers = await _context.Set<Worker>().ToListAsync();

        var processingJobs = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Processing)
            .Where(x => x.CurrentWorkerId != null)
            .Select(x => new { x.CurrentWorkerId, x.Id, x.Type })
            .ToListAsync();

        var jobByWorker = processingJobs.ToDictionary(j => j.CurrentWorkerId!.Value);

        var workersByServer = workers
            .GroupBy(w => w.ServerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return servers.Select(s => new ServerModel
        {
            Id = s.Id,
            ServerName = s.ServerName,
            StartedTime = s.StartedTime,
            LastHeartbeatTime = s.LastHeartbeatTime,
            ServiceCount = s.ServiceCount,
            Workers = workersByServer.GetValueOrDefault(s.Id, new List<Worker>())
                .Select(w =>
                {
                    jobByWorker.TryGetValue(w.Id, out var activeJob);
                    return new WorkerModel
                    {
                        WorkerId = w.Id,
                        StartedTime = w.StartedTime,
                        LastHeartbeatTime = w.LastHeartbeatTime,
                        CurrentJobId = activeJob?.Id,
                        CurrentJobType = activeJob?.Type
                    };
                })
                .ToList()
        }).ToList();
    }
}
