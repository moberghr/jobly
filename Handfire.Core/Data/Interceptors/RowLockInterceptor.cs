using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Handfire.Core.Interceptors;

public enum DatabaseType
{
    Postgres,
    SqlServer
}

public static class InterceptorConstants
{
    public static readonly string Label = "LOCK ROW";
}

public class PostgresRowLockInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        ManipulateCommand(command);

        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ManipulateCommand(command);

        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    /// <summary>
    /// FOR NO KEY UPDATE is weaker then FOR UPDATE lock: this lock will not block SELECT FOR KEY SHARE commands that attempt to acquire a lock on the same rows. This lock mode is also acquired by any UPDATE that does not acquire a FOR UPDATE lock.
    /// FOR UPDATE is locking RecurringJob.NextJobId when updating recurring job data so FOR NO KEY UPDATE is needed.
    /// </summary>
    /// <param name="command"></param>
    private static void ManipulateCommand(DbCommand command)
    {
        if (command.CommandText.StartsWith($"-- {InterceptorConstants.Label}", StringComparison.Ordinal))
        {
            command.CommandText += " FOR NO KEY UPDATE SKIP LOCKED";
        }
    }
}

public class SqlServerRowLockInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<DbDataReader> result)
    {
        ManipulateCommand(command);

        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ManipulateCommand(command);

        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    private static void ManipulateCommand(DbCommand command)
    {
        if (command.CommandText.StartsWith($"-- {InterceptorConstants.Label}", StringComparison.Ordinal))
        {
            command.CommandText = command.CommandText.Replace($"FROM [job] AS [j]", $"FROM [job] AS [j] WITH (ROWLOCK, UPDLOCK)");
        }
    }
}
