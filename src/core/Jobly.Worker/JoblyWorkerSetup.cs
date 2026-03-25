using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Setup for JoblyWorker
/// </summary>
/// <typeparam name="TContext"></typeparam>
public class JoblyWorkerSetup<TContext> : IHostedService where TContext : DbContext
{
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<JoblyWorker<TContext>> _workers = new();

    public JoblyWorkerSetup(IOptions<JoblyWorkerConfiguration> configuration, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _configuration.WorkerCount; i++)
        {
            var worker = ActivatorUtilities.CreateInstance<JoblyWorker<TContext>>(_serviceProvider);
            await worker.StartAsync(cancellationToken);

            _workers.Add(worker);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Make sure we shut down gracefully
        var tasks = _workers.Select(worker => worker.StopAsync(cancellationToken));
        return Task.WhenAll(tasks);
    }
}