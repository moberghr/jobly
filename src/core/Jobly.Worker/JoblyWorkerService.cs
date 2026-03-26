using System.Text.Json;
using Cronos;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Interceptors;
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
    private readonly Guid _workerId;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<JoblyWorkerService<TContext>> _logger;
    private readonly JoblyWorkerConfiguration _configuration;

    public JoblyWorkerService(Guid workerId, IServiceScopeFactory serviceScopeFactory, ILogger<JoblyWorkerService<TContext>> logger, IOptions<JoblyWorkerConfiguration> configuration)
    {
        _workerId = workerId;
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

        var isMultiHandlerRouting = false;

        try
        {
            _logger.LogInformation("Worker {workerId} processing message {id}", _workerId, job.Id);
            var hadNoHandler = job.HandlerType == null;
            await ProcessOutboxMessage(context, job, cancellationToken);

            // Detect multi-handler routing: HandlerType was null before and still null after
            // (single-handler sets it during ProcessOutboxMessage, multi-handler doesn't)
            isMultiHandlerRouting = hadNoHandler && job.HandlerType == null;

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

        // If this was a multi-handler routing job, immediately process one of the child jobs
        if (isMultiHandlerRouting)
        {
            return await GetAndProcessJob(cancellationToken);
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

        context.Set<JobState>().Add(newJobState);
        recurringJob.NextJobId = newJobState.Job.Id;

    }

    private async Task ProcessOutboxMessage(TContext context, Job job, CancellationToken cancellationToken)
    {
        var messageType = Type.GetType(job.Type);

        if (messageType is null)
        {
            throw new JoblyException($"Unknown type {job.Type}");
        }

        var message = JsonSerializer.Deserialize(job.Message, messageType);

        if (message is null)
        {
            throw new JoblyException($"Unable to deserialize message {job.Message} to type {job.Type}");
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dispatcher = new JobDispatcher();

        if (job.HandlerType != null)
        {
            // Phase 2: Execute — run specific handler through pipeline
            var handlerType = Type.GetType(job.HandlerType);
            if (handlerType is null)
            {
                throw new JoblyException($"Unknown handler type {job.HandlerType}");
            }
            await dispatcher.ExecuteHandler(message, messageType, handlerType, scope.ServiceProvider, cancellationToken);
            return;
        }

        // Discover handlers for this message type
        var handlerTypes = dispatcher.DiscoverHandlers(messageType, scope.ServiceProvider);

        if (handlerTypes.Count == 0)
        {
            throw new JoblyException($"No handler registered for {messageType.Name}");
        }

        if (handlerTypes.Count == 1)
        {
            // Single handler: execute directly on this job (no fan-out)
            job.HandlerType = handlerTypes[0].AssemblyQualifiedName;
            await dispatcher.ExecuteHandler(message, messageType, handlerTypes[0], scope.ServiceProvider, cancellationToken);
            return;
        }

        // Multiple handlers: fan out — create a child job per handler
        foreach (var handlerType in handlerTypes)
        {
            var childJobState = JobHelper.CreateJobAndJobState(
                message: job.Message,
                type: job.Type,
                retries: 0,
                scheduleTime: null,
                maxRetries: job.MaxRetries,
                priority: job.Priority,
                parentId: null,
                state: State.Enqueued);

            childJobState.Job.HandlerType = handlerType.AssemblyQualifiedName;

            context.Set<JobState>().Add(childJobState);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpdateJobData(TContext context, Job job, string? message, CancellationToken cancellationToken)
    {
        var state = !string.IsNullOrEmpty(message) ? State.Failed : State.Completed;
        if (job.RetriedTimes < job.MaxRetries && !string.IsNullOrEmpty(message))
        {
            state = State.Enqueued;
            job.RetriedTimes += 1;
            job.HandlerType = null; // Clear so handler is re-discovered on retry
        }

        job.CurrentState = state;
        job.CurrentWorkerId = null;
        
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
        var childJobs = await context.Set<Job>()
            .Where(x => x.ParentJobId == parentJobId)
            .Where(x => x.CurrentState == State.Awaiting)
            .ToListAsync(cancellationToken);

        foreach (var childJob in childJobs)
        {
            // Check if this child job is a batch placeholder
            var batch = await context.Set<Batch>()
                .Where(x => x.Id == childJob.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (batch != null)
            {
                // Batch placeholder: don't change its state (stays Awaiting until
                // UpdateCurrentAndNextBatchFromChildJob sets it Completed when counter hits 0).
                // Enqueue its batch work-item jobs directly.
                var batchJobs = await context.Set<Job>()
                    .Where(x => x.BatchId == batch.Id)
                    .ToListAsync(cancellationToken);

                foreach (var batchJob in batchJobs)
                {
                    batchJob.CurrentState = State.Enqueued;
                }
            }
            else
            {
                childJob.CurrentState = State.Enqueued;
            }
        }
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