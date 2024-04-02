using Jobly.Worker.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobly.Worker;

public static class ServiceConfiguration
{
    public static IServiceCollection AddJoblyWorker<TContext>(this IServiceCollection services, Action<JoblyWorkerConfiguration>? options = null)
        where TContext : DbContext
    {
        services.AddJoblyWorkerServices<TContext>();

        if (options != null)
        {
            services.Configure<JoblyWorkerConfiguration>(options);
        }

        return services;
    }
    
    public static IServiceCollection AddJoblyWorker<TContext>(this IServiceCollection services, Action<IServiceProvider, JoblyWorkerConfiguration>? options = null)
        where TContext : DbContext
    {
        services.AddJoblyWorkerServices<TContext>();

        if (options != null)
        {
            services.Configure<JoblyWorkerConfiguration>(config =>
            {
                options(services.BuildServiceProvider(), config);
            });
        }

        return services;
    }

    private static IServiceCollection AddJoblyWorkerServices<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();
        
        // Adding JoblyWorkerScheduler as a singleton so that the health check can access it.
        services.AddSingleton<IJoblyWorkerScheduler, JoblyWorkerScheduler<TContext>>();
        services.AddHostedService<IJoblyWorkerScheduler>(provider => provider.GetRequiredService<IJoblyWorkerScheduler>());
        services.AddSingleton<RetryInterceptor>();
        services.AddSingleton<ContinuationInterceptor>();
        
        services.AddSingleton<JoblyHealthCheck>();
        services.AddHealthChecks()
        .AddCheck<JoblyHealthCheck>("jobly_scheduler_health_check");

        return services;
    }
    
    public static IServiceCollection AddPostgresNotifyWakeupProvider<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddTransient<IWakeupProvider, PostgresNotifyWakeupProvider<TContext>>();
        return services;
    }
}