using System.Diagnostics;
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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _jobReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJob(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatcher worker failed on job {id}", job.Id);
            }
        }
    }

    private async Task ProcessJob(Job job, CancellationToken cancellationToken)
    {
        PerfTrace.Begin();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var trackedJob = await context.Set<Job>().FindAsync([job.Id], cancellationToken)
            ?? throw new InvalidOperationException($"Job {job.Id} not found");
        trackedJob.CurrentWorkerId = _workerId;
        trackedJob.HandlerType = job.HandlerType;
        await context.SaveChangesAsync(cancellationToken);
        job = trackedJob;

        var logCollector = new JobLogCollector { JobId = job.Id, TimeProvider = _timeProvider, WorkerId = _workerId };

        using var jobCts = new CancellationTokenSource();
        var monitorTask = RunJobMonitor(job.Id, jobCts, cancellationToken);

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
            };

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
        }

        OrchestrationTask<TContext>.Signal();

        PerfTrace.Mark(PerfTrace.Done);
        PerfTrace.End();
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

    private async Task RunJobMonitor(Guid jobId, CancellationTokenSource jobCts, CancellationToken stoppingToken)
    {
        var interval = _configuration.CancellationCheckInterval;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, jobCts.Token);
        while (!linked.IsCancellationRequested)
        {
            try { await Task.Delay(interval, linked.Token); }
            catch (OperationCanceledException) { return; }

            try
            {
                using var s = _scopeFactory.CreateScope();
                var ctx = s.ServiceProvider.GetRequiredService<TContext>();

                var cancellationMode = await ctx.Set<Job>()
                    .Where(x => x.Id == jobId)
                    .Select(x => x.CancellationMode)
                    .FirstOrDefaultAsync(stoppingToken);

                if (cancellationMode != CancellationMode.None)
                {
                    _logger.LogInformation("Job {jobId} cancellation requested ({mode}), cancelling handler", jobId, cancellationMode);
                    await jobCts.CancelAsync();
                    return;
                }

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                await ctx.Set<Job>()
                    .Where(x => x.Id == jobId)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastKeepAlive, now), stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed job monitor for {jobId}", jobId);
            }
        }
    }

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
        if (collector.Entries.Count == 0)
        {
            return;
        }

        await context.Set<JobLog>().AddRangeAsync(collector.Entries);
    }

    private static void AddCounters(TContext context, string totalKey, string hourlyKey)
    {
        context.Set<Counter>().Add(new Counter { Key = totalKey, Value = 1 });
        context.Set<Counter>().Add(new Counter { Key = hourlyKey, Value = 1 });
    }
}
