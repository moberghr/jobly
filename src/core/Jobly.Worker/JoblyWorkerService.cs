using System.Text.Json;
using Cronos;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Logging;
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

    public async Task<bool> GetAndProcessJob(CancellationToken cancellationToken)
    {
        // Prefer executing Jobs (real work) over routing Messages
        if (await TryExecuteJob(cancellationToken)) return true;
        if (await TryRouteMessage(cancellationToken)) return true;
        return false;
    }

    // ==================== Message Routing ====================

    private async Task<bool> TryRouteMessage(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var message = context.Set<Message>()
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => _configuration.Queues.Contains(x.Queue))
            .OrderBy(x => x.Queue)
            .ThenBy(x => x.CreateTime)
            .TagWith(InterceptorConstants.RowLockTableMessage)
            .FirstOrDefault();

        if (message == null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        _logger.LogInformation("Worker {workerId} routing message {id}", _workerId, message.Id);

        var dispatcher = new JobDispatcher();
        var messageType = Type.GetType(message.Type);

        if (messageType is null)
        {
            throw new JoblyException($"Unknown type {message.Type}");
        }

        var handlerTypes = dispatcher.DiscoverMessageHandlers(messageType, scope.ServiceProvider);

        if (handlerTypes.Count == 0)
        {
            message.CurrentState = State.Failed;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new JoblyException($"No handler registered for {messageType.Name}");
        }

        // Create a Job for each handler
        foreach (var handlerType in handlerTypes)
        {
            var job = JobHelper.CreateJob(
                message: message.Payload,
                type: message.Type,
                retries: 0,
                scheduleTime: null,
                maxRetries: 0,
                queue: message.Queue,
                parentId: null,
                state: State.Enqueued);

            job.HandlerType = handlerType.AssemblyQualifiedName;
            job.MessageId = message.Id;

            context.Set<Job>().Add(job);
            context.Set<JobLog>().Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Created",
                Timestamp = DateTime.UtcNow,
                Level = "Information",
                Message = $"Job {job.Id} created from message {message.Id}"
            });
        }

        message.JobCount = handlerTypes.Count;
        message.CurrentState = State.Processing;

        await context.Set<Statistic>()
            .Where(x => x.Key == "stats:created")
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Value, p => p.Value + handlerTypes.Count));

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Worker {workerId} routed message {id} to {count} jobs", _workerId, message.Id, handlerTypes.Count);

        // Immediately try to execute one of the newly created jobs
        return await TryExecuteJob(cancellationToken);
    }

    // ==================== Job Execution ====================

    private async Task<bool> TryExecuteJob(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var job = context.Set<Job>()
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => x.ScheduleTime < DateTime.UtcNow)
            .Where(x => _configuration.Queues.Contains(x.Queue))
            .OrderBy(x => x.Queue)
            .ThenBy(x => x.ScheduleTime)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .FirstOrDefault();

        if (job == null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        _logger.LogInformation("Worker {workerId} fetched job {id}", _workerId, job.Id);

        UpdateJobStatusToProcessing(context, job);

        if (job.RecurringJobId.HasValue)
        {
            await CreateNextJob(context, job, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Set up log capture for this job execution
        var logCollector = new JobLogCollector { JobId = job.Id };
        JobLogContext.Current = logCollector;

        try
        {
            _logger.LogInformation("Worker {workerId} executing job {id}", _workerId, job.Id);
            await ExecuteJob(job, scope.ServiceProvider, cancellationToken);
            _logger.LogInformation("Worker {workerId} completed job {id}", _workerId, job.Id);

            await using var endTransaction = await context.Database.BeginTransactionAsync(default);
            await UpdateJobData(context, job, message: null, default);
            await SaveJobLogs(context, logCollector);
            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error executing job {id}", job.Id);
            await using var endTransaction = await context.Database.BeginTransactionAsync(default);
            await UpdateJobData(context, job, e.Message, default);
            await SaveJobLogs(context, logCollector);
            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        finally
        {
            JobLogContext.Current = null;
        }

        return true;
    }

    private async Task ExecuteJob(Job job, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var messageType = Type.GetType(job.Type);
        if (messageType is null)
            throw new JoblyException($"Unknown type {job.Type}");

        var payload = JsonSerializer.Deserialize(job.Message, messageType);
        if (payload is null)
            throw new JoblyException($"Unable to deserialize message {job.Message} to type {job.Type}");

        var dispatcher = new JobDispatcher();

        if (job.HandlerType != null)
        {
            // Message-spawned job: execute specific handler
            var handlerType = Type.GetType(job.HandlerType);
            if (handlerType is null)
                throw new JoblyException($"Unknown handler type {job.HandlerType}");

            // Determine if this is a message handler or job handler based on MessageId
            if (job.MessageId != null)
            {
                await dispatcher.ExecuteMessageHandler(payload, messageType, handlerType, provider, cancellationToken);
            }
            else
            {
                await dispatcher.ExecuteJobHandler(payload, messageType, handlerType, provider, cancellationToken);
            }
            return;
        }

        // Direct IJob: discover single handler
        var jobHandlerType = dispatcher.DiscoverJobHandler(messageType, provider);
        if (jobHandlerType is null)
            throw new JoblyException($"No handler registered for {messageType.Name}");

        job.HandlerType = jobHandlerType.AssemblyQualifiedName;
        await dispatcher.ExecuteJobHandler(payload, messageType, jobHandlerType, provider, cancellationToken);
    }

    // ==================== Shared Logic ====================

    private void UpdateJobStatusToProcessing(TContext context, Job job)
    {
        job.CurrentState = State.Processing;
        job.CurrentWorkerId = _workerId;

        context.Set<JobLog>().Add(new JobLog
        {
            JobId = job.Id,
            EventType = "Processing",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = $"The job {job.Id} is being processed"
        });
    }

    private async Task CreateNextJob(TContext context, Job job, CancellationToken cancellationToken)
    {
        var recurringJob = await context.Set<RecurringJob>()
            .Where(x => x.Id == job.RecurringJobId)
            .FirstAsync(cancellationToken);

        if (recurringJob.NextJobId != job.Id)
            return;

        var fromUtc = DateTime.SpecifyKind(recurringJob.NextExecution ?? DateTime.UtcNow, DateTimeKind.Utc);
        var nextJobScheduleTime = CronExpression.Parse(recurringJob.Cron).GetNextOccurrence(fromUtc);

        var newJob = JobHelper.CreateJob(
            message: recurringJob.Message,
            type: recurringJob.Type,
            retries: 0,
            scheduleTime: nextJobScheduleTime,
            maxRetries: 0,
            queue: job.Queue,
            parentId: null,
            recurringJobId: recurringJob.Id,
            state: State.Enqueued);

        recurringJob.LastExecution = recurringJob.NextExecution;
        recurringJob.LastJobId = recurringJob.NextJobId;
        recurringJob.NextExecution = nextJobScheduleTime;

        context.Set<Job>().Add(newJob);
        context.Set<JobLog>().Add(new JobLog
        {
            JobId = newJob.Id,
            EventType = "Created",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = $"Job {newJob.Id} created for recurring job {recurringJob.Id}"
        });
        recurringJob.NextJobId = newJob.Id;
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

        // Set expiration and increment statistics (total + hourly)
        var hourSuffix = DateTime.UtcNow.ToString("yyyy-MM-dd-HH");
        if (state == State.Completed)
        {
            job.ExpireAt = DateTime.UtcNow.AddDays(1);
            await IncrementStatistic(context, "stats:succeeded");
            await IncrementStatistic(context, $"stats:succeeded:{hourSuffix}");
        }
        else if (state == State.Failed && job.RetriedTimes >= job.MaxRetries)
        {
            // Only count as failed when retries are exhausted (not on re-enqueue)
            await IncrementStatistic(context, "stats:failed");
            await IncrementStatistic(context, $"stats:failed:{hourSuffix}");
        }

        // Check for child job continuations
        var isParent = await context.Set<Job>()
            .Where(x => x.ParentJobId == job.Id)
            .AnyAsync(cancellationToken);

        if (job.CurrentState == State.Completed && isParent)
        {
            await UpdateChildJobs(context, job.Id, cancellationToken);
        }

        // Check for batch completion
        if (job.BatchId != null)
        {
            await UpdateCurrentAndNextBatchFromChildJob(context, job.BatchId.Value, cancellationToken);
        }

        // Check for message completion (all jobs for a message done)
        if (job.MessageId != null && job.CurrentState == State.Completed)
        {
            await UpdateMessageJobCount(context, job.MessageId.Value, cancellationToken);
        }

        await CreateJobLog(context, job.Id, state, string.IsNullOrEmpty(message) ? $"Job {job.Id} is completed" : message, cancellationToken);
    }

    private static async Task UpdateMessageJobCount(TContext context, Guid messageId, CancellationToken cancellationToken)
    {
        var msg = await context.Set<Message>()
            .Where(x => x.Id == messageId)
            .FirstAsync(cancellationToken);

        msg.JobCount--;

        if (msg.JobCount <= 0)
        {
            msg.JobCount = 0;
            msg.CurrentState = State.Completed;
            msg.ExpireAt = DateTime.UtcNow.AddDays(1);
        }
    }

    private static async Task SaveJobLogs(TContext context, JobLogCollector collector)
    {
        if (collector.Entries.Count == 0) return;
        await context.Set<JobLog>().AddRangeAsync(collector.Entries);
    }

    private static async Task CreateJobLog(TContext context, Guid jobId, State state, string? message, CancellationToken cancellationToken)
    {
        var eventType = state switch
        {
            State.Completed => "Completed",
            State.Failed => "Failed",
            State.Enqueued => "Requeued",
            State.Processing => "Processing",
            _ => state.ToString()
        };

        var level = state == State.Failed ? "Error" : "Information";

        await context.Set<JobLog>().AddAsync(new JobLog
        {
            JobId = jobId,
            EventType = eventType,
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message ?? string.Empty
        }, cancellationToken);
    }

    private static async Task UpdateChildJobs(TContext context, Guid parentJobId, CancellationToken cancellationToken)
    {
        var childJobs = await context.Set<Job>()
            .Where(x => x.ParentJobId == parentJobId)
            .Where(x => x.CurrentState == State.Awaiting)
            .ToListAsync(cancellationToken);

        foreach (var childJob in childJobs)
        {
            var batch = await context.Set<Batch>()
                .Where(x => x.Id == childJob.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (batch != null)
            {
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

        if (currentBatch == null)
            return;

        currentBatch.Counter--;

        if (currentBatch.Counter > 0)
            return;

        currentBatch.Counter = 0;

        var currentBatchJob = await context.Set<Job>()
            .Where(x => x.Id == currentBatch.Id)
            .FirstAsync(cancellationToken);

        currentBatchJob.CurrentState = State.Completed;

        var nextBatchJob = await context.Set<Job>()
            .Where(x => x.ParentJobId == currentBatchJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextBatchJob == null)
            return;

        var nextBatch = await context.Set<Batch>()
            .Where(x => x.Id == nextBatchJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

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
        else
        {
            nextBatchJob.CurrentState = State.Enqueued;
        }
    }

    private static async Task IncrementStatistic(TContext context, string key)
    {
        var updated = await context.Set<Statistic>()
            .Where(x => x.Key == key)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Value, p => p.Value + 1));

        if (updated == 0)
        {
            try
            {
                await context.Set<Statistic>().AddAsync(new Statistic { Key = key, Value = 1 });
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Race: another worker inserted first — retry update
                await context.Set<Statistic>()
                    .Where(x => x.Key == key)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.Value, p => p.Value + 1));
            }
        }
    }
}
