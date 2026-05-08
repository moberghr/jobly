using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Warp.Core;
using Warp.Core.Data;
using Warp.Core.Data.Queries;
using Warp.Core.Notifications;

namespace Warp.Provider.PostgreSql;

/// <summary>
/// Registers the PostgreSQL-specific provider services (row-lock SQL, exception classifier)
/// for Warp. Call <c>opt.UsePostgreSql()</c> inside the <c>AddWarp</c> or
/// <c>AddWarpWorker</c> lambda to opt in.
/// </summary>
public static class PostgreSqlServiceConfiguration
{
    public static IWarpBuilder<TContext> UsePostgreSql<TContext>(this IWarpBuilder<TContext> builder)
        where TContext : DbContext
    {
        builder.Services.TryAddSingleton<IWarpSqlQueries<TContext>>(sp =>
        {
            using var scope = sp.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var names = WarpJobTableNames.FromModel(context.Model);
            return new PostgresWarpSqlQueries<TContext>(names);
        });

        builder.Services.TryAddSingleton<IDatabaseExceptionClassifier, PostgresExceptionClassifier>();
        builder.Services.TryAddSingleton<IWarpNotificationTransportFactory, PostgresNotificationTransportFactory>();

        builder.Services.TryAddSingleton<IWarpLockProvider>(sp =>
            new PostgresLockProvider(ResolveConnectionString<TContext>(sp)));

        builder.Services.TryAddSingleton<IWarpSemaphoreProvider>(sp =>
            new PostgresSemaphoreProvider(ResolveConnectionString<TContext>(sp)));

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
                ?? throw new InvalidOperationException("Cannot resolve connection string for Warp PostgreSQL provider.");
        }

        return connectionString;
    }
}
