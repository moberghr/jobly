using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Jobly.Worker;

public class JoblyWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IJoblyWorkerService _joblyWorkerService;

    public JoblyWorker(IJoblyWorkerService joblyWorkerService)
    {
        _joblyWorkerService = joblyWorkerService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {   
            await _joblyWorkerService.GetAndProcessJob(stoppingToken);
        }
    }
}

