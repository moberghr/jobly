using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jobly.PerfTest;

/// <summary>
/// EF Core command interceptor that tallies SQL commands by verb (SELECT/UPDATE/INSERT/DELETE/Other).
/// Used by the perf scenarios to quantify DB noise per configuration — the whole point of the
/// DB-push feature is to reduce these numbers.
/// </summary>
public sealed class CommandCountingInterceptor : DbCommandInterceptor
{
    private long _select;
    private long _update;
    private long _insert;
    private long _delete;
    private long _other;

    public long Select => Interlocked.Read(ref _select);
    public long Update => Interlocked.Read(ref _update);
    public long Insert => Interlocked.Read(ref _insert);
    public long Delete => Interlocked.Read(ref _delete);
    public long Other => Interlocked.Read(ref _other);
    public long Total => Select + Update + Insert + Delete + Other;

    public void Reset()
    {
        Interlocked.Exchange(ref _select, 0);
        Interlocked.Exchange(ref _update, 0);
        Interlocked.Exchange(ref _insert, 0);
        Interlocked.Exchange(ref _delete, 0);
        Interlocked.Exchange(ref _other, 0);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Tally(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Tally(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Tally(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Tally(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Tally(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Tally(command.CommandText);
        return ValueTask.FromResult(result);
    }

    private void Tally(string sql)
    {
        // Skip whitespace + common EF comment prefixes (-- ...\n) to find the verb.
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                // Skip to end of line
                var newline = sql.IndexOf('\n', i);
                i = newline < 0 ? sql.Length : newline + 1;
                continue;
            }

            break;
        }

        if (i >= sql.Length)
        {
            Interlocked.Increment(ref _other);
            return;
        }

        // Match the first verb — case-insensitive.
        if (StartsWith(sql, i, "SELECT"))
        {
            Interlocked.Increment(ref _select);
        }
        else if (StartsWith(sql, i, "UPDATE"))
        {
            Interlocked.Increment(ref _update);
        }
        else if (StartsWith(sql, i, "INSERT"))
        {
            Interlocked.Increment(ref _insert);
        }
        else if (StartsWith(sql, i, "DELETE"))
        {
            Interlocked.Increment(ref _delete);
        }
        else
        {
            Interlocked.Increment(ref _other);
        }
    }

    private static bool StartsWith(string sql, int offset, string verb)
    {
        if (offset + verb.Length > sql.Length)
        {
            return false;
        }

        for (var j = 0; j < verb.Length; j++)
        {
            var a = char.ToUpperInvariant(sql[offset + j]);
            if (a != verb[j])
            {
                return false;
            }
        }

        return true;
    }
}
