using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Services;

public interface IRecurringJobService
{
    Task<PagedList<RecurringJobModel>> GetRecurringJobs(BaseListRequest request);

    Task<RecurringJobDetailModel?> GetRecurringJobById(int id);

    Task<PagedList<RecurringJobHistoryModel>> GetRecurringJobHistory(int id, BaseListRequest request);

    Task TriggerRecurringJob(int id);

    Task DeleteRecurringJob(int id);
}

public class RecurringJobService<TContext> : IRecurringJobService
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _timeProvider;

    public RecurringJobService(TContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<PagedList<RecurringJobModel>> GetRecurringJobs(BaseListRequest request)
    {
        return await _context.Set<RecurringJob>()
            .OrderBy(x => x.NextExecution)
            .Select(x => new RecurringJobModel
            {
                Id = x.Id,
                Name = x.Name!,
                Cron = x.Cron!,
                Type = x.Type!,
                NextExecution = x.NextExecution,
                LastExecution = x.LastExecution,
                CreatedAt = x.CreatedAt,
            })
            .ToPagedListAsync(request);
    }

    public async Task<RecurringJobDetailModel?> GetRecurringJobById(int id)
    {
        return await _context.Set<RecurringJob>()
            .Where(x => x.Id == id)
            .Select(x => new RecurringJobDetailModel
            {
                Id = x.Id,
                Name = x.Name!,
                Cron = x.Cron!,
                Type = x.Type!,
                Message = x.Message,
                NextExecution = x.NextExecution,
                LastExecution = x.LastExecution,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .FirstOrDefaultAsync();
    }

    public async Task<PagedList<RecurringJobHistoryModel>> GetRecurringJobHistory(int id, BaseListRequest request)
    {
        var logs = await _context.Set<RecurringJobLog>()
            .Where(l => l.RecurringJobId == id)
            .OrderByDescending(l => l.CreatedAt)
            .ToPagedListAsync(request);

        var jobIds = logs.Items.Select(l => l.JobId).ToList();
        var jobs = await _context.Set<Job>()
            .Where(j => jobIds.Contains(j.Id))
            .Select(j => new { j.Id, j.Type, j.CurrentState })
            .ToDictionaryAsync(j => j.Id);

        var items = logs.Items.Select(l =>
        {
            jobs.TryGetValue(l.JobId, out var job);
            return new RecurringJobHistoryModel
            {
                JobId = l.JobId,
                CreatedAt = l.CreatedAt,
                JobExists = job != null,
                Type = job?.Type,
                CurrentState = job != null ? job.CurrentState : null,
            };
        }).ToList();

        return new PagedList<RecurringJobHistoryModel>(logs.TotalCount, items, logs.PageCount);
    }

    public async Task TriggerRecurringJob(int id)
    {
        var recurringJob = await _context.Set<RecurringJob>().FindAsync(id) ?? throw new ArgumentException("Recurring job not found.", nameof(id));

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var job = new Job
        {
            Type = recurringJob.Type,
            Message = recurringJob.Message,
            CreateTime = now,
            ScheduleTime = now,
            CurrentState = State.Enqueued,
            MaxRetries = 0,
            Queue = recurringJob.Queue,
        };

        await _context.Set<Job>().AddAsync(job);
        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = job.Id,
            EventType = "Created",
            Timestamp = now,
            Level = "Information",
            Message = $"Job {job.Id} was created from recurring job {recurringJob.Id}",
        });
        _context.Set<RecurringJobLog>().Add(new RecurringJobLog
        {
            RecurringJobId = recurringJob.Id,
            JobId = job.Id,
            CreatedAt = now,
        });
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRecurringJob(int id)
    {
        var recurringJob = await _context.Set<RecurringJob>().FindAsync(id) ?? throw new ArgumentException("Recurring job not found.", nameof(id));
        _context.Set<RecurringJob>().Remove(recurringJob);
        await _context.SaveChangesAsync();
    }
}
