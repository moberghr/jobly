using System.Threading.Channels;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Batch-fetches jobs from the database and distributes them to worker slots via a channel.
/// One dispatcher per worker group. Workers execute handlers and complete jobs individually.
/// </summary>
public class JoblyDispatcher<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JoblyDispatcher<TContext>> _logger;
    private readonly WorkerGroupConfiguration _groupConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<Job> _jobChannel;
    private readonly int _workerCount;
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly Guid _workerGroupId;

    public JoblyDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<JoblyDispatcher<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        WorkerGroupConfiguration groupConfiguration,
        TimeProvider timeProvider,
        PauseStateHolder pauseStateHolder,
        Guid workerGroupId)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _groupConfiguration = groupConfiguration;
        _timeProvider = timeProvider;
        _workerCount = groupConfiguration.WorkerCount;
        _pauseStateHolder = pauseStateHolder;
        _workerGroupId = workerGroupId;

        _jobChannel = Channel.CreateBounded<Job>(new BoundedChannelOptions(_workerCount)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
        });
    }

    public ChannelReader<Job> JobReader => _jobChannel.Reader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var floor = _groupConfiguration.PollingInterval;
        var max = _groupConfiguration.MaxPollingInterval;
        var factor = _groupConfiguration.PollingIntervalFactor;
        var currentDelay = floor;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_pauseStateHolder.IsPaused(_workerGroupId))
                {
                    currentDelay = floor;
                    await Task.Delay(floor, stoppingToken);
                    continue;
                }

                var result = await FetchAndDistribute(stoppingToken);
                if (result == FetchResult.Empty)
                {
                    currentDelay = PollingBackoff.Next(currentDelay, floor, max, factor);
                    await Task.Delay(currentDelay, stoppingToken);
                    continue;
                }

                if (result == FetchResult.Fetched)
                {
                    // Real work happened — reset the idle backoff. ChannelFull is not evidence
                    // of work done (workers are saturated), so leave currentDelay alone; the
                    // 10ms wait inside FetchAndDistribute is the channel-full throttle.
                    currentDelay = floor;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Exception is a transient signal, not an idle-queue signal — do not compound
                // the polling backoff. A single floor delay keeps the dispatcher responsive
                // after recovery instead of sitting at MaxPollingInterval for 30s.
                _logger.LogError(ex, "Dispatcher fetch failed");
                await Task.Delay(floor, stoppingToken);
            }
        }

        _jobChannel.Writer.Complete();
    }

    private async Task<FetchResult> FetchAndDistribute(CancellationToken ct)
    {
        var available = _workerCount - _jobChannel.Reader.Count;
        if (available <= 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
            return FetchResult.ChannelFull;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Fetch only Kind=Job (messages are routed by MessageRoutingTask)
        var jobs = await context.Set<Job>()
            .Where(x => x.Kind == JobKind.Job && x.CurrentState == State.Enqueued && x.ScheduleTime < now)
            .Where(x => _groupConfiguration.Queues.Contains(x.Queue))
            .OrderBy(x => x.Queue)
            .ThenBy(x => x.ScheduleTime)
            .Take(available)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .ToListAsync(ct);

        if (jobs.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return FetchResult.Empty;
        }

        // Batch mark all fetched jobs as Processing
        foreach (var job in jobs)
        {
            job.CurrentState = State.Processing;
            job.LastKeepAlive = now;

            context.Set<JobLog>().Add(new JobLog
            {
                JobId = job.Id,
                EventType = "Processing",
                Timestamp = now,
                Level = "Information",
                Message = $"The job {job.Id} is being processed",
            });
        }

        // Mutex enforcement is not done here. MutexPipelineBehavior (registered via
        // AddJoblyMutex) runs inside the handler pipeline in JoblyDispatcherWorker.ExecuteJob
        // and short-circuits to Deleted via IJobContext.Outcome if the concurrency key is held.
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        foreach (var job in jobs)
        {
            await _jobChannel.Writer.WriteAsync(job, ct);
        }

        return FetchResult.Fetched;
    }

    private enum FetchResult
    {
        Empty,
        Fetched,
        ChannelFull,
    }
}
