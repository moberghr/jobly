using Jobly.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

public static class ServiceConfiguration
{
    public static IServiceCollection AddJoblyWorker<TContext>(this IServiceCollection services, Action<JoblyWorkerConfiguration>? options = null)
        where TContext : DbContext
    {
        if (options != null)
        {
            services.Configure<JoblyWorkerConfiguration>(options);
        }

        services.AddJobly<TContext>();
        
        services.AddJoblyWorkerServices<TContext>();
        
        return services;
    }

    public static IServiceCollection AddJoblyWorker<TContext>(this IServiceCollection services,
        Action<IServiceProvider, JoblyWorkerConfiguration> options)
        where TContext : DbContext
    {
        services.AddSingleton<IHostedService, JoblyWorker<TContext>>();
        services.Configure<JoblyWorkerConfiguration>(config =>
        {
            options(services.BuildServiceProvider(), config);
        });

        services.AddJoblyWorkerServices<TContext>();
        return services;
    }
    private static void AddJoblyWorkerServices<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();

        // Adding JoblyWorkerScheduler as a singleton so that the health check can access it.
        // services.AddSingleton<IJoblyWorkerScheduler, JoblyWorkerScheduler<TContext>>();
        // services.AddHostedService<IJoblyWorkerScheduler>(provider => provider.GetRequiredService<IJoblyWorkerScheduler>());
        // services.AddSingleton<RetryInterceptor>();
        // services.AddSingleton<ContinuationInterceptor>();

        // services.AddSingleton<JoblyHealthCheck>();
        // services.AddHealthChecks()
        //     .AddCheck<JoblyHealthCheck>("jobly_scheduler_health_check");
        
        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();
        
        // services.AddTransient<IHostedService>(provider =>
        // {
        //     var config = provider.GetRequiredService<IOptions<JoblyWorkerConfiguration>>().Value;
        //
        //     for (var i = 0; i < config.WorkerCount; i++)
        //     {
        //         services.AddHostedService<JoblyWorker<TContext>>();
        //     }
        //     
        // });

        
        services.AddHostedService<JoblyWorkerSetup<TContext>>();

    }

}