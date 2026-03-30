using System.Globalization;
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

    Task<PagedList<JobModel>> GetRecurringJobHistory(int id, BaseListRequest request);

    Task TriggerRecurringJob(int id);

    Task DeleteRecurringJob(int id);
}

public class RecurringJobService<TContext> : IRecurringJobService
    where TContext : DbContext
{
    private readonly TContext _context;

    public RecurringJobService(TContext context)
    {
        _context = context;
    }

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
                Name = x.Name,
                Cron = x.Cron,
                Type = x.Type,
                Message = x.Message,
                NextExecution = x.NextExecution,
                LastExecution = x.LastExecution,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                NextJobId = x.NextJobId,
                LastJobId = x.LastJobId,
                TotalJobCount = _context.Set<Job>().Count(j => j.RecurringJobId == x.Id),
            })
            .FirstOrDefaultAsync();
    }

    public async Task<PagedList<JobModel>> GetRecurringJobHistory(int id, BaseListRequest request)
    {
        return await _context.Set<Job>()
            .Where(j => j.RecurringJobId == id)
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

    public async Task TriggerRecurringJob(int id)
    {
        var recurringJob = await _context.Set<RecurringJob>().FindAsync(id) ?? throw new ArgumentException("Recurring job not found.", nameof(id));

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
            RecurringJobId = recurringJob.Id,
        };

        await _context.Set<Job>().AddAsync(job);
        await _context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = job.Id,
            EventType = "Created",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = $"Job {job.Id} was created from recurring job {recurringJob.Id}",
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
