using Jobly.Core;
using Jobly.Core.Data;
using Jobly.Core.Data.Queries;
using Jobly.Core.Notifications;
using Medallion.Threading;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jobly.Provider.PostgreSql;

/// <summary>
/// Registers the PostgreSQL-specific provider services (row-lock SQL, exception classifier)
/// for Jobly. Call <c>opt.UsePostgreSql()</c> inside the <c>AddJobly</c> or
/// <c>AddJoblyWorker</c> lambda to opt in.
/// </summary>
public static class PostgreSqlServiceConfiguration
{
    public static IJoblyBuilder<TContext> UsePostgreSql<TContext>(this IJoblyBuilder<TContext> builder)
        where TContext : DbContext
    {
        builder.Services.TryAddSingleton<IJoblySqlQueries<TContext>>(sp =>
        {
            using var scope = sp.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var names = JoblyJobTableNames.FromModel(context.Model);
            return new PostgresJoblySqlQueries<TContext>(names);
        });

        builder.Services.TryAddSingleton<IDatabaseExceptionClassifier, PostgresExceptionClassifier>();
        builder.Services.TryAddSingleton<IJoblyNotificationTransportFactory, PostgresNotificationTransportFactory>();

        builder.Services.TryAddSingleton<IDistributedLockProvider>(sp =>
            new PostgresDistributedSynchronizationProvider(ResolveConnectionString<TContext>(sp)));

        return builder;
    }

    private static string ResolveConnectionString<TContext>(IServiceProvider sp)
        where TContext : DbContext
    {
        using var scope = sp.CreateScope();
        var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<TContext>>();
        var relationalExtension = dbOptions.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();
        var connectionString = relationalExtension?.ConnectionString;

        if (connectionString is null)
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            connectionString = context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("Cannot resolve connection string for Jobly PostgreSQL provider.");
        }

        return connectionString;
    }
}
