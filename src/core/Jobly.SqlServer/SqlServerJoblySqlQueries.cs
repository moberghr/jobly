using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Jobly.SqlServer;

/// <summary>
/// SQL Server-specific SQL queries using <c>WITH (ROWLOCK, UPDLOCK, READPAST)</c> directly in
/// the FROM clause (not via a regex-rewriting interceptor). SQL templates are built once from
/// the EF model and cached on this singleton.
/// <para>
/// SQL Server doesn't support array parameters, so multi-valued filters (queue list, dead
/// server ids) are expanded into comma-separated strings and split via <c>STRING_SPLIT</c>
/// inside the query. Per-call cost is still just parameter binding + a small constant for
/// <c>string.Join</c>.
/// </para>
/// </summary>
public sealed class SqlServerJoblySqlQueries<TContext> : IJoblySqlQueries<TContext>
    where TContext : DbContext
{
    private const string Separator = "\x1F"; // ASCII "unit separator" — safe: queue names / GUIDs won't contain it

    private readonly JoblyJobTableNames _n;

    private readonly string _claimEnqueuedJobsSql;
    private readonly string _lockNextEnqueuedMessageSql;
    private readonly string _lockStaleProcessingJobsSql;
    private readonly string _lockJobByIdSkipSql;
    private readonly string _lockJobByIdWaitSql;
    private readonly string _lockAllServersSql;

    public SqlServerJoblySqlQueries(JoblyJobTableNames names)
    {
        _n = names;

        var table = QualifiedTable();
        var serverTable = QualifiedServerTable();

        // Atomic claim via UPDATE with OUTPUT. The candidates CTE picks up to N rows in
        // queue/schedule order with ROWLOCK+UPDLOCK+READPAST so concurrent workers skip each
        // other's locked rows. The UPDATE mutates the same rows and OUTPUT INSERTED.* streams
        // them back — caller gets tracked Job entities and sees post-update state.
        _claimEnqueuedJobsSql = $@"
            WITH candidates AS (
                SELECT TOP ({{3}}) *
                FROM {table} WITH (ROWLOCK, UPDLOCK, READPAST)
                WHERE [{_n.Kind}] = {(int)JobKind.Job}
                  AND [{_n.CurrentState}] = {(int)State.Enqueued}
                  AND [{_n.Queue}] IN (SELECT value FROM STRING_SPLIT({{2}}, N'{Separator}'))
                ORDER BY [{_n.Queue}], [{_n.ScheduleTime}]
            )
            UPDATE candidates
            SET [{_n.CurrentState}] = {(int)State.Processing},
                [{_n.CurrentWorkerId}] = {{0}},
                [{_n.LastKeepAlive}] = {{1}}
            OUTPUT INSERTED.*";

        _lockNextEnqueuedMessageSql = $@"
            SELECT TOP (1) *
            FROM {table} WITH (ROWLOCK, UPDLOCK, READPAST)
            WHERE [{_n.Kind}] = {(int)JobKind.Message}
              AND [{_n.CurrentState}] = {(int)State.Enqueued}
            ORDER BY [{_n.Queue}], [{_n.ScheduleTime}]";

        _lockStaleProcessingJobsSql = $@"
            SELECT *
            FROM {table} WITH (ROWLOCK, UPDLOCK, READPAST)
            WHERE [{_n.Kind}] = {(int)JobKind.Job}
              AND [{_n.CurrentState}] = {(int)State.Processing}
              AND [{_n.LastKeepAlive}] IS NOT NULL
              AND [{_n.LastKeepAlive}] < {{0}}";

        _lockJobByIdSkipSql = $@"
            SELECT *
            FROM {table} WITH (ROWLOCK, UPDLOCK, READPAST)
            WHERE [{_n.Id}] = {{0}}";

        _lockJobByIdWaitSql = $@"
            SELECT *
            FROM {table} WITH (ROWLOCK, UPDLOCK)
            WHERE [{_n.Id}] = {{0}}";

        _lockAllServersSql = $@"
            SELECT *
            FROM {serverTable} WITH (ROWLOCK, UPDLOCK)";
    }

    public async Task<List<Job>> ClaimEnqueuedJobsAsync(
        TContext context,
        string[] queues,
        Guid workerId,
        DateTime now,
        int limit,
        CancellationToken ct)
    {
        var queueCsv = string.Join(Separator, queues);
        return await context.Set<Job>()
            .FromSqlRaw(_claimEnqueuedJobsSql, workerId, now, queueCsv, limit)
            .ToListAsync(ct);
    }

    public async Task<Job?> LockNextEnqueuedMessageAsync(TContext context, CancellationToken ct)
    {
        return await context.Set<Job>()
            .FromSqlRaw(_lockNextEnqueuedMessageSql)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Job>> LockStaleProcessingJobsAsync(
        TContext context,
        DateTime cutoff,
        CancellationToken ct)
    {
        return await context.Set<Job>()
            .FromSqlRaw(_lockStaleProcessingJobsSql, cutoff)
            .ToListAsync(ct);
    }

    public async Task<Job?> LockJobByIdAsync(TContext context, Guid jobId, CancellationToken ct)
    {
        return await context.Set<Job>()
            .FromSqlRaw(_lockJobByIdSkipSql, jobId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Job?> LockJobByIdWaitAsync(TContext context, Guid jobId, CancellationToken ct)
    {
        return await context.Set<Job>()
            .FromSqlRaw(_lockJobByIdWaitSql, jobId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Server>> LockAllServersAsync(TContext context, CancellationToken ct)
    {
        return await context.Set<Server>()
            .FromSqlRaw(_lockAllServersSql)
            .ToListAsync(ct);
    }

    private string QualifiedTable()
    {
        return string.IsNullOrEmpty(_n.Schema)
            ? $"[{_n.Table}]"
            : $"[{_n.Schema}].[{_n.Table}]";
    }

    private string QualifiedServerTable()
    {
        return string.IsNullOrEmpty(_n.ServerSchema)
            ? $"[{_n.ServerTable}]"
            : $"[{_n.ServerSchema}].[{_n.ServerTable}]";
    }
}
