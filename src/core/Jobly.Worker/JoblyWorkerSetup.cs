using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Setup for JoblyWorker
/// </summary>
/// <typeparam name="TContext"></typeparam>
public class JoblyWorkerSetup<TContext> : BackgroundService where TContext : DbContext
{
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public JoblyWorkerSetup(IOptions<JoblyWorkerConfiguration> configuration, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var i = 0; i < _configuration.WorkerCount; i++)
        {
            var worker = ActivatorUtilities.CreateInstance<JoblyWorker<TContext>>(_serviceProvider);
            if (worker is IHostedService hostedService)
            {
                await hostedService.StartAsync(stoppingToken);
            }
        }
    }
}