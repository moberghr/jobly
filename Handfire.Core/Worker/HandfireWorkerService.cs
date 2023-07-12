using System.Text.Json;
using Cronos;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Core.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Handfire.Core;

public interface IHandfireWorkerService
{
    Task GetAndProcessJob(CancellationToken cancellationToken);
}

public class HandfireWorkerService<TContext> : IHandfireWorkerService
    where TContext : DbContext
{
    private readonly string _workerId = Guid.NewGuid().ToString();
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<HandfireWorkerService<TContext>> _logger;

    public HandfireWorkerService(IServiceScopeFactory serviceScopeFactory, ILogger<HandfireWorkerService<TContext>> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task GetAndProcessJob(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var jobData = context.Set<Job>()
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => x.ScheduleTime < DateTime.UtcNow || x.ScheduleTime == null)
            .Select(x =>
                    new JobData
                    {
                        Job = x,
                        IsParent = x.ChildJobs.Any(),
                        BatchId = x.BatchId,
                    })
            .TagWith(InterceptorConstants.RowLock)
            .FirstOrDefault();

        var job = jobData?.Job;

        // if we didn't find any messages then we wait, otherwise we query again immediately 
        if (job == null)
        {
            await Task.Delay(1000, cancellationToken);

            return;
        }

        _logger.LogInformation("Worker {workerId} fetched message {id}", _workerId, job.Id);

        try
        {
            if (job.RecurringJobId.HasValue)
            {
                await CreateNextJob(job, cancellationToken);
            }

            await ProcessOutboxMessage(job, cancellationToken);
        }
        catch (Exception e)
        {
            await UpdateJobData(context, jobData!, e.Message, cancellationToken);

            transaction.Commit();

            return;
        }

        await UpdateJobData(context, jobData!, message: null, cancellationToken);
        transaction.Commit();
    }

    private async Task CreateNextJob(Job job, CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var temporaryContext = scope.ServiceProvider.GetRequiredService<TContext>();

        var recurringJob = await temporaryContext.Set<RecurringJob>()
            .Where(x => x.Id == job.RecurringJobId)
            .FirstAsync(cancellationToken);

        if (recurringJob.NextJobId != job.Id)
        {
            return;
        }

        var createTime = DateTime.UtcNow;

        var fromUtc = DateTime.SpecifyKind(recurringJob.NextExecution ?? DateTime.UtcNow, DateTimeKind.Utc);
        var nextJobScheduleTime = CronExpression.Parse(recurringJob.Cron).GetNextOccurrence(fromUtc);

        var jobStats = new List<JobState>
        {
            new() { State = State.Enqueued, DateTime = createTime}
        };

        var newJobId = Guid.NewGuid().ToString();
        var newJob = new Job
        {
            Id = newJobId,
            Message = recurringJob.Message,
            Type = recurringJob.Type,
            CreateTime = createTime,
            ScheduleTime = nextJobScheduleTime,
            CurrentState = State.Enqueued,
            RecurringJobId = recurringJob.Id,
            JobStates = jobStats
        };

        recurringJob.LastExecution = recurringJob.NextExecution;
        recurringJob.LastJobId = recurringJob.NextJobId;

        recurringJob.NextExecution = nextJobScheduleTime;
        recurringJob.NextJob = newJob;

        await temporaryContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessOutboxMessage(Job message, CancellationToken cancellationToken)
    {
        var type = Type.GetType(message.Type);

        if (type is null)
        {
            throw new HandfireException($"Unknown type {message.Type}");
        }

        var request = JsonSerializer.Deserialize(message.Message, type);

        if (request is null)
        {
            throw new HandfireException($"Unable to deserialize message {message.Message} to type {message.Type}");
        }

        using var scope = _serviceScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(request, cancellationToken);
    }

    private static async Task UpdateJobData(TContext context, JobData jobData, string? message, CancellationToken cancellationToken)
    {
        var state = !string.IsNullOrEmpty(message) ? State.Failed : State.Completed;
        if (jobData.Job.RetriedTimes < jobData.Job.MaxRetries && !string.IsNullOrEmpty(message))
        {
            state = State.Enqueued;
            jobData.Job.RetriedTimes += 1;
        }

        jobData.Job.CurrentState = state;

        if (jobData.Job.CurrentState == State.Completed && jobData.IsParent)
        {
            await UpdateChildJobs(context, jobData.Job.Id, cancellationToken);
        }

        if (jobData.BatchId != null)
        {
            await UpdateBatch(context, jobData.BatchId, cancellationToken);
        }

        await CreateJobState(context, jobData.Job.Id, state, message, cancellationToken);
    }

    private static async Task CreateJobState(TContext context, string jobId, State state, string? message, CancellationToken cancellationToken)
    {
        var jobState = new JobState
        {
            JobId = jobId,
            DateTime = DateTime.UtcNow,
            State = state,
            Message = message
        };

        await context.Set<JobState>().AddAsync(jobState, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async static Task UpdateChildJobs(TContext context, string parentJobId, CancellationToken cancellationToken)
    {
        await context.Set<Job>()
             .Where(x => x.ParentJobId == parentJobId)
             .Where(x => x.CurrentState == State.Awaiting)
             .ExecuteUpdateAsync(x => x.SetProperty(y => y.CurrentState, State.Enqueued), cancellationToken);
    }

    private async static Task UpdateBatch(TContext context, string batchId, CancellationToken cancellationToken)
    {
        var batchData = await context.Set<Batch>()
            .Where(x => x.BatchStatus != State.Completed)
            .Where(x => x.Id == batchId)
            .Select(x =>
                new
                {
                    Batch = x,
                    Jobs = x.Jobs,
                })
            .TagWith(InterceptorConstants.RowLock)
            .FirstOrDefaultAsync(cancellationToken);

        if (batchData != null)
        {
            batchData.Batch.Counter = batchData.Jobs.Where(x => x.CurrentState != State.Completed).Count();

            if (batchData.Batch.Counter == 0)
            {
                batchData.Batch.BatchStatus = State.Completed;

                var batchContinuationJobs = await context.Set<BatchContinuation>()
                    .Where(x => x.BatchId == batchData.Batch.Id)
                    .Select(x => x.Job)
                    .TagWith(InterceptorConstants.RowLock)
                    .ToListAsync(cancellationToken);

                foreach (var batchContinuationJob in batchContinuationJobs)
                {
                    batchContinuationJob.CurrentState = State.Enqueued;
                }
            }
        }
    }

    private class JobData
    {
        public Job Job { get; set; } = null!;

        public bool IsParent { get; set; }

        public string? BatchId { get; set; }
    }
}