using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jobly.Worker;

public class JoblyWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly ILogger<JoblyWorker<TContext>> _logger;
    private readonly IJoblyWorkerService _joblyWorkerService;
    private readonly WorkerGroupConfiguration _groupConfiguration;
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly Guid _workerGroupId;

    public JoblyWorker(IJoblyWorkerService joblyWorkerService, ILogger<JoblyWorker<TContext>> logger, WorkerGroupConfiguration groupConfiguration, PauseStateHolder pauseStateHolder, Guid workerGroupId)
    {
        _joblyWorkerService = joblyWorkerService;
        _logger = logger;
        _groupConfiguration = groupConfiguration;
        _pauseStateHolder = pauseStateHolder;
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
                    await Task.Delay(floor, stoppingToken);
                    continue;
                }

                var didProcessJob = await _joblyWorkerService.GetAndProcessJob(stoppingToken);
                if (didProcessJob)
                {
                    currentDelay = floor;
                    continue;
                }

                currentDelay = PollingBackoff.Next(currentDelay, floor, max, factor);
                await Task.Delay(currentDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Exception is a transient signal, not an idle-queue signal — do not compound
                // the polling backoff. Sleep a short fixed interval and retry, keeping the
                // service alive across DB hiccups or handler pipeline faults.
                _logger.LogError(ex, "Worker fetch failed");
                await Task.Delay(floor, stoppingToken);
            }
        }

        _logger.LogInformation("Jobly worker is stopping.");
    }
}
