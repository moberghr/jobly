using System.Text.Json;
using System.Threading.Channels;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Interceptors;
using Jobly.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Worker that receives pre-fetched jobs from a dispatcher channel.
/// Handles execution and completion — dispatcher only handles fetching.
/// </summary>
public class JoblyDispatcherWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly Guid _workerId;
    private readonly ChannelReader<Job> _jobReader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JoblyDispatcherWorker<TContext>> _logger;
    private readonly JoblyWorkerConfiguration _configuration;

    public JoblyDispatcherWorker(
        Guid workerId,
        ChannelReader<Job> jobReader,
        IServiceScopeFactory scopeFactory,
        ILogger<JoblyDispatcherWorker<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration)
    {
        _workerId = workerId;
        _jobReader = jobReader;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration.Value;
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

        // Reload the job in this context (dispatcher's context is a different scope)
        var trackedJob = await context.Set<Job>().FindAsync([job.Id], cancellationToken)
            ?? throw new InvalidOperationException($"Job {job.Id} not found");
        trackedJob.CurrentWorkerId = _workerId;
        trackedJob.HandlerType = job.HandlerType;
        await context.SaveChangesAsync(cancellationToken);
        job = trackedJob;

        // Set up log capture and execution context
        var logCollector = new JobLogCollector { JobId = job.Id };
        JobLogContext.Current = logCollector;
        JobExecutionContext.Current = new JobExecutionInfo
        {
            JobId = job.Id,
            TraceId = job.TraceId ?? job.Id,
        };

        // Start keep-alive
        using var keepAliveCts = new CancellationTokenSource();
        var keepAliveTask = RunKeepAlive(job.Id, keepAliveCts.Token);

        try
        {
            PerfTrace.Mark(PerfTrace.ExecuteHandler);
            _logger.LogInformation("Worker {workerId} executing job {id}", _workerId, job.Id);
            await ExecuteJob(job, scope.ServiceProvider, cancellationToken);
            _logger.LogInformation("Worker {workerId} completed job {id}", _workerId, job.Id);

            PerfTrace.Mark(PerfTrace.CancelKeepAlive);
            await keepAliveCts.CancelAsync();
            await keepAliveTask;

            PerfTrace.Mark(PerfTrace.BeginTransaction2);
            await using var endTransaction = await context.Database.BeginTransactionAsync(default);

            await UpdateJobData(context, job, null, default);
            await SaveJobLogs(context, logCollector);

            PerfTrace.Mark(PerfTrace.SaveCompleted);
            await context.SaveChangesAsync(default);

            PerfTrace.Mark(PerfTrace.CommitTransaction2);
            await endTransaction.CommitAsync(default);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error executing job {id}", job.Id);
            await keepAliveCts.CancelAsync();
            await keepAliveTask;

            await using var endTransaction = await context.Database.BeginTransactionAsync(default);
            await UpdateJobData(context, job, e, default);
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
    }

    private static async Task ExecuteJob(Job job, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var messageType = Type.GetType(job.Type) ?? throw new JoblyException($"Unknown type {job.Type}");
        var payload = JsonSerializer.Deserialize(job.Message, messageType) ?? throw new JoblyException($"Unable to deserialize message {job.Message} to type {job.Type}");
        if (job.HandlerType != null)
        {
            var handlerType = Type.GetType(job.HandlerType) ?? throw new JoblyException($"Unknown handler type {job.HandlerType}");
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

        var jobHandlerType = JobDispatcher.DiscoverJobHandler(messageType, provider) ?? throw new JoblyException($"No handler registered for {messageType.Name}");
        job.HandlerType = jobHandlerType.AssemblyQualifiedName;
        await JobDispatcher.ExecuteJobHandler(payload, messageType, jobHandlerType, provider, cancellationToken);
    }

    private async Task RunKeepAlive(Guid jobId, CancellationToken ct)
    {
        var interval = _configuration.InvisibilityTimeout / 5;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { return; }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                await context.Set<Job>()
                    .Where(x => x.Id == jobId)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastKeepAlive, DateTime.UtcNow), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to refresh keep-alive for job {jobId}", jobId);
            }
        }
    }

    private static async Task UpdateJobData(TContext context, Job job, Exception? error, CancellationToken cancellationToken)
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
        job.CurrentWorkerId = null;
        job.LastKeepAlive = null;

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
        var logException = error?.ToString();

        await CreateJobLog(context, job.Id, state, logMessage, cancellationToken, logException);
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

    private static async Task CreateJobLog(TContext context, Guid jobId, State state, string? message, CancellationToken cancellationToken, string? exception = null)
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

        var currentBatchJob = await context.Set<Job>()
            .Where(x => x.Id == currentBatch.Id)
            .FirstAsync(cancellationToken);

        if (currentBatch.ContinuationOptions == BatchContinuationOptions.OnlyOnSucceeded)
        {
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
