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
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_pauseStateHolder.IsPaused(_workerGroupId))
            {
                await Task.Delay(_groupConfiguration.PollingInterval, stoppingToken);
                continue;
            }

            var didProcessJob = await _joblyWorkerService.GetAndProcessJob(stoppingToken);
            if (!didProcessJob)
            {
                await Task.Delay(_groupConfiguration.PollingInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Jobly worker is stopping.");
    }
}
