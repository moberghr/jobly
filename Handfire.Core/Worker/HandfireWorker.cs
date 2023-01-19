using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Handfire.Core.Worker;

public class HandfireWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IHandfireWorkerService _handfireWorkerService;
    private readonly string _workerId = Guid.NewGuid().ToString();

    public HandfireWorker(IHandfireWorkerService handfireWorkerService)
    {
        _handfireWorkerService = handfireWorkerService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _handfireWorkerService.GetAndProcessJob(_workerId);
        }
    }
}

