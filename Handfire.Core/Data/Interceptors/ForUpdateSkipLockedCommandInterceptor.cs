using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Handfire.Core.Interceptors;
public class ForUpdateSkipLockedCommandInterceptor : DbCommandInterceptor
{
    public static readonly string Label = "Use FOR UPDATE SKIP LOCKED";

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
        if (command.CommandText.StartsWith($"-- {Label}", StringComparison.Ordinal))
        {
            command.CommandText += " FOR UPDATE SKIP LOCKED";
        }
    }
}
