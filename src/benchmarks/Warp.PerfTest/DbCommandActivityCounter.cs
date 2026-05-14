using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Warp.PerfTest;

/// <summary>
/// Counts every DB command issued through Npgsql by listening on Npgsql's activity source.
/// Unlike <see cref="CommandCountingInterceptor"/> (an EF Core interceptor), this captures
/// commands issued via raw <c>DbConnection.CreateCommand()</c> — Warp's atomic SQL methods
/// (HeartbeatAsync, ActivateScheduledJobsAsync) go through that path and would otherwise
/// be invisible to the EF interceptor.
/// <para>
/// We listen for activities that carry a <c>db.statement</c> tag — Npgsql tags every command
/// execution with the SQL text (per OpenTelemetry db semantic conventions). This excludes
/// connection-lifecycle activities (open/close) which don't carry a statement.
/// </para>
/// </summary>
public sealed class DbCommandActivityCounter : IDisposable
{
    private readonly ActivityListener _listener;
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

    public DbCommandActivityCounter()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, "Npgsql", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnActivityStopped,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _select, 0);
        Interlocked.Exchange(ref _update, 0);
        Interlocked.Exchange(ref _insert, 0);
        Interlocked.Exchange(ref _delete, 0);
        Interlocked.Exchange(ref _other, 0);
        _byText.Clear();
    }

    public void Dispose() => _listener.Dispose();

    private void OnActivityStopped(Activity activity)
    {
        // Only count activities that represent an actual command execution. Npgsql tags
        // those with db.statement (the SQL text). Connection open/close, broker setup,
        // etc. don't carry a statement and aren't queries we care about.
        var statement = activity.GetTagItem("db.statement") as string
            ?? activity.GetTagItem("db.query.text") as string;
        if (string.IsNullOrEmpty(statement))
        {
            return;
        }

        if (CaptureSql)
        {
            _byText.AddOrUpdate(NormalizeSql(statement), 1L, static (_, v) => v + 1L);
        }

        TallyVerb(statement);
    }

    private void TallyVerb(string sql)
    {
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
            // WITH-CTEs and other multi-statement batches land here. Counting them as Other
            // is honest — they're real round-trips that don't fit the single-verb taxonomy.
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

    private static string NormalizeSql(string sql)
    {
        var sb = new System.Text.StringBuilder(sql.Length);
        var lastWhitespace = false;
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

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
}
