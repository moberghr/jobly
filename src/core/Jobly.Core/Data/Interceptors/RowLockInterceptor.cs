using System.Data.Common;
using Jobly.Core.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jobly.Core.Interceptors;

public static class InterceptorConstants
{
    public static readonly string RowLockTableJob = "LOCK ROW TABLE JOB";
    public static readonly string RowLockTableJobWait = "LOCK ROW TABLE JOB WAIT";
    public static readonly string RowLockTableCounter = "LOCK ROW TABLE COUNTER";
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
    private static void ManipulateCommand(DbCommand command)
    {
        if (command.CommandText.StartsWith($"-- {InterceptorConstants.RowLockTableJob}", StringComparison.Ordinal)
            && !command.CommandText.StartsWith($"-- {InterceptorConstants.RowLockTableJobWait}", StringComparison.Ordinal))
        {
            command.CommandText += " FOR NO KEY UPDATE SKIP LOCKED";
        }
        else if (command.CommandText.StartsWith($"-- {InterceptorConstants.RowLockTableJobWait}", StringComparison.Ordinal))
        {
            command.CommandText += " FOR NO KEY UPDATE";
        }
        else if (command.CommandText.StartsWith($"-- {InterceptorConstants.RowLockTableCounter}", StringComparison.Ordinal))
        {
            command.CommandText += " FOR UPDATE SKIP LOCKED";
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
        if (command.CommandText.StartsWith($"-- {InterceptorConstants.RowLockTableJob}", StringComparison.Ordinal)
            && !command.CommandText.StartsWith($"-- {InterceptorConstants.RowLockTableJobWait}", StringComparison.Ordinal))
        {
            command.CommandText = command.CommandText.Replace($"FROM [{nameof(Job)}] AS [j]", $"FROM [{nameof(Job)}] AS [j] WITH (ROWLOCK, UPDLOCK, READPAST)", StringComparison.Ordinal);
        }
        else if (command.CommandText.StartsWith($"-- {InterceptorConstants.RowLockTableJobWait}", StringComparison.Ordinal))
        {
            command.CommandText = command.CommandText.Replace($"FROM [{nameof(Job)}] AS [j]", $"FROM [{nameof(Job)}] AS [j] WITH (ROWLOCK, UPDLOCK)", StringComparison.Ordinal);
        }
        else if (command.CommandText.StartsWith($"-- {InterceptorConstants.RowLockTableCounter}", StringComparison.Ordinal))
        {
            command.CommandText = command.CommandText.Replace("FROM [Counter] AS [c]", "FROM [Counter] AS [c] WITH (ROWLOCK, UPDLOCK, READPAST)", StringComparison.Ordinal);
        }
    }
}
