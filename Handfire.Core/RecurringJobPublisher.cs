using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Cronos;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Core.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

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

        using var transaction = await _context.Database.BeginTransactionAsync();

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

        var jobStats = new List<JobState>
        {
            new() { State = State.Created, DateTime = DateTime.UtcNow}
        };

        var job = new Job
        {
            Message = jobMessage!,
            Type = jobType!,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = nextJobScheduleTime,
            CurrentState = State.Created,
            JobStates = jobStats
        };

        return job;
    }

    private async Task<RecurringJob> AddOrUpdateRecurringJobToDb<T>(T message, string name, string cronExpression, Job job) where T : class
    {
        var (nextJobScheduleTime, jobMessage, jobType) = GetRecurringJobData(message, cronExpression);

        var recurringJob = await _context.Set<RecurringJob>()
            .Where(x => x.Name == name)
            .SingleOrDefaultAsync();

        if (recurringJob != null)
        {
            // if nextJob is LOCKED in HandfireWorker it will WAIT and timeout (after 30 sec)
            var nextJob = await _context.Set<Job>()
                .Where(x => x.Id == recurringJob.NextJobId)
                .TagWith(ForUpdateSkipLockedCommandInterceptor.Label)
                .FirstAsync();

            if (nextJob.ProcessedTime == null)
            {
                nextJob.ProcessedTime = DateTime.UtcNow;
                nextJob.CurrentState = State.Deleted;

                var jobState = new JobState
                {
                    DateTime = DateTime.UtcNow,
                    State = State.Deleted,
                    JobId = nextJob.Id,
                };

                await _context.Set<JobState>().AddAsync(jobState);
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
