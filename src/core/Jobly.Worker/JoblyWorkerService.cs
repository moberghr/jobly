using System.Diagnostics;
using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.Interceptors;
using Jobly.Core.Logging;
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
    private readonly WorkerGroupConfiguration _groupConfiguration;

    public JoblyWorkerService(Guid workerId, IServiceScopeFactory serviceScopeFactory, ILogger<JoblyWorkerService<TContext>> logger, IOptions<JoblyWorkerConfiguration> configuration, WorkerGroupConfiguration groupConfiguration)
    {
        _workerId = workerId;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _configuration = configuration.Value;
        _groupConfiguration = groupConfiguration;
    }

    public async Task<bool> GetAndProcessJob(CancellationToken cancellationToken)
    {
        // Prefer executing Jobs (real work) over routing Messages
        if (await TryExecuteJob(cancellationToken))
        {
            return true;
        }

        if (await TryRouteMessage(cancellationToken))
        {
            return true;
        }

        return false;
    }

    private async Task<bool> TryRouteMessage(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var message = await context.Set<Message>()
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => _groupConfiguration.Queues.Contains(x.Queue))
            .OrderBy(x => x.Queue)
            .ThenBy(x => x.CreateTime)
            .TagWith(InterceptorConstants.RowLockTableMessage)
            .FirstOrDefaultAsync(cancellationToken);

        if (message == null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        _logger.LogInformation("Worker {workerId} routing message {id}", _workerId, message.Id);

        var messageType = Type.GetType(message.Type) ?? throw new JoblyException($"Unknown type {message.Type}");
        var handlerTypes = JobDispatcher.DiscoverMessageHandlers(messageType, scope.ServiceProvider);

        if (handlerTypes.Count == 0)
        {
            message.CurrentState = State.Failed;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new JoblyException($"No handler registered for {messageType.Name}");
        }

        // Create a Job for each handler — all share a TraceId
        var messageTraceId = Guid.NewGuid();
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
            job.TraceId = messageTraceId;

            context.Set<Job>().Add(job);
            context.Set<JobLog>().Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Created",
                Timestamp = DateTime.UtcNow,
                Level = "Information",
                Message = $"Job {job.Id} created from message {message.Id}",
            });
        }

        message.JobCount = handlerTypes.Count;
        message.CurrentState = State.Processing;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Worker {workerId} routed message {id} to {count} jobs", _workerId, message.Id, handlerTypes.Count);

        // Immediately try to execute one of the newly created jobs
        return await TryExecuteJob(cancellationToken);
    }

    private async Task<bool> TryExecuteJob(CancellationToken cancellationToken)
    {
        PerfTrace.Begin();

        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        PerfTrace.Mark(PerfTrace.BeginTransaction1);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        PerfTrace.Mark(PerfTrace.FetchJob);
        var job = await context.Set<Job>()
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => x.ScheduleTime < DateTime.UtcNow)
            .Where(x => _groupConfiguration.Queues.Contains(x.Queue))
            .OrderBy(x => x.Queue)
            .ThenBy(x => x.ScheduleTime)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .FirstOrDefaultAsync(cancellationToken);

        if (job == null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        _logger.LogInformation("Worker {workerId} fetched job {id}", _workerId, job.Id);

        UpdateJobStatusToProcessing(context, job);

        PerfTrace.Mark(PerfTrace.SaveProcessing);
        await context.SaveChangesAsync(cancellationToken);

        PerfTrace.Mark(PerfTrace.CommitTransaction1);
        await transaction.CommitAsync(cancellationToken);

        var logCollector = new JobLogCollector { JobId = job.Id };

        // Start keep-alive background loop
        using var keepAliveCts = new CancellationTokenSource();
        var keepAliveTask = RunKeepAlive(job.Id, keepAliveCts.Token);

        Stopwatch? handlerStopwatch = null;
        try
        {
            PerfTrace.Mark(PerfTrace.ExecuteHandler);
            _logger.LogInformation("Worker {workerId} executing job {id}", _workerId, job.Id);

            // Enable log capture only during handler execution
            JobLogContext.Current = logCollector;
            JobExecutionContext.Current = new JobExecutionInfo
            {
                JobId = job.Id,
                TraceId = job.TraceId ?? job.Id,
            };

            handlerStopwatch = Stopwatch.StartNew();
            await ExecuteJob(job, scope.ServiceProvider, cancellationToken);
            handlerStopwatch.Stop();

            JobLogContext.Current = null;
            JobExecutionContext.Current = null;

            _logger.LogInformation("Worker {workerId} completed job {id}", _workerId, job.Id);

            PerfTrace.Mark(PerfTrace.CancelKeepAlive);
            await keepAliveCts.CancelAsync();
            await keepAliveTask;

            PerfTrace.Mark(PerfTrace.BeginTransaction2);
            await using var endTransaction = await context.Database.BeginTransactionAsync(default);

            await UpdateJobData(context, job, null, handlerStopwatch.Elapsed.TotalMilliseconds, default);
            await SaveJobLogs(context, logCollector);

            PerfTrace.Mark(PerfTrace.SaveCompleted);
            await context.SaveChangesAsync(default);

            PerfTrace.Mark(PerfTrace.CommitTransaction2);
            await endTransaction.CommitAsync(default);
        }
        catch (Exception e)
        {
            handlerStopwatch?.Stop();
            JobLogContext.Current = null;
            JobExecutionContext.Current = null;
            _logger.LogError(e, "Error executing job {id}", job.Id);
            await keepAliveCts.CancelAsync();
            await keepAliveTask;

            await using var endTransaction = await context.Database.BeginTransactionAsync(default);
            await UpdateJobData(context, job, e, handlerStopwatch?.Elapsed.TotalMilliseconds, default);
            await SaveJobLogs(context, logCollector);
            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        finally
        {
            JobLogContext.Current = null;
            JobExecutionContext.Current = null;
        }

        PerfTrace.Mark(PerfTrace.Done);
        PerfTrace.End();

        return true;
    }

    private static async Task ExecuteJob(Job job, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var messageType = Type.GetType(job.Type) ?? throw new JoblyException($"Unknown type {job.Type}");
        var payload = JsonSerializer.Deserialize(job.Message, messageType) ?? throw new JoblyException($"Unable to deserialize message {job.Message} to type {job.Type}");
        if (job.HandlerType != null)
        {
            // Message-spawned job: execute specific handler
            var handlerType = Type.GetType(job.HandlerType) ?? throw new JoblyException($"Unknown handler type {job.HandlerType}");

            // Determine if this is a message handler or job handler based on MessageId
            if (job.MessageId != null)
            {
                await JobDispatcher.ExecuteMessageHandler(payload, messageType, handlerType, provider, cancellationToken);
            }
            else
            {
                await JobDispatcher.ExecuteJobHandler(payload, messageType, handlerType, provider, cancellationToken);
            }

            return;
        }

        // Direct IJob: discover single handler
        var jobHandlerType = JobDispatcher.DiscoverJobHandler(messageType, provider) ?? throw new JoblyException($"No handler registered for {messageType.Name}");
        job.HandlerType = jobHandlerType.AssemblyQualifiedName;
        await JobDispatcher.ExecuteJobHandler(payload, messageType, jobHandlerType, provider, cancellationToken);
    }

    private void UpdateJobStatusToProcessing(TContext context, Job job)
    {
        job.CurrentState = State.Processing;
        job.CurrentWorkerId = _workerId;
        job.LastKeepAlive = DateTime.UtcNow;

        context.Set<JobLog>().Add(new JobLog
        {
            JobId = job.Id,
            EventType = "Processing",
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = $"The job {job.Id} is being processed",
        });
    }

    private async Task RunKeepAlive(Guid jobId, CancellationToken ct)
    {
        var interval = _configuration.InvisibilityTimeout / 5;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                await context.Set<Job>()
                    .Where(x => x.Id == jobId)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastKeepAlive, DateTime.UtcNow), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to refresh keep-alive for job {jobId}", jobId);
            }
        }
    }

    private static async Task UpdateJobData(TContext context, Job job, Exception? error, double? durationMs, CancellationToken cancellationToken)
    {
        var failed = error != null;
        var state = failed ? State.Failed : State.Completed;
        if (job.RetriedTimes < job.MaxRetries && failed)
        {
            state = State.Enqueued;
            job.RetriedTimes += 1;
            job.HandlerType = null; // Clear so handler is re-discovered on retry
        }

        job.CurrentState = state;
        job.CurrentWorkerId = null;
        job.LastKeepAlive = null;

        // Set expiration and increment statistics (total + hourly)
        PerfTrace.Mark(PerfTrace.IncrementStats);
        var hourSuffix = DateTime.UtcNow.ToString("yyyy-MM-dd-HH");
        if (state == State.Completed)
        {
            job.ExpireAt = DateTime.UtcNow.AddDays(1);
            AddCounters(context, "stats:succeeded", $"stats:succeeded:{hourSuffix}");
        }
        else if (state == State.Failed && job.RetriedTimes >= job.MaxRetries)
        {
            AddCounters(context, "stats:failed", $"stats:failed:{hourSuffix}");
        }

        // Check for child job continuations — skip DB query if job can't have children
        PerfTrace.Mark(PerfTrace.CheckChildren);
        if (job.CurrentState == State.Completed)
        {
            var isParent = await context.Set<Job>()
                .Where(x => x.ParentJobId == job.Id)
                .AnyAsync(cancellationToken);

            if (isParent)
            {
                await UpdateChildJobs(context, job.Id, cancellationToken);
            }
        }

        PerfTrace.Mark(PerfTrace.CheckBatchMessage);
        if (job.BatchId != null && (state == State.Completed || state == State.Failed))
        {
            await UpdateCurrentAndNextBatchFromChildJob(context, job.BatchId.Value, cancellationToken);
        }

        if (job.MessageId != null && job.CurrentState == State.Completed)
        {
            await UpdateMessageJobCount(context, job.MessageId.Value, cancellationToken);
        }

        var logMessage = error != null ? error.Message : $"Job {job.Id} completed";
        var logException = error?.ToString(); // Full stack trace

        await CreateJobLog(context, job.Id, state, logMessage, durationMs, cancellationToken, logException);
    }

    private static async Task UpdateMessageJobCount(TContext context, Guid messageId, CancellationToken cancellationToken)
    {
        var msg = await context.Set<Message>()
            .Where(x => x.Id == messageId)
            .TagWith(InterceptorConstants.RowLockTableMessageWait)
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
        if (collector.Entries.Count == 0)
        {
            return;
        }

        await context.Set<JobLog>().AddRangeAsync(collector.Entries);
    }

    private static async Task CreateJobLog(TContext context, Guid jobId, State state, string? message, double? durationMs, CancellationToken cancellationToken, string? exception = null)
    {
        var eventType = state switch
        {
            State.Completed => "Completed",
            State.Failed => "Failed",
            State.Enqueued => "Requeued",
            State.Processing => "Processing",
            _ => state.ToString(),
        };

        var level = state == State.Failed ? "Error" : "Information";

        await context.Set<JobLog>().AddAsync(
            new JobLog
            {
                JobId = jobId,
                EventType = eventType,
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message ?? string.Empty,
                Exception = exception,
                DurationMs = durationMs,
            },
            cancellationToken);
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
        {
            return;
        }

        currentBatch.JobCount--;

        if (currentBatch.JobCount > 0)
        {
            return;
        }

        currentBatch.JobCount = 0;

        // All jobs finished — check if continuation should fire
        var currentBatchJob = await context.Set<Job>()
            .Where(x => x.Id == currentBatch.Id)
            .FirstAsync(cancellationToken);

        // Already completed (e.g., after a requeued batch job re-completes) — don't re-trigger
        if (currentBatchJob.CurrentState == State.Completed || currentBatchJob.CurrentState == State.Failed)
        {
            return;
        }

        if (currentBatch.ContinuationOptions == BatchContinuationOptions.OnlyOnSucceeded)
        {
            // Check if any batch jobs failed
            var hasFailedJobs = await context.Set<Job>()
                .Where(x => x.BatchId == batchId && x.CurrentState == State.Failed)
                .AnyAsync(cancellationToken);

            if (hasFailedJobs)
            {
                currentBatchJob.CurrentState = State.Failed;
                return;
            }
        }

        currentBatchJob.CurrentState = State.Completed;

        var nextBatchJob = await context.Set<Job>()
            .Where(x => x.ParentJobId == currentBatchJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextBatchJob == null)
        {
            return;
        }

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

    private static void AddCounters(TContext context, string totalKey, string hourlyKey)
    {
        context.Set<Counter>().Add(new Counter { Key = totalKey, Value = 1 });
        context.Set<Counter>().Add(new Counter { Key = hourlyKey, Value = 1 });
    }
}
