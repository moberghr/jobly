using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Cronos;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public interface IRecurringJobPublisher
{
    Task AddOrUpdateRecurringJob<T>(T message, string name, string cron) where T : class;
}

public class RecurringJobPublisher<TContext> : IRecurringJobPublisher
    where TContext : DbContext
{
    private readonly TContext _context;

    public RecurringJobPublisher(TContext context)
    {
        _context = context;
    }

    public async Task AddOrUpdateRecurringJob<T>(T message, string name, string cronExpression) where T : class
    {
        ValidateCronExpression(cronExpression);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var job = CreateJobForRecurringJob(message, cronExpression);

        var recurringJob = await AddOrUpdateRecurringJobToDb(message, name, cronExpression, job);

        await _context.SaveChangesAsync();

        job.RecurringJob = recurringJob;
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
    }

    private Job CreateJobForRecurringJob<T>(T message, string cronExpression) where T : class
    {
        var (nextJobScheduleTime, jobMessage, jobType) = GetRecurringJobData(message, cronExpression);
        if (nextJobScheduleTime == null || string.IsNullOrWhiteSpace(jobMessage) || string.IsNullOrWhiteSpace(jobType))
        {
            throw new InvalidOperationException("Failed to create job for recurring job.");
        }

        var jobState = JobHelper.CreateJobAndJobState(jobMessage, jobType, 0, nextJobScheduleTime, 0, Priority.Normal, null, State.Enqueued);

        return jobState.Job;
    }

    private async Task<RecurringJob> AddOrUpdateRecurringJobToDb<T>(T message, string name, string cronExpression, Job job) where T : class
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
                nextJob.JobStates.Add(new() { DateTime = DateTime.UtcNow, State = State.Deleted });
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

    private (DateTime? nextJobScheduleTime, string? jobMessage, string? jobType) GetRecurringJobData<T>(T message, string cronExpression) where T : class
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
