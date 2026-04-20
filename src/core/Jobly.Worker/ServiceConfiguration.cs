using Jobly.Core;
using Jobly.Core.Logging;
using Jobly.Worker.Services;
using Medallion.Threading;
using Medallion.Threading.Postgres;
using Medallion.Threading.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    /// Add Jobly worker service configuration to the service collection. Call the builder's
    /// config fields directly (<c>opt.WorkerCount = 10</c>), opt into provider (
    /// <c>opt.UsePostgreSql()</c> — provider-package extension), and worker-side addons (
    /// <c>opt.UseDatabasePush()</c>) inside the lambda.
    /// </summary>
    public static IServiceCollection AddJoblyWorker<TContext>(
        this IServiceCollection services,
        Action<JoblyWorkerBuilder<TContext>>? configure = null)
        where TContext : DbContext
    {
        var builder = new JoblyWorkerBuilder<TContext>(services);
        configure?.Invoke(builder);

        // Register the builder as both the worker-level and Core-level options so one set of
        // values drives everything. JoblyWorkerConfiguration inherits from JoblyConfiguration,
        // so this is safe. TryAdd: if AddJobly was called separately first, its builder wins
        // for the Core-level IOptions — user's addons from that lambda (Mutex entity config,
        // etc.) are preserved.
        services.TryAddSingleton<IOptions<JoblyWorkerConfiguration>>(Options.Create<JoblyWorkerConfiguration>(builder));
        services.TryAddSingleton<IOptions<JoblyConfiguration>>(Options.Create<JoblyConfiguration>(builder));

        return AddJoblyWorkerInner<TContext>(services);
    }

    private static IServiceCollection AddJoblyWorkerInner<TContext>(
        this IServiceCollection services)
        where TContext : DbContext
    {
        // Core setup is idempotent (TryAdd-based) so calling it here is safe even if the user
        // also called AddJobly separately for their own addon opt-ins.
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

        // Register distributed locks — resolved from DbContextOptions to preserve credentials.
        // Database.GetConnectionString() may strip passwords (Npgsql PersistSecurityInfo=false).
        services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            using var scope = sp.CreateScope();
            var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<TContext>>();
            var relationalExtension = dbOptions.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();
            var connectionString = relationalExtension?.ConnectionString;

            if (connectionString is null)
            {
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                connectionString = context.Database.GetConnectionString()
                    ?? throw new InvalidOperationException("Cannot resolve connection string for distributed locks.");
            }

            var isPostgres = relationalExtension?.GetType().FullName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
            if (isPostgres)
            {
                return new PostgresDistributedSynchronizationProvider(connectionString);
            }

            return new SqlDistributedSynchronizationProvider(connectionString);
        });

        services.AddSingleton<IJoblyLockProvider, JoblyLockProvider>();

        services.AddSingleton<ServerRegistrationState>();
        services.AddHostedService<JoblyServerRegistration<TContext>>();
        services.AddHostedService<JoblyDispatcherHost<TContext>>();
        services.AddHostedService<JoblySingleWorkerHost<TContext>>();
        services.AddHostedService<HeartbeatTask<TContext>>();
        services.AddHostedService<CounterAggregatorTask<TContext>>();
        services.AddHostedService<ServerCleanupTask<TContext>>();
        services.AddHostedService<StaleJobRecoveryTask<TContext>>();
        services.AddHostedService<ExpirationCleanupTask<TContext>>();
        services.AddHostedService<RecurringJobSchedulerTask<TContext>>();
        services.AddHostedService<ScheduledJobActivationTask<TContext>>();
        services.AddHostedService<MessageRoutingTask<TContext>>();
        services.AddHostedService<OrchestrationTask<TContext>>();

        return services;
    }
}
