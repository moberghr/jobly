using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Interceptors;
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

    Task<PagedList<JobModel>> GetJobStatesInProcess(BaseListRequest request);

    Task<int> CountProcessingJobs();

    Task<List<ServerModel>> GetServers();

    Task<int> GetServerCount();

    // Job details & actions
    Task<JobDetailModel?> GetJobById(Guid jobId);
    Task DeleteJob(Guid jobId);
    Task RequeueJob(Guid jobId);
    Task<BulkResultModel> BulkDeleteJobs(Guid[] jobIds);
    Task<BulkResultModel> BulkRequeueJobs(Guid[] jobIds);

    // Awaiting jobs
    Task<PagedList<JobModel>> GetAwaitingJobs(BaseListRequest request);

    // Messages
    Task<PagedList<MessageModel>> GetMessages(BaseListRequest request);
    Task<MessageDetailModel?> GetMessageById(Guid messageId);

    // Recurring jobs
    Task<PagedList<RecurringJobModel>> GetRecurringJobs(BaseListRequest request);
    Task TriggerRecurringJob(int id);
    Task DeleteRecurringJob(int id);

    // Batches
    Task<PagedList<BatchModel>> GetBatches(BaseListRequest request);
    Task<BatchDetailModel?> GetBatchById(Guid batchId);

    // Statistics
    Task<List<StatsHistoryPoint>> GetStatsHistory(int hours = 24);
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
        var processing = await CountProcessingJobs();

        var servers = await GetServerCount();
        var awaiting = await GetJobsCount(State.Awaiting);
        var messages = await _context.Set<Message>().CountAsync();
        var batches = await _context.Set<Batch>().CountAsync();

        var totalSucceeded = await _context.Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        var totalFailed = await _context.Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        var totalDeleted = await _context.Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
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
            Messages = messages,
            Batches = batches,
            TotalSucceeded = totalSucceeded,
            TotalFailed = totalFailed,
            TotalDeleted = totalDeleted,
            TotalCreated = 0
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


    public async Task<int> CountProcessingJobs()
    {
        return await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Processing)
            .CountAsync();
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
                MaxRetries = x.MaxRetries,
                TraceId = x.TraceId,
                SpawnedByJobId = x.SpawnedByJobId
            })
            .FirstOrDefaultAsync();

        if (job == null) return null;

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

        // Trace: all jobs sharing the same TraceId
        if (job.TraceId != null)
        {
            job.TraceJobs = await _context.Set<Job>()
                .Where(x => x.TraceId == job.TraceId && x.Id != jobId)
                .OrderBy(x => x.CreateTime)
                .Select(x => new JobModel
                {
                    Id = x.Id, Type = x.Type, Message = x.Message,
                    CreateTime = x.CreateTime, ScheduleTime = x.ScheduleTime,
                    CurrentState = x.CurrentState
                })
                .ToListAsync();
        }

        return job;
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
            throw new ArgumentException("Job not found.");
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
            Message = $"Job {job.Id} was deleted"
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
            throw new ArgumentException("Job not found.");
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
            Message = $"Job {job.Id} was requeued"
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
            _ => null
        };

        if (key != null)
        {
            await _context.Set<Statistic>()
                .Where(x => x.Key == key)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Value, p => p.Value - 1));
        }
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
                Queue = x.Queue,
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
                Queue = x.Queue,
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
        var job = new Job
        {
            Type = recurringJob.Type,
            Message = recurringJob.Message,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            CurrentState = State.Enqueued,
            MaxRetries = 0,
            Queue = "default",
            RecurringJobId = recurringJob.Id
        };

        await _context.Set<Job>().AddAsync(job);
        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = job.Id,
            EventType = "Created",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = $"Job {job.Id} was created from recurring job {recurringJob.Id}"
        });
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRecurringJob(int id)
    {
        var recurringJob = await _context.Set<RecurringJob>().FindAsync(id);
        if (recurringJob == null) throw new ArgumentException("Recurring job not found.");

        _context.Set<RecurringJob>().Remove(recurringJob);
        await _context.SaveChangesAsync();
    }

    // ==================== Batches ====================

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
                CreateTime = x.PlaceholderJob.CreateTime
            })
            .ToPagedListAsync(request);
    }

    public async Task<BatchDetailModel?> GetBatchById(Guid batchId)
    {
        var batch = await _context.Set<Batch>()
            .Where(b => b.Id == batchId)
            .FirstOrDefaultAsync();

        if (batch == null) return null;

        var placeholderJob = await _context.Set<Job>()
            .Where(j => j.Id == batchId)
            .FirstOrDefaultAsync();

        if (placeholderJob == null) return null;

        var totalJobs = await _context.Set<Job>()
            .Where(j => j.BatchId == batchId)
            .CountAsync();

        var jobs = await _context.Set<Job>()
            .Where(j => j.BatchId == batchId)
            .Select(j => new JobModel
            {
                Id = j.Id, Type = j.Type, Message = j.Message,
                CreateTime = j.CreateTime, ScheduleTime = j.ScheduleTime,
                CurrentState = j.CurrentState
            })
            .ToListAsync();

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
            Jobs = jobs,
            ContinuationJobId = continuationJob == Guid.Empty ? null : continuationJob
        };
    }

    // ==================== Statistics History ====================

    public async Task<List<StatsHistoryPoint>> GetStatsHistory(int hours = 24)
    {
        var since = DateTime.UtcNow.AddHours(-hours);

        var hourlyStats = await _context.Set<Statistic>()
            .Where(x => x.Key.StartsWith("stats:succeeded:") || x.Key.StartsWith("stats:failed:"))
            .ToListAsync();

        // Parse keys like "stats:succeeded:2026-03-28-14" into date + metric
        var points = new Dictionary<string, StatsHistoryPoint>();

        foreach (var stat in hourlyStats)
        {
            var parts = stat.Key.Split(':');
            if (parts.Length != 3) continue;

            var metric = parts[1]; // "succeeded" or "failed"
            var hourStr = parts[2]; // "2026-03-28-14"

            if (!DateTime.TryParseExact(hourStr, "yyyy-MM-dd-HH",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var hour))
                continue;

            if (hour < since) continue;

            if (!points.ContainsKey(hourStr))
                points[hourStr] = new StatsHistoryPoint { Hour = hour };

            if (metric == "succeeded") points[hourStr].Succeeded = stat.Value;
            else if (metric == "failed") points[hourStr].Failed = stat.Value;
        }

        return points.Values.OrderBy(p => p.Hour).ToList();
    }
}
