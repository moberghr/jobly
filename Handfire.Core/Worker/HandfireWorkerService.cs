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
                    })
            .TagWith(InterceptorConstants.RowLockTableJob)
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

        if (jobData.Job.BatchId != null)
        {
            await UpdateCurrentAndNextBatchFromChildJob(context, jobData.Job.BatchId, cancellationToken);
        }

        await UpdateNextBatchFromChildJobs(context, jobData.Job.Id, cancellationToken);

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
        var childJobIds = await context.Set<Job>()
            .Where(x => x.ParentJobId == parentJobId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (childJobIds == null)
        {
            return;
        }

        var batchIds = await context.Set<Batch>()
            .Where(x => childJobIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        // If the next child job is a batch (placeholder job), don't change the child job status to Enqueued
        await context.Set<Job>()
            .Where(x => x.ParentJobId == parentJobId)
            .Where(x => x.CurrentState == State.Awaiting)
            .Where(x => !batchIds.Contains(x.Id))
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.CurrentState, State.Enqueued), cancellationToken);
    }

    private async static Task UpdateNextBatchFromChildJobs(TContext context, string? jobId, CancellationToken cancellationToken)
    {
        var childJobIds = await context.Set<Job>()
            .Where(x => x.ParentJobId ==  jobId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (childJobIds == null)
        {
            return;
        }

        var nextBatchIds = await context.Set<Batch>()
            .Where(x => childJobIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (nextBatchIds == null)
        {
            return;
        }

        var nextBatchJobs = await context.Set<Job>()
            .Where(x => nextBatchIds.Contains(x.BatchId))
            .ToListAsync(cancellationToken);

        foreach (var batchJob in nextBatchJobs)
        {
            batchJob.CurrentState = State.Enqueued;
        }
    }

    private async static Task UpdateCurrentAndNextBatchFromChildJob(TContext context, string batchId, CancellationToken cancellationToken)
    {
        var currentBatch = await context.Set<Batch>()
            .Where(x => x.Id == batchId)
            .TagWith(InterceptorConstants.RowLockTableBatch)
            .FirstOrDefaultAsync(cancellationToken);

        await UpdateBatchBase(context, currentBatch, cancellationToken);
    }

    private async static Task UpdateBatchBase(TContext context, Batch? currentBatch, CancellationToken cancellationToken)
    {
        // Check if this is a batch job
        if (currentBatch == null)
        {
            return;
        }

        currentBatch.Counter--;

        // If all jobs in a single batch are finished
        if (currentBatch.Counter > 0)
        {
            return;
        }

        currentBatch.Counter = 0;

        var currentBatchJob = await context.Set<Job>()
            .Where(x => x.Id == currentBatch.Id)
            .FirstAsync(cancellationToken);

        currentBatchJob.CurrentState = State.Completed;

        var nextBatchJob = await context.Set<Job>()
            .Where(x => x.ParentJobId == currentBatchJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Check if another parent job exists
        // If yes, then start another batch jobs process
        // if no, then no more jobs exists that need to be started (this is the last one)
        if (nextBatchJob == null)
        {
            return;
        }

        var nextBatch = await context.Set<Batch>()
            .Where(x => x.Id == nextBatchJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Check if this is another batch of jobs or...
        if (nextBatch != null)
        {
            var nextBatchJobs = await context.Set<Job>()
                .Where(x => x.BatchId == nextBatch.Id)
                .ToListAsync(cancellationToken);

            foreach (var batchJob in nextBatchJobs)
            {
                batchJob.CurrentState = State.Enqueued;
            }
        }
        // ...A single job
        else
        {
            nextBatchJob.CurrentState = State.Enqueued;
        }
    }

    private class JobData
    {
        public Job Job { get; set; } = null!;

        public bool IsParent { get; set; }
    }
}