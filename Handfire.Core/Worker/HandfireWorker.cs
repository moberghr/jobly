using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Handfire.Core.Worker;

public class HandfireWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IHandfireWorkerService _handfireWorkerService;

    public HandfireWorker(IHandfireWorkerService handfireWorkerService)
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

