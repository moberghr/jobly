using Jobly.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobly.Worker;

public static class ServiceConfiguration
{
    public static IServiceCollection AddJoblyWorker<TContext>(this IServiceCollection services, Action<JoblyWorkerConfiguration>? options = null)
        where TContext : DbContext
    {
        services.Configure(options ?? (_ => { }));
        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();
        services.AddSingleton<IHostedService, JoblyWorkerPool<TContext>>();
        
        return services;
    }
    
    public static IServiceCollection AddPostgresNotifyWakeupProvider<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddTransient<IWakeupProvider, PostgresNotifyWakeupProvider<TContext>>();
        return services;
    }
}