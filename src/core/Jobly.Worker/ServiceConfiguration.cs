using Jobly.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobly.Worker;

public static class ServiceConfiguration
{
    public static IServiceCollection AddJoblyWorker<TContext>(this IServiceCollection services,
        Action<JoblyWorkerConfiguration>? options = null)
        where TContext : DbContext
    {
        if (options != null)
        {
            services.Configure(options);
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
        services.Configure<JoblyWorkerConfiguration>(config => { options(services.BuildServiceProvider(), config); });

        services.AddJoblyWorkerServices<TContext>();
        return services;
    }

    private static void AddJoblyWorkerServices<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();

        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();

        services.AddTransient<IInterceptorService, InterceptorService>();

        services.AddHostedService<JoblyHealthManager<TContext>>();

        services.AddHostedService<JoblyWorkerSetup<TContext>>();
    }
}