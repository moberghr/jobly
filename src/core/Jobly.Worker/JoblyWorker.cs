using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jobly.Worker;

public class JoblyWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly ILogger<JoblyWorker<TContext>> _logger;
    private readonly IJoblyWorkerService _joblyWorkerService;

    public JoblyWorker(IJoblyWorkerService joblyWorkerService, ILogger<JoblyWorker<TContext>> logger)
    {
        _joblyWorkerService = joblyWorkerService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {   
            var didProcessJob = await _joblyWorkerService.GetAndProcessJob(stoppingToken);
            if (!didProcessJob)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        _logger.LogInformation("Jobly worker is stopping.");
    }
}

