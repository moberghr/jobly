using Jobly.Core;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobly.Worker;

public static class ServiceConfiguration
{
    public static IServiceCollection AddJoblyWorker<TContext>(
        this IServiceCollection services,
        Action<JoblyWorkerConfiguration>? optionsAction = null)
        where TContext : DbContext
    {
        optionsAction ??= _ => { };

        return AddJoblyWorker<TContext>(services, (_, options) => optionsAction(options));
    }

    public static IServiceCollection AddJoblyWorker<TContext>(this IServiceCollection services,
        Action<IServiceProvider, JoblyWorkerConfiguration> options)
        where TContext : DbContext
    {
        services.AddSingleton<IHostedService, JoblyWorker<TContext>>();
        services.Configure<JoblyWorkerConfiguration>(config => { options(services.BuildServiceProvider(), config); });

        services.AddJobly<TContext>();
        
        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();

        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();

        services.AddTransient<IInterceptorService, InterceptorService>();

        services.AddHostedService<JoblyHealthManager<TContext>>();

        services.AddHostedService<JoblyWorkerSetup<TContext>>();

        return services;
    }

}