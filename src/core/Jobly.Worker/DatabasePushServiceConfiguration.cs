using Jobly.Core.Notifications;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jobly.Worker;

/// <summary>
/// Opt-in DB-push extension. Call <c>AddJoblyDatabasePush&lt;TContext&gt;()</c> after
/// <c>AddJoblyWorker&lt;TContext&gt;()</c> to replace the default polling wake-up on the
/// dispatcher / MessageRoutingTask / OrchestrationTask with push notifications delivered
/// via PostgreSQL LISTEN/NOTIFY or SQL Server Service Broker.
/// </summary>
public static class DatabasePushServiceConfiguration
{
    public static IServiceCollection AddJoblyDatabasePush<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        return AddJoblyDatabasePush<TContext>(services, _ => { });
    }

    public static IServiceCollection AddJoblyDatabasePush<TContext>(
        this IServiceCollection services,
        Action<JoblyDatabasePushConfiguration> configure)
        where TContext : DbContext
    {
        var options = new JoblyDatabasePushConfiguration();
        configure(options);
        services.AddSingleton(options);

        // Replace the default NullNotificationTransport with a provider-specific one.
        // RemoveAll is required because the null transport was added via TryAddSingleton in AddJobly.
        services.RemoveAll<IJoblyNotificationTransport>();
        services.AddSingleton<IJoblyNotificationTransport>(sp =>
        {
            var dbOptions = sp.GetRequiredService<DbContextOptions<TContext>>();
            var relationalExtension = dbOptions.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();
            var connectionString = relationalExtension?.ConnectionString;

            // Provider detection: prefer RelationalOptionsExtension type name (cheap, no scope
            // needed). For factory-configured DbContexts (UseNpgsql(sp => ...)) the extension is
            // present but its ConnectionString is null, so we resolve via a scoped context AND
            // re-check the provider via Database.ProviderName to avoid silently defaulting to
            // SQL Server for a Postgres user.
            var providerName = relationalExtension?.GetType().FullName;
            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(providerName))
            {
                using var scope = sp.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                connectionString ??= context.Database.GetConnectionString()
                    ?? throw new InvalidOperationException("Cannot resolve connection string for Jobly DB push.");
                providerName ??= context.Database.ProviderName;
            }

            var isPostgres = providerName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

            return isPostgres
                ? new PostgresNotificationTransport(
                    connectionString,
                    options,
                    sp.GetService<ILogger<PostgresNotificationTransport>>())
                : new SqlServerNotificationTransport(
                    connectionString,
                    options,
                    sp.GetService<ILogger<SqlServerNotificationTransport>>());
        });

        services.AddHostedService<NotificationListenerTask<TContext>>();

        return services;
    }
}
