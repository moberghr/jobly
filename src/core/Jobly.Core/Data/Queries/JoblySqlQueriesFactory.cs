using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Data.Queries;

/// <summary>
/// Builds a provider-appropriate <see cref="IJoblySqlQueries{TContext}"/> directly from an
/// <see cref="DbContext"/>. Used by the DI registration and as a fallback when code runs
/// without the singleton registered (tests that instantiate worker internals directly, static
/// test helpers on background tasks).
/// </summary>
public static class JoblySqlQueriesFactory
{
    public static IJoblySqlQueries<TContext> Create<TContext>(TContext context)
        where TContext : DbContext
    {
        var names = JoblyJobTableNames.FromModel(context.Model);
        var isPostgres = context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        return isPostgres
            ? new PostgresJoblySqlQueries<TContext>(names)
            : new SqlServerJoblySqlQueries<TContext>(names);
    }
}
