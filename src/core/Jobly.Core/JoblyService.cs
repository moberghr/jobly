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

    // Job details & actions
    Task<JobDetailModel?> GetJobById(Guid jobId);
    Task DeleteJob(Guid jobId);
    Task RequeueJob(Guid jobId);

    // Awaiting jobs
    Task<PagedList<JobModel>> GetAwaitingJobs(BaseListRequest request);

    // Messages
    Task<PagedList<MessageModel>> GetMessages(BaseListRequest request);
    Task<MessageDetailModel?> GetMessageById(Guid messageId);

    // Recurring jobs
    Task<PagedList<RecurringJobModel>> GetRecurringJobs(BaseListRequest request);
    Task TriggerRecurringJob(int id);
    Task DeleteRecurringJob(int id);
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
        var awaiting = await GetJobsCount(State.Awaiting);
        var messages = await _context.Set<Message>().CountAsync();

        var model = new DashboardStatistics
        {
            Total = total,
            Pending = pending,
            Scheduled = scheduled,
            Created = created,
            Completed = completed,
            Failed = failed,
            Processing = processing,
            Servers = servers,
            Awaiting = awaiting,
            Messages = messages
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

    // ==================== Job Details & Actions ====================

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
                MaxRetries = x.MaxRetries
            })
            .FirstOrDefaultAsync();

        if (job == null) return null;

        job.StateHistory = await _context.Set<JobState>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.DateTime)
            .Select(x => new JobStateModel
            {
                Id = x.Id,
                State = x.State,
                DateTime = x.DateTime,
                Message = x.Message,
                JobId = x.JobId
            })
            .ToListAsync();

        job.Logs = await _context.Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .Select(x => new JobLogModel
            {
                Id = x.Id,
                Timestamp = x.Timestamp,
                Level = x.Level,
                Message = x.Message,
                Exception = x.Exception
            })
            .ToListAsync();

        // Sibling jobs (from same message)
        if (job.MessageId != null)
        {
            job.SiblingJobs = await _context.Set<Job>()
                .Where(x => x.MessageId == job.MessageId && x.Id != jobId)
                .Select(x => new JobModel
                {
                    Id = x.Id, Type = x.Type, Message = x.Message,
                    CreateTime = x.CreateTime, ScheduleTime = x.ScheduleTime,
                    CurrentState = x.CurrentState
                })
                .ToListAsync();
        }

        // Child jobs (continuations)
        job.ChildJobs = await _context.Set<Job>()
            .Where(x => x.ParentJobId == jobId)
            .Select(x => new JobModel
            {
                Id = x.Id, Type = x.Type, Message = x.Message,
                CreateTime = x.CreateTime, ScheduleTime = x.ScheduleTime,
                CurrentState = x.CurrentState
            })
            .ToListAsync();

        return job;
    }

    public async Task DeleteJob(Guid jobId)
    {
        var job = await _context.Set<Job>().FindAsync(jobId);
        if (job == null) throw new ArgumentException("Job not found.");

        job.CurrentState = State.Deleted;

        var jobState = new JobState
        {
            JobId = job.Id,
            DateTime = DateTime.UtcNow,
            State = State.Deleted,
            Message = $"Job {job.Id} was deleted"
        };

        await _context.Set<JobState>().AddAsync(jobState);
        await _context.SaveChangesAsync();
    }

    public async Task RequeueJob(Guid jobId)
    {
        var job = await _context.Set<Job>().FindAsync(jobId);
        if (job == null) throw new ArgumentException("Job not found.");

        job.CurrentState = State.Enqueued;
        job.HandlerType = null;

        var jobState = new JobState
        {
            JobId = job.Id,
            DateTime = DateTime.UtcNow,
            State = State.Enqueued,
            Message = $"Job {job.Id} was requeued"
        };

        await _context.Set<JobState>().AddAsync(jobState);
        await _context.SaveChangesAsync();
    }

    // ==================== Awaiting Jobs ====================

    public async Task<PagedList<JobModel>> GetAwaitingJobs(BaseListRequest request)
    {
        return await GetJobsByState(State.Awaiting).ToPagedListAsync(request);
    }

    // ==================== Messages ====================

    public async Task<PagedList<MessageModel>> GetMessages(BaseListRequest request)
    {
        return await _context.Set<Message>()
            .OrderByDescending(x => x.CreateTime)
            .Select(x => new MessageModel
            {
                Id = x.Id,
                Type = x.Type,
                Payload = x.Payload,
                Priority = x.Priority,
                CurrentState = x.CurrentState,
                JobCount = x.JobCount,
                CreateTime = x.CreateTime
            })
            .ToPagedListAsync(request);
    }

    public async Task<MessageDetailModel?> GetMessageById(Guid messageId)
    {
        var message = await _context.Set<Message>()
            .Where(x => x.Id == messageId)
            .Select(x => new MessageDetailModel
            {
                Id = x.Id,
                Type = x.Type,
                Payload = x.Payload,
                Priority = x.Priority,
                CurrentState = x.CurrentState,
                JobCount = x.JobCount,
                CreateTime = x.CreateTime
            })
            .FirstOrDefaultAsync();

        if (message == null) return null;

        message.Jobs = await _context.Set<Job>()
            .Where(x => x.MessageId == messageId)
            .Select(x => new JobModel
            {
                Id = x.Id,
                Type = x.Type,
                Message = x.Message,
                CreateTime = x.CreateTime,
                ScheduleTime = x.ScheduleTime,
                CurrentState = x.CurrentState
            })
            .ToListAsync();

        return message;
    }

    // ==================== Recurring Jobs ====================

    public async Task<PagedList<RecurringJobModel>> GetRecurringJobs(BaseListRequest request)
    {
        return await _context.Set<RecurringJob>()
            .OrderBy(x => x.NextExecution)
            .Select(x => new RecurringJobModel
            {
                Id = x.Id,
                Name = x.Name,
                Cron = x.Cron,
                Type = x.Type,
                NextExecution = x.NextExecution,
                LastExecution = x.LastExecution,
                CreatedAt = x.CreatedAt
            })
            .ToPagedListAsync(request);
    }

    public async Task TriggerRecurringJob(int id)
    {
        var recurringJob = await _context.Set<RecurringJob>().FindAsync(id);
        if (recurringJob == null) throw new ArgumentException("Recurring job not found.");

        // Create a new job from the recurring job definition
        var jobState = new JobState
        {
            Job = new Job
            {
                Type = recurringJob.Type,
                Message = recurringJob.Message,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                CurrentState = State.Enqueued,
                MaxRetries = 0,
                Priority = Priority.Normal,
                RecurringJobId = recurringJob.Id
            },
            State = State.Enqueued,
            DateTime = DateTime.UtcNow
        };

        await _context.Set<JobState>().AddAsync(jobState);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRecurringJob(int id)
    {
        var recurringJob = await _context.Set<RecurringJob>().FindAsync(id);
        if (recurringJob == null) throw new ArgumentException("Recurring job not found.");

        _context.Set<RecurringJob>().Remove(recurringJob);
        await _context.SaveChangesAsync();
    }
}
