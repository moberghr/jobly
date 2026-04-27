using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warp.Core.Data.Entities;
using Warp.Core.Data.Queries;
using Warp.Core.Entities;
using Warp.Core.Enums;

namespace Warp.Worker;

/// <summary>
/// Batch-fetches jobs from the database and distributes them to worker slots via a channel.
/// One dispatcher per worker group. Workers execute handlers and complete jobs individually.
/// </summary>
public class WarpDispatcher<TContext> : BackgroundService
    where TContext : DbContext
{
    // Push-path wake-up: the NotificationListenerTask holds no reference to dispatcher instances,
    // so we register ourselves in a static list and SignalAll fans out. Fine because dispatchers
    // are per-group and there's typically a handful. SemaphoreSlim is cross-thread safe.
    private static readonly List<WarpDispatcher<TContext>> _instances = [];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WarpDispatcher<TContext>> _logger;
    private readonly WorkerGroupConfiguration _groupConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<Job> _jobChannel;
    private readonly int _workerCount;
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly Guid _workerGroupId;
    private readonly SemaphoreSlim _signal = new(0);

    public WarpDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<WarpDispatcher<TContext>> logger,
        IOptions<WarpWorkerConfiguration> configuration,
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

        lock (_instances)
        {
            _instances.Add(this);
        }
    }

    public ChannelReader<Job> JobReader => _jobChannel.Reader;

    /// <summary>
    /// Wakes all registered dispatchers — used by the notification listener so a <c>JobEnqueued</c>
    /// push shortcuts the current exponential-backoff sleep.
    /// </summary>
    public static void SignalAll()
    {
        lock (_instances)
        {
            foreach (var signal in _instances.Select(instance => instance._signal))
            {
                if (signal.CurrentCount == 0)
                {
                    signal.Release();
                }
            }
        }
    }

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
                    await _signal.WaitAsync(floor, stoppingToken);
                    continue;
                }

                var result = await FetchAndDistribute(stoppingToken);
                if (result == FetchResult.Empty)
                {
                    currentDelay = PollingBackoff.Next(currentDelay, floor, max, factor);
                    await _signal.WaitAsync(currentDelay, stoppingToken);
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

        lock (_instances)
        {
            _instances.Remove(this);
        }
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
        var sqlQueries = scope.ServiceProvider.GetRequiredService<IWarpSqlQueries<TContext>>();

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Atomic batch claim. One round-trip, no SELECT→UPDATE window — returns up to
        // `available` rows already transitioned to Processing. Workers across servers get
        // distinct rows via FOR UPDATE SKIP LOCKED (PG) / ROWLOCK+UPDLOCK+READPAST (MSSQL)
        // in the claim subquery.
        // Mutex enforcement runs inside the handler pipeline (MutexPipelineBehavior) — same
        // as before; it short-circuits to Deleted if the concurrency key is held.
        var jobs = await sqlQueries.ClaimEnqueuedJobsAsync(
            context,
            _groupConfiguration.Queues,
            _workerGroupId,
            now,
            available,
            ct);

        if (jobs.Count == 0)
        {
            return FetchResult.Empty;
        }

        // The atomic claim already committed State=Processing for every row in `jobs`. Delivering
        // them to the channel is a separate, interruptible phase: the JobLog SaveChangesAsync and
        // each WriteAsync are await points where a shutdown-triggered cancellation can fire. If
        // we don't recover, claimed-but-undelivered rows stay as Processing orphans until
        // StaleJobRecovery finds them. The try/catch here restores undelivered rows back to
        // Enqueued so shutdown leaves the DB in a clean state.
        var delivered = 0;
        try
        {
            context.Set<JobLog>().AddRange(jobs.Select(job => new JobLog
            {
                JobId = job.Id,
                EventType = "Processing",
                Timestamp = now,
                Level = "Information",
                Message = $"The job {job.Id} is being processed",
            }));

            await context.SaveChangesAsync(ct);

            foreach (var job in jobs)
            {
                await _jobChannel.Writer.WriteAsync(job, ct);
                delivered++;
            }
        }
        catch
        {
            if (delivered < jobs.Count)
            {
                await UnclaimUndelivered(jobs, delivered);
            }

            throw;
        }

        return FetchResult.Fetched;
    }

    /// <summary>
    /// Flips the tail of <paramref name="jobs"/> (rows starting at index <paramref name="delivered"/>)
    /// back to <see cref="State.Enqueued"/> in the DB, clearing the claim stamp. Used when delivery
    /// to the channel is interrupted — most commonly by shutdown cancellation — so the rows don't
    /// linger as Processing orphans. Uses a fresh scope + <see cref="CancellationToken.None"/> so
    /// the cleanup is never itself cancelled.
    /// </summary>
    private async Task UnclaimUndelivered(List<Job> jobs, int delivered)
    {
        try
        {
            var undeliveredIds = jobs.Skip(delivered).Select(j => j.Id).ToArray();
            using var cleanupScope = _scopeFactory.CreateScope();
            var cleanupContext = cleanupScope.ServiceProvider.GetRequiredService<TContext>();

            await cleanupContext.Set<Job>()
                .Where(x => undeliveredIds.Contains(x.Id))
                .ExecuteUpdateAsync(
                    x => x
                        .SetProperty(p => p.CurrentState, State.Enqueued)
                        .SetProperty(p => p.CurrentWorkerId, (Guid?)null)
                        .SetProperty(p => p.LastKeepAlive, (DateTime?)null),
                    CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Cleanup failure is not fatal — StaleJobRecovery will still recover the orphaned
            // Processing rows on its normal cadence. Log loudly because it means the DB was in
            // a bad state (connection lost, timeout) exactly when we needed it.
            _logger.LogError(ex, "Failed to un-claim {Count} undelivered jobs; StaleJobRecovery will recover", jobs.Count - delivered);
        }
    }

    private enum FetchResult
    {
        Empty,
        Fetched,
        ChannelFull,
    }
}
