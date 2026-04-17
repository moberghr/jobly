using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Logging;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Worker that receives pre-fetched jobs from a dispatcher channel.
/// Pure executor — handles execution and completion only. Orchestration handled by OrchestrationTask.
/// Completions are buffered in a per-worker <see cref="CompletionBatch{TContext}"/> and flushed
/// as a single multi-row transaction when any of: size threshold, time threshold, idle, or shutdown fires.
/// </summary>
public class JoblyDispatcherWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly Guid _workerId;
    private readonly ChannelReader<Job> _jobReader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JoblyDispatcherWorker<TContext>> _logger;
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly CompletionBatch<TContext> _batch;

    public JoblyDispatcherWorker(
        Guid workerId,
        ChannelReader<Job> jobReader,
        IServiceScopeFactory scopeFactory,
        ILogger<JoblyDispatcherWorker<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        TimeProvider timeProvider)
    {
        _workerId = workerId;
        _jobReader = jobReader;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration.Value;
        _timeProvider = timeProvider;
        _batch = new CompletionBatch<TContext>(
            scopeFactory,
            timeProvider,
            logger,
            _configuration.CompletionBatchSize,
            _configuration.CompletionFlushInterval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _jobReader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (_jobReader.TryRead(out var job))
                {
                    try
                    {
                        await ProcessJob(job, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Dispatcher worker failed on job {id}", job.Id);
                    }

                    if (_batch.IsFull || _batch.IsTimeElapsed)
                    {
                        await FlushBatchSafely(stoppingToken);
                    }
                }

                // Idle — drain any buffered completions before suspending on WaitToReadAsync
                await FlushBatchSafely(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful stop — StopAsync handles the final flush
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        try
        {
            await _batch.FlushAsync(CancellationToken.None);
            OrchestrationTask<TContext>.SignalOrchestrator();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final batch flush on shutdown failed");
        }
    }

    private async Task FlushBatchSafely(CancellationToken cancellationToken)
    {
        if (_batch.Count == 0)
        {
            return;
        }

        try
        {
            await _batch.FlushAsync(cancellationToken);
            OrchestrationTask<TContext>.SignalOrchestrator();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush completion batch");
        }
    }

    private async Task ProcessJob(Job job, CancellationToken cancellationToken)
    {
        PerfTrace.Begin();

        // Operational observability — Dashboard/incident response needs to see "worker X holds job Y"
        // while it runs. Single UPDATE (no SELECT, no change tracker). Scope disposes when the helper returns.
        await MarkWorkerOwnership(job, cancellationToken);
        job.CurrentWorkerId = _workerId;

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
            handlerScope = _scopeFactory.CreateScope();

            jobContext = handlerScope.ServiceProvider.GetRequiredService<JobContext>();
            jobContext.JobId = job.Id;
            jobContext.TraceId = job.TraceId ?? job.Id;
            jobContext.Metadata = MetadataSerializer.Deserialize(job.Metadata);

            handlerStopwatch = Stopwatch.StartNew();
            await ExecuteJob(job, handlerScope.ServiceProvider, jobCts.Token);
            handlerStopwatch.Stop();

            // Commit handler's work (outbox: published jobs + business entities) before disposing
            var handlerContext = handlerScope.ServiceProvider.GetRequiredService<TContext>();
            await handlerContext.SaveChangesAsync(default);

            // Read metadata and outcome from handler scope before disposing
            job.Metadata = JsonSerializer.Serialize(jobContext.Metadata);
            var successOutcome = jobContext.Outcome;
            handlerScope.Dispose();
            handlerScope = null;

            var durationMs = handlerStopwatch.Elapsed.TotalMilliseconds;

            if (successOutcome != null)
            {
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

            var (counters, finalLog) = BuildFinalization(job, null, durationMs, successOutcome);
            var logs = CollectLogs(finalLog, logCollector).ToArray();
            _batch.Add(new PendingCompletion(job, counters, logs));
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
            job.CurrentState = State.Deleted;
            job.ExpireAt = cancelNow.Add(_configuration.JobExpirationTimeout);
            job.CancellationMode = CancellationMode.None;
            job.CurrentWorkerId = null;
            job.LastKeepAlive = null;

            // Match BuildFinalization: emit both the aggregate and per-hour counters so cancellations
            // show up in the dashboard's hourly graph alongside other terminal states.
            var hourSuffix = cancelNow.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture);
            IReadOnlyList<Counter> cancelCounters =
            [
                new() { Key = "stats:deleted", Value = 1 },
                new() { Key = $"stats:deleted:{hourSuffix}", Value = 1 },
            ];
            var cancelLog = new JobLog
            {
                JobId = job.Id,
                EventType = "Cancelled",
                Timestamp = cancelNow,
                Level = "Information",
                Message = "Job was cancelled by user",
                DurationMs = handlerStopwatch?.Elapsed.TotalMilliseconds,
                WorkerId = _workerId,
            };
            var logs = CollectLogs(cancelLog, logCollector).ToArray();
            _batch.Add(new PendingCompletion(job, cancelCounters, logs));
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
            _logger.LogError(e, "Error executing job {id}", job.Id);
            await jobCts.CancelAsync();
            await monitorTask;

            var (counters, finalLog) = BuildFinalization(job, e, errorDurationMs, outcome);
            var logs = CollectLogs(finalLog, logCollector).ToArray();
            _batch.Add(new PendingCompletion(job, counters, logs));
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

        PerfTrace.Mark(PerfTrace.Done);
        PerfTrace.End();
    }

    private async Task MarkWorkerOwnership(Job job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var handlerTypeToSet = job.HandlerType;
        await context.Set<Job>()
            .Where(x => x.Id == job.Id)
            .ExecuteUpdateAsync(
                x => x
                    .SetProperty(p => p.CurrentWorkerId, _workerId)
                    .SetProperty(p => p.HandlerType, handlerTypeToSet),
                cancellationToken);
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

                using var s = _scopeFactory.CreateScope();
                var ctx = s.ServiceProvider.GetRequiredService<TContext>();

                if (doCancellationCheck)
                {
                    timeSinceLastCheck = TimeSpan.Zero;

                    var cancellationMode = await ctx.Set<Job>()
                        .Where(x => x.Id == jobId)
                        .Select(x => x.CancellationMode)
                        .FirstOrDefaultAsync(stoppingToken);

                    if (cancellationMode != CancellationMode.None)
                    {
                        _logger.LogInformation("Job {jobId} cancellation requested ({mode}), cancelling handler", jobId, cancellationMode);

                        // Flush any pending logs before cancelling — they were already drained from the queue
                        if (pendingLogs.Count > 0)
                        {
                            ctx.Set<JobLog>().AddRange(pendingLogs);
                            await ctx.SaveChangesAsync(stoppingToken);
                        }

                        await jobCts.CancelAsync();
                        return;
                    }

                    var now = _timeProvider.GetUtcNow().UtcDateTime;
                    await ctx.Set<Job>()
                        .Where(x => x.Id == jobId)
                        .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastKeepAlive, now), stoppingToken);
                }

                if (pendingLogs.Count > 0)
                {
                    ctx.Set<JobLog>().AddRange(pendingLogs);
                    await ctx.SaveChangesAsync(stoppingToken);
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
    /// Clears worker-owned fields on the job and produces the completion counters + final state log.
    /// State must be set on the job before calling this method.
    /// </summary>
    private (List<Counter> Counters, JobLog FinalLog) BuildFinalization(Job job, Exception? error, double? durationMs, JobOutcome? outcome)
    {
        var state = job.CurrentState;
        job.CancellationMode = CancellationMode.None;
        job.CurrentWorkerId = null;
        job.LastKeepAlive = null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var hourSuffix = now.ToString("yyyy-MM-dd-HH");
        var counters = new List<Counter>();
        if (state == State.Completed)
        {
            job.ExpireAt = now.Add(_configuration.JobExpirationTimeout);
            counters.Add(new Counter { Key = "stats:succeeded", Value = 1 });
            counters.Add(new Counter { Key = $"stats:succeeded:{hourSuffix}", Value = 1 });
        }
        else if (state == State.Failed)
        {
            counters.Add(new Counter { Key = "stats:failed", Value = 1 });
            counters.Add(new Counter { Key = $"stats:failed:{hourSuffix}", Value = 1 });
        }
        else if (state == State.Deleted)
        {
            job.ExpireAt = now.Add(_configuration.JobExpirationTimeout);
            counters.Add(new Counter { Key = "stats:deleted", Value = 1 });
            counters.Add(new Counter { Key = $"stats:deleted:{hourSuffix}", Value = 1 });
        }

        var logMessage = outcome?.LogMessage
            ?? (error != null ? error.Message : $"Job {job.Id} completed");
        var logException = error?.ToString();

        var eventType = state switch
        {
            State.Completed => "Completed",
            State.Failed => "Failed",
            State.Enqueued => "Requeued",
            State.Deleted => "Deleted",
            _ => state.ToString(),
        };

        var finalLog = new JobLog
        {
            JobId = job.Id,
            EventType = eventType,
            Timestamp = now,
            Level = state == State.Failed ? "Error" : "Information",
            Message = logMessage,
            Exception = logException,
            DurationMs = durationMs,
            WorkerId = _workerId,
        };

        return (counters, finalLog);
    }

    private IEnumerable<JobLog> CollectLogs(JobLog finalLog, JobLogCollector collector)
    {
        yield return finalLog;

        if (!_configuration.EnableHandlerLogging)
        {
            yield break;
        }

        foreach (var drained in collector.Drain())
        {
            yield return drained;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
