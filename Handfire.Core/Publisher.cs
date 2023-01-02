using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Cronos;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
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

        var recurringJob = new RecurringJob
        {
            Name = name,
            Message = JsonSerializer.Serialize(message),
            Type = message.GetType().AssemblyQualifiedName!,
            Cron = cronExpression,
            CreatedAt = DateTime.UtcNow,
            LastExecution = null,
            NextExecution = null
        };

        await _context.Set<RecurringJob>().AddAsync(recurringJob);
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