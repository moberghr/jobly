using Jobly.Core;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Provides methods to configure service for Jobly worker.
///
/// based on https://learn.microsoft.com/en-us/dotnet/core/extensions/options-library-authors
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// Add Jobly worker service configuration to the service collection.
    /// </summary>
    /// <typeparam name="TContext">The type of the DbContext.</typeparam>
    /// <param name="services">The service collection to add the configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddJoblyWorker<TContext>(
        this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddOptions<JoblyWorkerConfiguration>();
        return AddJoblyWorkerInner<TContext>(services);
    }

    /// <summary>
    /// Add Jobly worker service configuration to the service collection.
    /// </summary>
    /// <typeparam name="TContext">The type of the DbContext.</typeparam>
    /// <param name="services">The service collection to add the configuration.</param>
    /// <param name="optionsAction">The action to configure the Jobly worker configuration options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddJoblyWorker<TContext>(
        this IServiceCollection services,
        Action<JoblyWorkerConfiguration> optionsAction)
        where TContext : DbContext
    {
        // Setup the configuration
        services.AddOptions<JoblyWorkerConfiguration>()
            .Configure(optionsAction);
        
        return AddJoblyWorkerInner<TContext>(services);
    }

    /// <summary>
    /// Add Jobly worker service configuration to the service collection.
    /// </summary>
    /// <typeparam name="TContext">The type of the DbContext.</typeparam>
    /// <param name="services">The service collection to add the configuration.</param>
    /// <param name="namedConfigurationSection">The named configuration section.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddJoblyWorker<TContext>(
        this IServiceCollection services,
        IConfiguration namedConfigurationSection)
        where TContext : DbContext
    {
        // Setup the configuration
        services.AddOptions<JoblyWorkerConfiguration>()
            .Bind(namedConfigurationSection);
        
        return AddJoblyWorkerInner<TContext>(services);
    }

    private static IServiceCollection AddJoblyWorkerInner<TContext>(
        this IServiceCollection services)
        where TContext : DbContext
    {
        
        services.AddSingleton<IHostedService, JoblyWorker<TContext>>();

        services.AddJobly<TContext>();

        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();

        services.AddTransient<IJoblyWorkerService, JoblyWorkerService<TContext>>();

        services.AddHostedService<JoblyWorkerSetup<TContext>>();

        return services;
    }
}