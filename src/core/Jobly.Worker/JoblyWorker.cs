using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

public class JoblyWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly ILogger<JoblyWorker<TContext>> _logger;
    private readonly IJoblyWorkerService _joblyWorkerService;
    private readonly JoblyWorkerConfiguration _configuration;

    public JoblyWorker(IJoblyWorkerService joblyWorkerService, ILogger<JoblyWorker<TContext>> logger, IOptions<JoblyWorkerConfiguration> configuration)
    {
        _joblyWorkerService = joblyWorkerService;
        _logger = logger;
        _configuration = configuration.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var didProcessJob = await _joblyWorkerService.GetAndProcessJob(stoppingToken);
            if (!didProcessJob)
            {
                await Task.Delay(_configuration.PollingInterval, stoppingToken);
            }
        }
        _logger.LogInformation("Jobly worker is stopping.");
    }
}
