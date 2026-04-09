using System.Diagnostics;
using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Interceptors;
using Jobly.Core.Logging;
using Jobly.Worker.Services;
using Medallion.Threading;
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
    private readonly TimeProvider _timeProvider;
    private readonly IDistributedLockProvider _lockProvider;

    public JoblyWorkerService(Guid workerId, IServiceScopeFactory serviceScopeFactory, ILogger<JoblyWorkerService<TContext>> logger, IOptions<JoblyWorkerConfiguration> configuration, WorkerGroupConfiguration groupConfiguration, TimeProvider timeProvider, IDistributedLockProvider lockProvider)
    {
        _workerId = workerId;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _configuration = configuration.Value;
        _groupConfiguration = groupConfiguration;
        _timeProvider = timeProvider;
        _lockProvider = lockProvider;
    }

    public async Task<bool> GetAndProcessJob(CancellationToken cancellationToken)
    {
        PerfTrace.Begin();

        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        PerfTrace.Mark(PerfTrace.BeginTransaction1);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Fetch only Kind=Job (messages are routed by MessageRoutingTask)
        PerfTrace.Mark(PerfTrace.FetchJob);
        var job = await context.Set<Job>()
            .Where(x => x.Kind == JobKind.Job && x.CurrentState == State.Enqueued && x.ScheduleTime < now)
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

        job.CurrentState = State.Processing;
        job.CurrentWorkerId = _workerId;
        job.LastKeepAlive = now;

        context.Set<JobLog>().Add(new JobLog
        {
            JobId = job.Id,
            EventType = "Processing",
            Timestamp = now,
            Level = "Information",
            Message = $"The job {job.Id} is being processed",
            WorkerId = _workerId,
        });

        PerfTrace.Mark(PerfTrace.SaveProcessing);
        await context.SaveChangesAsync(cancellationToken);

        PerfTrace.Mark(PerfTrace.CommitTransaction1);
        await transaction.CommitAsync(cancellationToken);

        IAsyncDisposable? mutexHandleToRelease = null;

        // Mutex check: use distributed lock to ensure only one job per ConcurrencyKey is Processing.
        // Acquired AFTER commit so the Processing state is visible to other workers.
        if (job.ConcurrencyKey != null)
        {
            var mutexLock = _lockProvider.CreateLock($"jobly:mutex:{job.ConcurrencyKey}");
            var mutexHandle = await mutexLock.TryAcquireAsync(timeout: TimeSpan.Zero, cancellationToken);

            if (mutexHandle == null)
            {
                // Another worker holds this mutex — cancel this job
                await using var cancelTx = await context.Database.BeginTransactionAsync(cancellationToken);
                job.CurrentState = State.Deleted;
                job.ExpireAt = now.Add(_configuration.JobExpirationTimeout);
                job.CurrentWorkerId = null;
                job.LastKeepAlive = null;
                context.Set<Counter>().Add(new Counter { Key = "stats:deleted", Value = 1 });
                context.Set<JobLog>().Add(new JobLog
                {
                    JobId = job.Id,
                    EventType = "Deleted",
                    Timestamp = now,
                    Level = "Information",
                    Message = $"Cancelled — mutex '{job.ConcurrencyKey}' held by another job",
                    WorkerId = _workerId,
                });
                await context.SaveChangesAsync(cancellationToken);
                await cancelTx.CommitAsync(cancellationToken);
                return true;
            }

            // Lock acquired — will be held until handler completes (released in finally below)
            // Store handle to release after execution
            mutexHandleToRelease = mutexHandle;
        }

        var logCollector = new JobLogCollector { JobId = job.Id, TimeProvider = _timeProvider, WorkerId = _workerId };
        using var jobCts = new CancellationTokenSource();
        var monitorTask = RunJobMonitor(job.Id, logCollector, jobCts, cancellationToken);

        Stopwatch? handlerStopwatch = null;
        try
        {
            PerfTrace.Mark(PerfTrace.ExecuteHandler);
            _logger.LogInformation("Worker {workerId} executing job {id}", _workerId, job.Id);

            JobLogContext.Current = logCollector;
            JobExecutionContext.Current = new JobExecutionInfo
            {
                JobId = job.Id,
                TraceId = job.TraceId ?? job.Id,
                MetadataJson = job.Metadata,
            };

            var jobContext = scope.ServiceProvider.GetRequiredService<JobContext>();
            jobContext.JobId = job.Id;
            jobContext.TraceId = job.TraceId ?? job.Id;
            jobContext.Metadata = job.Metadata != null
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(job.Metadata) ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();

            handlerStopwatch = Stopwatch.StartNew();
            await ExecuteJob(job, scope.ServiceProvider, jobCts.Token);
            handlerStopwatch.Stop();

            JobLogContext.Current = null;
            JobExecutionContext.Current = null;

            _logger.LogInformation("Worker {workerId} completed job {id}", _workerId, job.Id);

            PerfTrace.Mark(PerfTrace.CancelKeepAlive);
            await jobCts.CancelAsync();
            await monitorTask;

            PerfTrace.Mark(PerfTrace.BeginTransaction2);
            await using var endTransaction = await context.Database.BeginTransactionAsync(default);
            UpdateJobState(context, job, null, handlerStopwatch.Elapsed.TotalMilliseconds);
            await SaveJobLogs(context, logCollector);

            PerfTrace.Mark(PerfTrace.SaveCompleted);
            await context.SaveChangesAsync(default);

            PerfTrace.Mark(PerfTrace.CommitTransaction2);
            await endTransaction.CommitAsync(default);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Job was cancelled (deleted while running)
            handlerStopwatch?.Stop();
            JobLogContext.Current = null;
            JobExecutionContext.Current = null;
            _logger.LogInformation("Job {id} was cancelled", job.Id);
            await monitorTask;

            var cancelNow = _timeProvider.GetUtcNow().UtcDateTime;
            await using var endTransaction = await context.Database.BeginTransactionAsync(default);
            job.CurrentState = State.Deleted;
            job.ExpireAt = cancelNow.Add(_configuration.JobExpirationTimeout);
            job.CancellationMode = CancellationMode.None;
            job.CurrentWorkerId = null;
            job.LastKeepAlive = null;
            context.Set<Counter>().Add(new Counter { Key = "stats:deleted", Value = 1 });
            context.Set<JobLog>().Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Cancelled",
                Timestamp = cancelNow,
                Level = "Information",
                Message = "Job was cancelled by user",
                DurationMs = handlerStopwatch?.Elapsed.TotalMilliseconds,
                WorkerId = _workerId,
            });
            await SaveJobLogs(context, logCollector);
            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        catch (Exception e)
        {
            handlerStopwatch?.Stop();
            JobLogContext.Current = null;
            JobExecutionContext.Current = null;
            _logger.LogError(e, "Error executing job {id}", job.Id);
            await jobCts.CancelAsync();
            await monitorTask;

            await using var endTransaction = await context.Database.BeginTransactionAsync(default);
            UpdateJobState(context, job, e, handlerStopwatch?.Elapsed.TotalMilliseconds);
            await SaveJobLogs(context, logCollector);
            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        finally
        {
            JobLogContext.Current = null;
            JobExecutionContext.Current = null;

            if (mutexHandleToRelease != null)
            {
                await mutexHandleToRelease.DisposeAsync();
            }
        }

        // Signal orchestrator — this job may have a parent that needs finalization,
        // or children that need activation
        OrchestrationTask<TContext>.SignalOrchestrator();

        PerfTrace.Mark(PerfTrace.Done);
        PerfTrace.End();

        return true;
    }

    private static async Task ExecuteJob(Job job, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var messageType = Type.GetType(job.Type!) ?? throw new JoblyException($"Unknown type {job.Type}");
        var payload = JsonSerializer.Deserialize(job.Message!, messageType) ?? throw new JoblyException($"Unable to deserialize message {job.Message} to type {job.Type}");

        if (job.HandlerType != null)
        {
            var handlerType = Type.GetType(job.HandlerType) ?? throw new JoblyException($"Unknown handler type {job.HandlerType}");
            await JobDispatcher.ExecuteHandler(payload, messageType, handlerType, provider, cancellationToken);
            return;
        }

        var jobHandlerType = JobDispatcher.DiscoverJobHandler(messageType, provider) ?? throw new JoblyException($"No handler registered for {messageType.Name}");
        job.HandlerType = jobHandlerType.AssemblyQualifiedName;
        await JobDispatcher.ExecuteJobHandler(payload, messageType, jobHandlerType, provider, cancellationToken);
    }

    private async Task RunJobMonitor(Guid jobId, JobLogCollector logCollector, CancellationTokenSource jobCts, CancellationToken stoppingToken)
    {
        var logFlushInterval = TimeSpan.FromSeconds(1);
        var cancellationCheckInterval = _configuration.CancellationCheckInterval;
        var tickInterval = logFlushInterval < cancellationCheckInterval ? logFlushInterval : cancellationCheckInterval;
        var timeSinceLastCheck = TimeSpan.Zero;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, jobCts.Token);
        while (!linked.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(tickInterval, linked.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            timeSinceLastCheck += tickInterval;

            try
            {
                var pendingLogs = logCollector.Drain();
                var doCancellationCheck = timeSinceLastCheck >= cancellationCheckInterval;

                if (pendingLogs.Count == 0 && !doCancellationCheck)
                {
                    continue;
                }

                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TContext>();

                if (doCancellationCheck)
                {
                    timeSinceLastCheck = TimeSpan.Zero;

                    var cancellationMode = await context.Set<Job>()
                        .Where(x => x.Id == jobId)
                        .Select(x => x.CancellationMode)
                        .FirstOrDefaultAsync(stoppingToken);

                    if (cancellationMode != CancellationMode.None)
                    {
                        _logger.LogInformation("Job {jobId} cancellation requested ({mode}), cancelling handler", jobId, cancellationMode);

                        // Flush any pending logs before cancelling — they were already drained from the queue
                        if (pendingLogs.Count > 0)
                        {
                            context.Set<JobLog>().AddRange(pendingLogs);
                            await context.SaveChangesAsync(stoppingToken);
                        }

                        await jobCts.CancelAsync();
                        return;
                    }

                    var now = _timeProvider.GetUtcNow().UtcDateTime;
                    await context.Set<Job>()
                        .Where(x => x.Id == jobId)
                        .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastKeepAlive, now), stoppingToken);
                }

                if (pendingLogs.Count > 0)
                {
                    context.Set<JobLog>().AddRange(pendingLogs);
                    await context.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed job monitor for {jobId}", jobId);
            }
        }
    }

    /// <summary>
    /// Updates job state, counters, and creates the completion log.
    /// Pure state update — no parent/child orchestration.
    /// </summary>
    private void UpdateJobState(TContext context, Job job, Exception? error, double? durationMs)
    {
        var failed = error != null;
        var state = failed ? State.Failed : State.Completed;
        if (job.RetriedTimes < job.MaxRetries && failed)
        {
            state = State.Enqueued;
            job.RetriedTimes += 1;
            job.HandlerType = null;
        }

        job.CurrentState = state;
        job.CancellationMode = CancellationMode.None;
        job.CurrentWorkerId = null;
        job.LastKeepAlive = null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var hourSuffix = now.ToString("yyyy-MM-dd-HH");
        if (state == State.Completed)
        {
            job.ExpireAt = now.Add(_configuration.JobExpirationTimeout);
            AddCounters(context, "stats:succeeded", $"stats:succeeded:{hourSuffix}");
        }
        else if (state == State.Failed && job.RetriedTimes >= job.MaxRetries)
        {
            AddCounters(context, "stats:failed", $"stats:failed:{hourSuffix}");
        }

        var logMessage = error != null ? error.Message : $"Job {job.Id} completed";
        var logException = error?.ToString();

        var eventType = state switch
        {
            State.Completed => "Completed",
            State.Failed => "Failed",
            State.Enqueued => "Requeued",
            _ => state.ToString(),
        };

        context.Set<JobLog>().Add(new JobLog
        {
            JobId = job.Id,
            EventType = eventType,
            Timestamp = now,
            Level = state == State.Failed ? "Error" : "Information",
            Message = logMessage,
            Exception = logException,
            DurationMs = durationMs,
            WorkerId = _workerId,
        });
    }

    private static async Task SaveJobLogs(TContext context, JobLogCollector collector)
    {
        var entries = collector.Drain();
        if (entries.Count == 0)
        {
            return;
        }

        await context.Set<JobLog>().AddRangeAsync(entries);
    }

    private static void AddCounters(TContext context, string totalKey, string hourlyKey)
    {
        context.Set<Counter>().Add(new Counter { Key = totalKey, Value = 1 });
        context.Set<Counter>().Add(new Counter { Key = hourlyKey, Value = 1 });
    }
}
