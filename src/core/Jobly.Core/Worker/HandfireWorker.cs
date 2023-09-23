using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Jobly.Core.Worker;

public class JoblyWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IJoblyWorkerService _handfireWorkerService;

    public JoblyWorker(IJoblyWorkerService handfireWorkerService)
    {
        _handfireWorkerService = handfireWorkerService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {   
            await _handfireWorkerService.GetAndProcessJob(stoppingToken);
        }
    }
}

