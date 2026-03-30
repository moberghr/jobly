using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Cronos;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public interface IRecurringJobPublisher
{
    Task AddOrUpdateRecurringJob<T>(T message, string name, string cron)
        where T : class, IJob;
}

file static class RecurringJobPublisherConstants
{
    public static readonly char[] SplitChars = [' ', '\t'];
}

public class RecurringJobPublisher<TContext> : IRecurringJobPublisher
    where TContext : DbContext
{
    private readonly TContext _context;

    public RecurringJobPublisher(TContext context)
    {
        _context = context;
    }

    public async Task AddOrUpdateRecurringJob<T>(T message, string name, string cronExpression)
        where T : class, IJob
    {
        ValidateCronExpression(cronExpression);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var job = CreateJobForRecurringJob(message, cronExpression);
        _context.Set<Job>().Add(job);
        _context.Set<JobLog>().Add(new JobLog
        {
            JobId = job.Id,
            EventType = "Created",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = $"Job {job.Id} created for recurring job \"{name}\"",
        });
        await _context.SaveChangesAsync();

        var recurringJob = await AddOrUpdateRecurringJobToDb(message, name, cronExpression, job);
        job.RecurringJob = recurringJob;
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
    }

    private static Job CreateJobForRecurringJob<T>(T message, string cronExpression)
        where T : class, IJob
    {
        var (nextJobScheduleTime, jobMessage, jobType) = GetRecurringJobData(message, cronExpression);
        if (nextJobScheduleTime == null || string.IsNullOrWhiteSpace(jobMessage) || string.IsNullOrWhiteSpace(jobType))
        {
            throw new InvalidOperationException("Failed to create job for recurring job.");
        }

        var job = JobHelper.CreateJob(jobMessage, jobType, 0, nextJobScheduleTime, 0, "default", null, State.Enqueued);

        return job;
    }

    private async Task<RecurringJob> AddOrUpdateRecurringJobToDb<T>(T message, string name, string cronExpression, Job job)
        where T : class, IJob
    {
        var (nextJobScheduleTime, jobMessage, jobType) = GetRecurringJobData(message, cronExpression);

        var recurringJob = await _context.Set<RecurringJob>()
            .Where(x => x.Name == name)
            .FirstOrDefaultAsync();

        if (recurringJob != null)
        {
            // if nextJob is LOCKED in JoblyWorker it will WAIT and timeout (after 30 sec)
            var nextJob = await _context.Set<Job>()
                .Where(x => x.Id == recurringJob.NextJobId)
                .TagWith(InterceptorConstants.RowLockTableJob)
                .FirstAsync();

            if (nextJob.CurrentState == State.Enqueued)
            {
                nextJob.CurrentState = State.Deleted;
                _context.Set<JobLog>().Add(new JobLog
                {
                    JobId = nextJob.Id,
                    EventType = "Deleted",
                    Timestamp = DateTime.UtcNow,
                    Level = "Information",
                    Message = $"Job {nextJob.Id} was deleted (recurring job updated)",
                });
            }

            recurringJob.Cron = cronExpression;
            recurringJob.Message = jobMessage;
            recurringJob.Type = jobType;
            recurringJob.UpdatedAt = DateTime.UtcNow;
            recurringJob.LastExecution = recurringJob.NextExecution;
            recurringJob.LastJobId = recurringJob.NextJobId;
            recurringJob.NextExecution = nextJobScheduleTime;
            recurringJob.NextJob = job;

            return recurringJob;
        }

        recurringJob = new RecurringJob
        {
            Name = name,
            Message = jobMessage,
            Type = jobType,
            Cron = cronExpression,
            CreatedAt = DateTime.UtcNow,
            NextExecution = nextJobScheduleTime,
            NextJob = job,
        };

        await _context.Set<RecurringJob>().AddAsync(recurringJob);

        return recurringJob;
    }

    private static (DateTime? nextJobScheduleTime, string? jobMessage, string? jobType) GetRecurringJobData<T>(T message, string cronExpression)
        where T : class, IJob
    {
        var nextJobScheduleTime = CronExpression.Parse(cronExpression).GetNextOccurrence(DateTime.UtcNow);
        var jobMessage = JsonSerializer.Serialize(message);
        var jobType = message.GetType().AssemblyQualifiedName!;

        return (nextJobScheduleTime, jobMessage, jobType);
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

    private static void ParseCronExpression([NotNull] string cronExpression)
    {
        ArgumentNullException.ThrowIfNull(cronExpression);

        var parts = cronExpression.Split(RecurringJobPublisherConstants.SplitChars, StringSplitOptions.RemoveEmptyEntries);
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

        CronExpression.Parse(cronExpression, format);
    }
}
