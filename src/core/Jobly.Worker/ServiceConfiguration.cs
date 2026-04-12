using Jobly.Core;
using Jobly.Core.Logging;
using Jobly.Worker.Services;
using Medallion.Threading;
using Medallion.Threading.Postgres;
using Medallion.Threading.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        services.AddJobly<TContext>();

        services.AddSingleton<PauseStateHolder>();

        services.AddLogging(builder =>
        {
            builder.AddProvider(new JobLoggerProvider());
            builder.Configure(options =>
            {
                options.ActivityTrackingOptions |= ActivityTrackingOptions.TraceId
                    | ActivityTrackingOptions.SpanId
                    | ActivityTrackingOptions.ParentId;
            });
        });

        // Register distributed locks — resolved lazily from the DbContext's connection string
        services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            using var scope = sp.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var connectionString = context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("Cannot resolve connection string for distributed locks.");

            var isPostgres = context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
            if (isPostgres)
            {
                return new PostgresDistributedSynchronizationProvider(connectionString);
            }

            return new SqlDistributedSynchronizationProvider(connectionString);
        });

        services.AddHostedService<JoblyWorkerSetup<TContext>>();
        services.AddHostedService<HeartbeatTask<TContext>>();
        services.AddHostedService<CounterAggregatorTask<TContext>>();
        services.AddHostedService<ServerCleanupTask<TContext>>();
        services.AddHostedService<StaleJobRecoveryTask<TContext>>();
        services.AddHostedService<ExpirationCleanupTask<TContext>>();
        services.AddHostedService<RecurringJobSchedulerTask<TContext>>();
        services.AddHostedService<MessageRoutingTask<TContext>>();
        services.AddHostedService<OrchestrationTask<TContext>>();

        return services;
    }
}
