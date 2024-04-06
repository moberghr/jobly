using Cronos;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Interceptors;

/// <summary>
/// Make sure that the Recurring Job is scheduled correctly
/// </summary>
public class RecurringInterceptor : JobInterceptor
{
    public override async Task JobWillExecuteAsync(JobExecutingContext context, CancellationToken cancellationToken)
    {
        if (context.Job.RecurringJobId == null)
        {
            return;
        }
        
        var recurringJob = await context.DbContext.Set<RecurringJob>()
            .Where(x => x.Id == context.Job.RecurringJobId)
            .FirstAsync(cancellationToken);

        if (recurringJob.NextJobId != context.Job.Id)
        {
            return;
        }

        var createTime = DateTime.UtcNow;

        var fromUtc = DateTime.SpecifyKind(recurringJob.NextExecution ?? DateTime.UtcNow, DateTimeKind.Utc);
        var nextJobScheduleTime = CronExpression.Parse(recurringJob.Cron).GetNextOccurrence(fromUtc);

        var jobStats = new List<JobState>
        {
            new() {State = State.Enqueued, DateTime = createTime}
        };

        var newJobId = Guid.NewGuid();
        var newJob = new Job
        {
            Id = newJobId,
            Message = recurringJob.Message,
            Type = recurringJob.Type,
            CreateTime = createTime,
            ScheduleTime = nextJobScheduleTime ?? createTime,
            Priority = context.Job.Priority,
            CurrentState = State.Enqueued,
            RecurringJobId = recurringJob.Id,
            JobStates = jobStats
        };

        recurringJob.LastExecution = recurringJob.NextExecution;
        recurringJob.LastJobId = recurringJob.NextJobId;

        recurringJob.NextExecution = nextJobScheduleTime;
        recurringJob.NextJob = newJob;
    }
}