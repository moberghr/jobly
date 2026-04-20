using Jobly.Core.Notifications;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jobly.Worker;

/// <summary>
/// Opt-in DB-push extension on the Jobly builder. Call <c>opt.UseDatabasePush()</c> inside the
/// <c>AddJobly</c> or <c>AddJoblyWorker</c> lambda (after <c>UsePostgreSql()</c> or
/// <c>UseSqlServer()</c>) to replace the default polling wake-up on the dispatcher,
/// <c>MessageRoutingTask</c>, and <c>OrchestrationTask</c> with push notifications delivered via
/// the provider's native mechanism (Postgres LISTEN/NOTIFY, SQL Server Service Broker). Provider
/// selection is transparent: the transport is constructed from whichever
/// <see cref="IJoblyNotificationTransportFactory"/> the provider package registered.
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

        // Replace the default NullNotificationTransport with the provider-specific one.
        // RemoveAll is required because the null transport was added via TryAddSingleton in AddJobly.
        builder.Services.RemoveAll<IJoblyNotificationTransport>();
        builder.Services.AddSingleton<IJoblyNotificationTransport>(sp =>
        {
            var factory = sp.GetService<IJoblyNotificationTransportFactory>()
                ?? throw new InvalidOperationException(
                    "UseDatabasePush requires a provider package. Call opt.UsePostgreSql() or opt.UseSqlServer() inside the AddJobly/AddJoblyWorker lambda before opt.UseDatabasePush().");

            var dbOptions = sp.GetRequiredService<DbContextOptions<TContext>>();
            var relationalExtension = dbOptions.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();
            var connectionString = relationalExtension?.ConnectionString;

            // Factory-configured DbContexts (UseNpgsql(sp => ...)) have the extension present but
            // with a null connection string — resolve via a scoped context.
            if (string.IsNullOrEmpty(connectionString))
            {
                using var scope = sp.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                connectionString = context.Database.GetConnectionString()
                    ?? throw new InvalidOperationException("Cannot resolve connection string for Jobly DB push.");
            }

            return factory.Create(connectionString, options, sp.GetRequiredService<ILoggerFactory>());
        });

        builder.Services.AddHostedService<NotificationListenerTask<TContext>>();

        return builder;
    }
}
