using Cronos;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Polls recurring jobs and creates the next occurrence when due.
/// Decouples scheduling from execution — recurring jobs fire regardless of whether
/// the previous execution succeeded or failed.
/// </summary>
public class RecurringJobSchedulerTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    public RecurringJobSchedulerTask(
        IServiceScopeFactory scopeFactory,
        ILogger<RecurringJobSchedulerTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IDistributedLockProvider lockProvider,
        TimeProvider timeProvider)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:recurring-scheduler", lockProvider)
    {
    }

    protected override string TaskName => "RecurringJobScheduler";

    protected override TimeSpan DefaultInterval => TimeSpan.FromSeconds(15);

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var count = await ScheduleRecurringJobs(context, TimeProvider);
        return count > 0 ? $"Scheduled {count} recurring jobs" : null;
    }

    public static async Task<int> ScheduleRecurringJobs<TCtx>(TCtx context, TimeProvider timeProvider)
        where TCtx : DbContext
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var count = 0;

        var recurringJobs = await context.Set<RecurringJob>()
            .Where(x => x.NextExecution != null && x.NextExecution <= now)
            .ToListAsync();

        foreach (var recurringJob in recurringJobs)
        {
            // Check if the next job already exists and is still pending
            if (recurringJob.NextJobId != null)
            {
                var nextJob = await context.Set<Job>()
                    .Where(x => x.Id == recurringJob.NextJobId)
                    .Select(x => new { x.CurrentState })
                    .FirstOrDefaultAsync();

                // Job still enqueued or processing — don't create another
                if (nextJob != null && (nextJob.CurrentState == State.Enqueued || nextJob.CurrentState == State.Processing))
                {
                    continue;
                }
            }

            var nextExecution = CronExpression.Parse(recurringJob.Cron)
                .GetNextOccurrence(DateTime.SpecifyKind(now, DateTimeKind.Utc));

            var newJob = JobHelper.CreateJob(
                message: recurringJob.Message,
                type: recurringJob.Type,
                retries: 0,
                scheduleTime: nextExecution,
                maxRetries: 0,
                queue: recurringJob.Queue,
                parentId: null,
                recurringJobId: recurringJob.Id,
                state: State.Enqueued,
                now: now);

            context.Set<Job>().Add(newJob);
            context.Set<JobLog>().Add(new JobLog
            {
                JobId = newJob.Id,
                EventType = "Created",
                Timestamp = now,
                Level = "Information",
                Message = $"Job {newJob.Id} created for recurring job {recurringJob.Id}",
            });

            recurringJob.LastExecution = recurringJob.NextExecution;
            recurringJob.LastJobId = recurringJob.NextJobId;
            recurringJob.NextExecution = nextExecution;
            recurringJob.NextJobId = newJob.Id;

            count++;
        }

        if (count > 0)
        {
            await context.SaveChangesAsync();
        }

        return count;
    }
}
