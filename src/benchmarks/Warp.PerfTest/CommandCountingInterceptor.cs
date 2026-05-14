using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Warp.PerfTest;

/// <summary>
/// EF Core command interceptor that tallies SQL commands by verb (SELECT/UPDATE/INSERT/DELETE/Other).
/// Used by the perf scenarios to quantify DB noise per configuration — the whole point of the
/// DB-push feature is to reduce these numbers.
/// <para>
/// Set <see cref="CaptureSql"/> to <c>true</c> to also record a histogram of distinct command
/// texts (parameter values stripped; one-liner normalized). Off by default — capture has a
/// hash + dictionary cost per command.
/// </para>
/// </summary>
public sealed class CommandCountingInterceptor : DbCommandInterceptor
{
    private long _select;
    private long _update;
    private long _insert;
    private long _delete;
    private long _other;
    private readonly ConcurrentDictionary<string, long> _byText = new(StringComparer.Ordinal);

    public bool CaptureSql { get; set; }

    public long Select => Interlocked.Read(ref _select);
    public long Update => Interlocked.Read(ref _update);
    public long Insert => Interlocked.Read(ref _insert);
    public long Delete => Interlocked.Read(ref _delete);
    public long Other => Interlocked.Read(ref _other);
    public long Total => Select + Update + Insert + Delete + Other;

    public IReadOnlyDictionary<string, long> CapturedByText => _byText;

    public void Reset()
    {
        Interlocked.Exchange(ref _select, 0);
        Interlocked.Exchange(ref _update, 0);
        Interlocked.Exchange(ref _insert, 0);
        Interlocked.Exchange(ref _delete, 0);
        Interlocked.Exchange(ref _other, 0);
        _byText.Clear();
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
        if (CaptureSql)
        {
            _byText.AddOrUpdate(NormalizeSql(sql), 1L, static (_, v) => v + 1L);
        }

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

    /// <summary>
    /// Collapse whitespace runs and strip EF-style comments / SET LOCAL prefixes so functionally
    /// equivalent queries hash to the same key. Parameters are already $1/@p form in the
    /// captured text, so no value stripping is needed.
    /// </summary>
    private static string NormalizeSql(string sql)
    {
        var sb = new System.Text.StringBuilder(sql.Length);
        var lastWhitespace = false;
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

            // Skip line-comments through the surrounding newline.
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var newline = sql.IndexOf('\n', i);
                i = newline < 0 ? sql.Length : newline + 1;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWhitespace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWhitespace = true;
                }

                i++;
                continue;
            }

            sb.Append(c);
            lastWhitespace = false;
            i++;
        }

        if (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
        }

        return sb.ToString();
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
