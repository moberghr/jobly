using System.Text.Json;
using Cronos;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Jobly.Core.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

public interface IJoblyWorkerService
{
    Task<bool> GetAndProcessJob(CancellationToken cancellationToken);
}

public class JoblyWorkerService<TContext> : IJoblyWorkerService
    where TContext : DbContext
{
    private readonly Guid _workerId = Guid.NewGuid();
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<JoblyWorkerService<TContext>> _logger;
    private readonly JoblyWorkerConfiguration _configuration;

    public JoblyWorkerService(IServiceScopeFactory serviceScopeFactory, ILogger<JoblyWorkerService<TContext>> logger, IOptions<JoblyWorkerConfiguration> configuration)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _configuration = configuration.Value;
    }

    private void UpdateJobStatusToProcessing(TContext context, Job job)
    {
        var jobState = new JobState
        {
            JobId = job.Id,
            DateTime = DateTime.UtcNow,
            State = State.Processing,
            Message = $"The job {job.Id} is being processed"
        };
        
        job.CurrentState = State.Processing;
        job.CurrentServerId = _configuration.ServerId;
        job.CurrentWorkerId = _workerId;

        context.Set<JobState>().Add(jobState);
    }

    public async Task<bool> GetAndProcessJob(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var job = context.Set<Job>()
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => x.ScheduleTime < DateTime.UtcNow)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.ScheduleTime)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .FirstOrDefault();

        // if we didn't find any messages then we wait, otherwise we query again immediately 
        if (job == null)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        _logger.LogInformation("Worker {workerId} fetched message {id}", _workerId, job.Id);

        UpdateJobStatusToProcessing(context, job);

        if (job.RecurringJobId.HasValue)
        {
            await CreateNextJob(context, job, cancellationToken);
        }

        // Saving the job in processing state so that it is marked as processing in the db.
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        try
        {
            // Processing the message, we don't want to keep a transaction open during this time, it may take a while
            // and we don't want to keep a db lock open for that long. There is also no need to rollback the transaction
            // since we are not doing any db operations here. The ProcessOutboxMessage has its own scope anyway.
            _logger.LogInformation("Worker {workerId} processing message {id}", _workerId, job.Id);
            await ProcessOutboxMessage(job, cancellationToken);
            _logger.LogInformation("Worker {workerId} processed message {id}", _workerId, job.Id);

            await using var endTransaction = await context.Database.BeginTransactionAsync(default);

            await UpdateJobData(context, job, message: null, default);

            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing message {id}", job.Id);
            await using var endTransaction = await context.Database.BeginTransactionAsync(default);
            await UpdateJobData(context, job, e.Message, default);

            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }

        return true;
    }

    private async Task CreateNextJob(TContext context, Job job, CancellationToken cancellationToken)
    {

        var recurringJob = await context.Set<RecurringJob>()
            .Where(x => x.Id == job.RecurringJobId)
            .FirstAsync(cancellationToken);

        if (recurringJob.NextJobId != job.Id)
        {
            return;
        }

        var fromUtc = DateTime.SpecifyKind(recurringJob.NextExecution ?? DateTime.UtcNow, DateTimeKind.Utc);
        var nextJobScheduleTime = CronExpression.Parse(recurringJob.Cron).GetNextOccurrence(fromUtc);

        var newJobState = JobHelper.CreateJobAndJobState(
            message: recurringJob.Message, 
            type: recurringJob.Type,
            retries: 0,
            scheduleTime: nextJobScheduleTime,
            maxRetries: 0, 
            priority: job.Priority,
            parentId: null,
            recurringJobId: recurringJob.Id, 
            state: State.Enqueued);

        recurringJob.LastExecution = recurringJob.NextExecution;
        recurringJob.LastJobId = recurringJob.NextJobId;

        recurringJob.NextExecution = nextJobScheduleTime;
        recurringJob.NextJob = newJobState.Job;

    }

    private async Task ProcessOutboxMessage(Job message, CancellationToken cancellationToken)
    {
        var type = Type.GetType(message.Type);

        if (type is null)
        {
            throw new JoblyException($"Unknown type {message.Type}");
        }

        var request = JsonSerializer.Deserialize(message.Message, type);

        if (request is null)
        {
            throw new JoblyException($"Unable to deserialize message {message.Message} to type {message.Type}");
        }

        using var scope = _serviceScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(request, cancellationToken);
    }

    private static async Task UpdateJobData(TContext context, Job job, string? message, CancellationToken cancellationToken)
    {
        var state = !string.IsNullOrEmpty(message) ? State.Failed : State.Completed;
        if (job.RetriedTimes < job.MaxRetries && !string.IsNullOrEmpty(message))
        {
            state = State.Enqueued;
            job.RetriedTimes += 1;
        }

        job.CurrentState = state;
        
        var isParent = await context.Set<Job>()
            .Where(x => x.ParentJobId == job.Id)
            .AnyAsync(cancellationToken);

        if (job.CurrentState == State.Completed && isParent)
        {
            await UpdateChildJobs(context, job.Id, cancellationToken);
        }

        if (job.BatchId != null)
        {
            await UpdateCurrentAndNextBatchFromChildJob(context, job.BatchId.Value, cancellationToken);
        }

        await CreateJobState(context, job.Id, state, string.IsNullOrEmpty(message) ? $"Job {job.Id} is completed" : message, cancellationToken);
    }

    private static async Task CreateJobState(TContext context, Guid jobId, State state, string? message, CancellationToken cancellationToken)
    {
        var jobState = new JobState
        {
            JobId = jobId,
            DateTime = DateTime.UtcNow,
            State = state,
            Message = message
        };

        await context.Set<JobState>().AddAsync(jobState, cancellationToken);
    }

    private static async Task UpdateChildJobs(TContext context, Guid parentJobId, CancellationToken cancellationToken)
    {
        await context.Set<Job>()
            .Where(x => x.ParentJobId == parentJobId)
            .Where(x => x.CurrentState == State.Awaiting)
            // If a job has Batch property in it, then it's a placeholder job, and we don't want to change current status of a placeholder job
            .Where(x => x.Batch == null)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.CurrentState, State.Enqueued), cancellationToken);
    }

    private static async Task UpdateCurrentAndNextBatchFromChildJob(TContext context, Guid batchId, CancellationToken cancellationToken)
    {
        var currentBatch = await context.Set<Batch>()
            .Where(x => x.Id == batchId)
            .TagWith(InterceptorConstants.RowLockTableBatch)
            .FirstOrDefaultAsync(cancellationToken);

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
        public Job Job { get; init; } = null!;

        public bool IsParent { get; init; }
    }
}