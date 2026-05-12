using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Warp.Worker;

public class WarpWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly ILogger<WarpWorker<TContext>> _logger;
    private readonly IWarpWorkerService _warpWorkerService;
    private readonly WorkerGroupConfiguration _groupConfiguration;
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly TimeProvider _timeProvider;
    private readonly Guid _workerGroupId;

    public WarpWorker(IWarpWorkerService warpWorkerService, ILogger<WarpWorker<TContext>> logger, WorkerGroupConfiguration groupConfiguration, PauseStateHolder pauseStateHolder, TimeProvider timeProvider, Guid workerGroupId)
    {
        _warpWorkerService = warpWorkerService;
        _logger = logger;
        _groupConfiguration = groupConfiguration;
        _pauseStateHolder = pauseStateHolder;
        _timeProvider = timeProvider;
        _workerGroupId = workerGroupId;
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
                    await Task.Delay(floor, _timeProvider, stoppingToken);
                    continue;
                }

                var didProcessJob = await _warpWorkerService.GetAndProcessJob(stoppingToken);
                if (didProcessJob)
                {
                    currentDelay = floor;
                    continue;
                }

                currentDelay = PollingBackoff.Next(currentDelay, floor, max, factor);
                await Task.Delay(currentDelay, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Routine in multi-server deployments: another worker/server updated the row
                // first (row lock raced or concurrency token bumped). Not a handler failure —
                // don't spam stack traces at Error level. Short delay, re-fetch.
                _logger.LogDebug(ex, "Worker fetch hit optimistic concurrency; another worker won the row.");
                await Task.Delay(floor, _timeProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                // Exception is a transient signal, not an idle-queue signal — do not compound
                // the polling backoff. Sleep a short fixed interval and retry, keeping the
                // service alive across DB hiccups or handler pipeline faults.
                _logger.LogError(ex, "Worker fetch failed");
                await Task.Delay(floor, _timeProvider, stoppingToken);
            }
        }

        _logger.LogInformation("Warp worker is stopping.");
    }
}
