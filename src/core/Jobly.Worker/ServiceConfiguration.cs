using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jobly.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobly.Worker;

public static class ServiceConfiguration
{
    public static IServiceCollection AddJobly<TContext>(this IServiceCollection services, int workerCount, int retryCount = 0)
        where TContext : DbContext
    {
        services.AddJoblyCore<TContext>(retryCount);
        services.AddJoblyWorker<TContext>(workerCount);

        return services;
    }

    public static IServiceCollection AddJoblyWorker<TContext>(this IServiceCollection services, int workerCount)
        where TContext : DbContext
    {
        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();

        for (var i = 0; i < workerCount; i++)
        {
            services.AddSingleton<IHostedService, JoblyWorker<TContext>>();
        }

        return services;
    }
}