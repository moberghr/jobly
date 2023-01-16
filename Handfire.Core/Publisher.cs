using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Xml.Linq;
using Cronos;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public interface IPublisher
{
    Task Publish<T>(T message) where T : class;

    Task Publish<T>(T message, DateTime scheduleTime) where T : class;

    Task AddOrUpdateRecurringJob<T>(T message, string name, string cron) where T : class;
}

public class Publisher<TContext> : IPublisher
    where TContext : DbContext
{
    private readonly TContext _context;

    public Publisher(TContext context)
    {
        _context = context;
    }

    public async Task Publish<T>(T message)
        where T : class
    {
        await CreateJobAndJobState<T>(message, scheduleTime: null);
    }

    public async Task Publish<T>(T message, DateTime scheduleTime)
        where T : class
    {
        await CreateJobAndJobState<T>(message, scheduleTime);
    }

    public async Task AddOrUpdateRecurringJob<T>(T message, string name, string cronExpression) where T : class
    {
        ValidateCronExpression(cronExpression);

        var job = CreateJobForRecurringJob(message, cronExpression);

        var recurringJob = await CreateRecurringJob(message, name, cronExpression);

        await _context.Set<Job>().AddAsync(job);
        await _context.Set<RecurringJob>().AddAsync(recurringJob);

        await _context.SaveChangesAsync();

        recurringJob.NextJobId = job.Id;
        job.RecurringJob = recurringJob;

        _context.Set<RecurringJob>().Update(recurringJob);
        _context.Set<Job>().Update(job);

        await _context.SaveChangesAsync();
    }

    private Job CreateJobForRecurringJob<T>(T message, string cronExpression) where T : class
    {
        var (nextJobScheduleTime, jobMessage, jobType) = GetRecurringJobData(message, cronExpression);

        var jobStats = new List<JobState>
        {
            new() { State = State.Created, DateTime = DateTime.UtcNow}
        };

        var job = new Job
        {
            Message = jobMessage!,
            Type = jobType!,
            CreateTime = DateTime.UtcNow,
            IsRecurringJob = true,
            ScheduleTime = nextJobScheduleTime,
            CurrentState = State.Created,
            JobStates = jobStats
        };

        return job;
    }

    private async Task<RecurringJob> CreateRecurringJob<T>(T message, string name, string cronExpression) where T : class
    {
        var recurringJob = await _context.Set<RecurringJob>()
            .Where(x => x.Name == name)
            .SingleOrDefaultAsync();

        if (recurringJob != null)
        {
            var nextJob = await _context.Set<Job>()
                .Where(x => x.Id == recurringJob.NextJobId)
                .SingleAsync();

            if (nextJob.ProcessedTime == null)
            {
                nextJob.ProcessedTime = DateTime.UtcNow;
                nextJob.CurrentState = State.Obsolete;
                nextJob.JobStates.Add(new() { DateTime = DateTime.UtcNow, State = State.Obsolete });

                _context.Set<Job>().Update(nextJob);
            }
        }

        var (nextJobScheduleTime, jobMessage, jobType) = GetRecurringJobData(message, cronExpression);

        if (recurringJob == null)
        {
            recurringJob = new RecurringJob
            {
                Name = name,
                Message = jobMessage,
                Type = jobType,
                Cron = cronExpression,
                CreatedAt = DateTime.UtcNow,
                NextExecution = nextJobScheduleTime,
            };
        }

        return recurringJob;
    }

    private (DateTime? nextJobScheduleTime, string? jobMessage, string? jobType) GetRecurringJobData<T>(T message, string cronExpression) where T : class
    {
        var nextJobScheduleTime = CronExpression.Parse(cronExpression).GetNextOccurrence(DateTime.UtcNow);
        var jobMessage = JsonSerializer.Serialize(message);
        var jobType = message.GetType().AssemblyQualifiedName!;

        return (nextJobScheduleTime, jobMessage, jobType);
    }

    private async Task CreateJobAndJobState<T>(T message, DateTime? scheduleTime)
        where T : class
    {
        var job = new Job
        {
            CreateTime = DateTime.UtcNow,
            Message = JsonSerializer.Serialize(message),
            Type = message.GetType().AssemblyQualifiedName!,
            ScheduleTime = scheduleTime,
            CurrentState = Enums.State.Created
        };

        var jobState = new JobState
        {
            Job = job,
            State = Enums.State.Created,
            DateTime = DateTime.UtcNow,
        };

        await _context.Set<Job>().AddAsync(job);
        await _context.Set<JobState>().AddAsync(jobState);
    }

    private static void ValidateCronExpression(string cronExpression)
    {
        try
        {
            ParseCronExpression(cronExpression);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                "CRON expression is invalid. Please see the inner exception for details.",
                nameof(cronExpression),
                ex);
        }
    }

    private static CronExpression ParseCronExpression([NotNull] string cronExpression)
    {
        if (cronExpression == null) throw new ArgumentNullException(nameof(cronExpression));

        var parts = cronExpression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var format = CronFormat.Standard;

        if (parts.Length == 6)
        {
            format |= CronFormat.IncludeSeconds;
        }
        else if (parts.Length != 5)
        {
            throw new CronFormatException(
                $"Wrong number of parts in the `{cronExpression}` cron expression, you can only use 5 or 6 (with seconds) part-based expressions.");
        }

        return CronExpression.Parse(cronExpression, format);
    }
}