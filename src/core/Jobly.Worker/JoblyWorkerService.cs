using System.Diagnostics;
using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Logging;
using Jobly.Core.Notifications;
using Jobly.Worker.Services;
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
    private readonly IJoblyNotificationTransport _notificationTransport;
    private readonly IJoblySqlQueries<TContext> _sqlQueries;

    public JoblyWorkerService(Guid workerId, IServiceScopeFactory serviceScopeFactory, ILogger<JoblyWorkerService<TContext>> logger, IOptions<JoblyWorkerConfiguration> configuration, WorkerGroupConfiguration groupConfiguration, TimeProvider timeProvider, IJoblySqlQueries<TContext> sqlQueries, IJoblyNotificationTransport notificationTransport)
    {
        _workerId = workerId;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _configuration = configuration.Value;
        _groupConfiguration = groupConfiguration;
        _timeProvider = timeProvider;
        _sqlQueries = sqlQueries;
        _notificationTransport = notificationTransport;
    }

    public async Task<bool> GetAndProcessJob(CancellationToken cancellationToken)
    {
        PerfTrace.Begin();

        // Worker scope — owns Jobly state (Job, JobLog, Counter). Isolated from handler's DbContext.
        using var workerScope = _serviceScopeFactory.CreateScope();
        var workerContext = workerScope.ServiceProvider.GetRequiredService<TContext>();

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Atomic claim: UPDATE ... RETURNING (PG) / UPDATE ... OUTPUT (MSSQL) with
        // FOR UPDATE SKIP LOCKED / ROWLOCK+UPDLOCK+READPAST baked into the SQL. No SELECT→UPDATE
        // window, no dependency on a regex-rewriting interceptor. Concurrent workers across
        // servers get distinct rows or nothing at all — never the same row.
        PerfTrace.Mark(PerfTrace.FetchJob);
        var claimed = await _sqlQueries.ClaimEnqueuedJobsAsync(
            workerContext,
            _groupConfiguration.Queues,
            _workerId,
            now,
            limit: 1,
            cancellationToken);

        if (claimed.Count == 0)
        {
            return false;
        }

        var job = claimed[0];
        _logger.LogInformation("Worker {workerId} fetched job {id}", _workerId, job.Id);

        // The claim itself is committed atomically via UPDATE RETURNING. Persist the Processing
        // log entry in a separate round-trip; failure here leaves the job Processing with
        // LastKeepAlive set, which is fine — the worker carries on and the log is cosmetic.
        workerContext.Set<JobLog>().Add(new JobLog
        {
            JobId = job.Id,
            EventType = "Processing",
            Timestamp = now,
            Level = "Information",
            Message = $"The job {job.Id} is being processed",
            WorkerId = _workerId,
        });

        PerfTrace.Mark(PerfTrace.SaveProcessing);
        await workerContext.SaveChangesAsync(cancellationToken);
        PerfTrace.Mark(PerfTrace.CommitTransaction1);

        var logCollector = new JobLogCollector { JobId = job.Id, TimeProvider = _timeProvider, WorkerId = _workerId };
        using var jobCts = new CancellationTokenSource();
        var monitorTask = RunJobMonitor(job.Id, logCollector, jobCts, cancellationToken);

        var activity = JoblyTelemetry.StartJobActivity(job.TraceId ?? job.Id, job.ParentSpanId);
        var jobTypeName = JoblyTelemetry.GetShortTypeName(job.Type);
        activity.SetTag("messaging.system", "jobly");
        activity.SetTag("messaging.operation.name", "process");
        activity.SetTag("messaging.destination.name", job.Queue);
        activity.SetTag("messaging.message.id", job.Id.ToString());
        activity.SetTag("jobly.job.type", jobTypeName);
        activity.SetTag("jobly.job.kind", job.Kind.ToString());
        JoblyTelemetry.JobsActive.Add(1, new KeyValuePair<string, object?>("queue", job.Queue));
        Stopwatch? handlerStopwatch = null;
        IServiceScope? handlerScope = null;
        JobContext? jobContext = null;
        try
        {
            PerfTrace.Mark(PerfTrace.ExecuteHandler);
            _logger.LogInformation("Worker {workerId} executing job {id}", _workerId, job.Id);

            if (_configuration.EnableHandlerLogging)
            {
                JobLogContext.Current = logCollector;
            }

            JobExecutionContext.Current = new JobExecutionInfo
            {
                JobId = job.Id,
                TraceId = job.TraceId ?? job.Id,
                MetadataJson = job.Metadata,
            };

            // Handler scope — isolated DbContext for handler + pipeline behaviors.
            // Handler's change tracker is disposed with this scope, never leaking into worker saves.
            handlerScope = _serviceScopeFactory.CreateScope();

            jobContext = handlerScope.ServiceProvider.GetRequiredService<JobContext>();
            jobContext.JobId = job.Id;
            jobContext.TraceId = job.TraceId ?? job.Id;
            jobContext.Metadata = MetadataSerializer.Deserialize(job.Metadata);

            handlerStopwatch = Stopwatch.StartNew();
            await ExecuteJob(job, handlerScope.ServiceProvider, jobCts.Token);
            handlerStopwatch.Stop();

            // Commit handler's work (outbox: published jobs + business entities) before disposing.
            // Capture pending push notifications for any child jobs the handler added, fire post-commit.
            var handlerContext = handlerScope.ServiceProvider.GetRequiredService<TContext>();
            var handlerPending = NotificationDispatch.CapturePending(handlerContext);
            await handlerContext.SaveChangesAsync(default);
            await NotificationDispatch.FireAsync(_notificationTransport, handlerPending, cancellationToken);

            // Read metadata and outcome from handler scope before disposing
            job.Metadata = JsonSerializer.Serialize(jobContext.Metadata);
            var successOutcome = jobContext.Outcome;
            handlerScope.Dispose();
            handlerScope = null;

            var durationMs = handlerStopwatch.Elapsed.TotalMilliseconds;

            if (successOutcome != null)
            {
                // Pipeline behavior short-circuited (e.g. mutex held)
                var outcomeStatus = successOutcome.State.ToString().ToLowerInvariant();
                activity.SetTag("jobly.job.status", outcomeStatus);
                activity.SetTag("jobly.job.duration_ms", durationMs);
                activity.AddEvent(new ActivityEvent($"jobly.job.{outcomeStatus}"));
                JoblyTelemetry.JobDuration.Record(durationMs, new KeyValuePair<string, object?>("queue", job.Queue), new KeyValuePair<string, object?>("type", jobTypeName), new KeyValuePair<string, object?>("status", outcomeStatus));
                JoblyTelemetry.JobsCompleted.Add(1, new KeyValuePair<string, object?>("queue", job.Queue), new KeyValuePair<string, object?>("type", jobTypeName), new KeyValuePair<string, object?>("status", outcomeStatus));
            }
            else
            {
                activity.SetTag("jobly.job.status", "succeeded");
                activity.SetTag("jobly.job.duration_ms", durationMs);
                activity.AddEvent(new ActivityEvent("jobly.job.completed", tags: new ActivityTagsCollection
                {
                    { "duration_ms", durationMs },
                }));
                JoblyTelemetry.JobDuration.Record(durationMs, new KeyValuePair<string, object?>("queue", job.Queue), new KeyValuePair<string, object?>("type", jobTypeName), new KeyValuePair<string, object?>("status", "succeeded"));
                JoblyTelemetry.JobsCompleted.Add(1, new KeyValuePair<string, object?>("queue", job.Queue), new KeyValuePair<string, object?>("type", jobTypeName), new KeyValuePair<string, object?>("status", "succeeded"));
            }

            JobLogContext.Current = null;
            JobExecutionContext.Current = null;

            _logger.LogInformation("Worker {workerId} completed job {id}", _workerId, job.Id);

            PerfTrace.Mark(PerfTrace.CancelKeepAlive);
            await jobCts.CancelAsync();
            await monitorTask;

            PerfTrace.Mark(PerfTrace.BeginTransaction2);
            await using var endTransaction = await workerContext.Database.BeginTransactionAsync(default);

            if (successOutcome != null)
            {
                job.CurrentState = successOutcome.State;
                if (successOutcome.ClearHandlerType)
                {
                    job.HandlerType = null;
                }

                if (successOutcome.ScheduleTime != null)
                {
                    job.ScheduleTime = successOutcome.ScheduleTime.Value;
                }
            }
            else
            {
                job.CurrentState = State.Completed;
            }

            FinalizeJobState(workerContext, job, null, handlerStopwatch.Elapsed.TotalMilliseconds, successOutcome);
            if (_configuration.EnableHandlerLogging)
            {
                await SaveJobLogs(workerContext, logCollector);
            }

            PerfTrace.Mark(PerfTrace.SaveCompleted);
            await workerContext.SaveChangesAsync(default);

            PerfTrace.Mark(PerfTrace.CommitTransaction2);
            await endTransaction.CommitAsync(default);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Job was cancelled (deleted while running) — dispose handler scope first
            handlerScope?.Dispose();
            handlerScope = null;

            handlerStopwatch?.Stop();
            activity.SetTag("jobly.job.status", "cancelled");
            activity.AddEvent(new ActivityEvent("jobly.job.cancelled"));
            JoblyTelemetry.JobsCompleted.Add(1, new KeyValuePair<string, object?>("queue", job.Queue), new KeyValuePair<string, object?>("type", jobTypeName), new KeyValuePair<string, object?>("status", "cancelled"));
            JobLogContext.Current = null;
            JobExecutionContext.Current = null;
            _logger.LogInformation("Job {id} was cancelled", job.Id);
            await monitorTask;

            var cancelNow = _timeProvider.GetUtcNow().UtcDateTime;
            await using var endTransaction = await workerContext.Database.BeginTransactionAsync(default);
            job.CurrentState = State.Deleted;
            job.ExpireAt = cancelNow.Add(_configuration.JobExpirationTimeout);
            job.CancellationMode = CancellationMode.None;
            job.CurrentWorkerId = null;
            job.LastKeepAlive = null;
            workerContext.Set<Counter>().Add(new Counter { Key = "stats:deleted", Value = 1 });
            workerContext.Set<JobLog>().Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Cancelled",
                Timestamp = cancelNow,
                Level = "Information",
                Message = "Job was cancelled by user",
                DurationMs = handlerStopwatch?.Elapsed.TotalMilliseconds,
                WorkerId = _workerId,
            });
            if (_configuration.EnableHandlerLogging)
            {
                await SaveJobLogs(workerContext, logCollector);
            }

            await workerContext.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        catch (Exception e)
        {
            handlerStopwatch?.Stop();
            var errorDurationMs = handlerStopwatch?.Elapsed.TotalMilliseconds;

            // Read pipeline outcome from handler scope before disposing
            var outcome = jobContext?.Outcome;
            if (outcome != null)
            {
                job.CurrentState = outcome.State;
                if (outcome.ClearHandlerType)
                {
                    job.HandlerType = null;
                }

                if (outcome.ScheduleTime != null)
                {
                    job.ScheduleTime = outcome.ScheduleTime.Value;
                }

                job.Metadata = JsonSerializer.Serialize(jobContext!.Metadata);
            }
            else
            {
                job.CurrentState = State.Failed;
            }

            handlerScope?.Dispose();
            handlerScope = null;

            var willRetry = job.CurrentState == State.Enqueued;
            var errorStatus = willRetry ? "retried" : "failed";
            activity.SetStatus(ActivityStatusCode.Error, Truncate(e.Message, 256));
            activity.SetTag("jobly.job.status", errorStatus);

            if (willRetry)
            {
                activity.AddEvent(new ActivityEvent("jobly.job.retried"));
            }
            else
            {
                activity.AddEvent(new ActivityEvent("jobly.job.failed", tags: new ActivityTagsCollection
                {
                    { "exception.type", e.GetType().FullName },
                    { "exception.message", e.Message },
                }));
            }

            JoblyTelemetry.JobDuration.Record(errorDurationMs ?? 0, new KeyValuePair<string, object?>("queue", job.Queue), new KeyValuePair<string, object?>("type", jobTypeName), new KeyValuePair<string, object?>("status", errorStatus));
            JoblyTelemetry.JobsCompleted.Add(1, new KeyValuePair<string, object?>("queue", job.Queue), new KeyValuePair<string, object?>("type", jobTypeName), new KeyValuePair<string, object?>("status", errorStatus));
            JobLogContext.Current = null;
            JobExecutionContext.Current = null;

            // Handler exceptions (including intentional test-case throws) are logged at the
            // user's chosen level — Information is enough because the job state transition
            // is recorded separately and the exception message is stored in the JobLog.
            // Full stack traces at Error level during dense multi-server test scenarios produce
            // many MB of log output per CI run without adding diagnostic value.
            _logger.LogInformation("Error executing job {id}: {exceptionType}: {message}", job.Id, e.GetType().Name, e.Message);
            await jobCts.CancelAsync();
            await monitorTask;

            await using var endTransaction = await workerContext.Database.BeginTransactionAsync(default);
            FinalizeJobState(workerContext, job, e, errorDurationMs, outcome);
            if (_configuration.EnableHandlerLogging)
            {
                await SaveJobLogs(workerContext, logCollector);
            }

            await workerContext.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        finally
        {
            handlerScope?.Dispose();
            JoblyTelemetry.JobsActive.Add(-1, new KeyValuePair<string, object?>("queue", job.Queue));
            activity.Stop();
            activity.Dispose();
            JobLogContext.Current = null;
            JobExecutionContext.Current = null;
        }

        // Signal orchestrator — this job may have a parent that needs finalization,
        // or children that need activation. Local signal wakes the in-process task; the
        // push notification fans out to other servers' OrchestrationTask via the listener.
        OrchestrationTask<TContext>.SignalOrchestrator();
        await NotificationDispatch.FireAsync(
            _notificationTransport,
            [new Notification(NotificationKind.JobFinalized, null)],
            cancellationToken);

        PerfTrace.Mark(PerfTrace.Done);
        PerfTrace.End();

        return true;
    }

    private static async Task ExecuteJob(Job job, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var messageType = Type.GetType(job.Type!) ?? throw new JoblyException($"Unknown type {job.Type}");
        var payload = JsonSerializer.Deserialize(job.Message!, messageType) ?? throw new JoblyException($"Unable to deserialize message {job.Message} to type {job.Type}");

        var jobContext = provider.GetRequiredService<JobContext>();

        if (job.HandlerType != null)
        {
            var handlerType = Type.GetType(job.HandlerType) ?? throw new JoblyException($"Unknown handler type {job.HandlerType}");
            jobContext.HandlerType = handlerType;
            await JobDispatcher.ExecuteHandler(payload, messageType, handlerType, provider, cancellationToken);

            return;
        }

        var jobHandlerType = JobDispatcher.DiscoverJobHandler(messageType, provider) ?? throw new JoblyException($"No handler registered for {messageType.Name}");
        job.HandlerType = jobHandlerType.AssemblyQualifiedName;
        jobContext.HandlerType = jobHandlerType;
        await JobDispatcher.ExecuteJobHandler(payload, messageType, jobHandlerType, provider, cancellationToken);
    }

    private async Task RunJobMonitor(Guid jobId, JobLogCollector logCollector, CancellationTokenSource jobCts, CancellationToken stoppingToken)
    {
        var logFlushInterval = _configuration.LogFlushInterval;
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
    /// Finalizes job state: clears worker fields, adds counters and log entry.
    /// State must be set on the job before calling this method.
    /// </summary>
    private void FinalizeJobState(TContext context, Job job, Exception? error, double? durationMs, JobOutcome? outcome = null)
    {
        var state = job.CurrentState;
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
        else if (state == State.Failed)
        {
            AddCounters(context, "stats:failed", $"stats:failed:{hourSuffix}");
        }
        else if (state == State.Deleted)
        {
            job.ExpireAt = now.Add(_configuration.JobExpirationTimeout);
            AddCounters(context, "stats:deleted", $"stats:deleted:{hourSuffix}");
        }

        var logMessage = outcome?.LogMessage
            ?? (error != null ? error.Message : $"Job {job.Id} completed");
        var logException = error?.ToString();

        // Both Enqueued (immediate retry) and Scheduled (delayed retry) log as "Requeued" —
        // operators reading the dashboard think in terms of "the job was retried", not its
        // transient EF-state spelling.
        var eventType = state switch
        {
            State.Completed => "Completed",
            State.Failed => "Failed",
            State.Enqueued or State.Scheduled => "Requeued",
            State.Deleted => "Deleted",
            _ => state.ToString(),
        };

        // Retries apply jitter to ScheduleTime; recording it here so operators debugging a
        // retry storm can see the actual delay applied (otherwise the factor is invisible).
        if (state == State.Enqueued || state == State.Scheduled)
        {
            var scheduledAt = job.ScheduleTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            logMessage = $"{logMessage} (next attempt scheduled: {scheduledAt})";
        }

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

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
