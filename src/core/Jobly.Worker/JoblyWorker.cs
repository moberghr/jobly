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

    public JoblyWorker(IJoblyWorkerService joblyWorkerService, ILogger<JoblyWorker<TContext>> logger, WorkerGroupConfiguration groupConfiguration)
    {
        _joblyWorkerService = joblyWorkerService;
        _logger = logger;
        _groupConfiguration = groupConfiguration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var didProcessJob = await _joblyWorkerService.GetAndProcessJob(stoppingToken);
            if (!didProcessJob)
            {
                await Task.Delay(_groupConfiguration.PollingInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Jobly worker is stopping.");
    }
}
