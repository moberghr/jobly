using Jobly.Core.Notifications;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jobly.Worker;

/// <summary>
/// Opt-in DB-push extension on <see cref="JoblyWorkerBuilder{TContext}"/>. Call
/// <c>opt.UseDatabasePush()</c> inside the <c>AddJoblyWorker</c> lambda to replace the default
/// polling wake-up on the dispatcher / MessageRoutingTask / OrchestrationTask with push
/// notifications delivered via PostgreSQL LISTEN/NOTIFY or SQL Server Service Broker.
/// </summary>
public static class DatabasePushServiceConfiguration
{
    public static Jobly.Core.IJoblyBuilder<TContext> UseDatabasePush<TContext>(
        this Jobly.Core.IJoblyBuilder<TContext> builder,
        Action<JoblyDatabasePushConfiguration>? configure = null)
        where TContext : DbContext
    {
        var options = new JoblyDatabasePushConfiguration();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        // Replace the default NullNotificationTransport with a provider-specific one.
        // RemoveAll is required because the null transport was added via TryAddSingleton in AddJobly.
        builder.Services.RemoveAll<IJoblyNotificationTransport>();
        builder.Services.AddSingleton<IJoblyNotificationTransport>(sp =>
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

        builder.Services.AddHostedService<NotificationListenerTask<TContext>>();

        return builder;
    }
}
