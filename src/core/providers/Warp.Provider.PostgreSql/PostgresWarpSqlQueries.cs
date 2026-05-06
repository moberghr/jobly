using Microsoft.EntityFrameworkCore;
using Warp.Core.Data.Entities;
using Warp.Core.Data.Queries;
using Warp.Core.Entities;
using Warp.Core.Enums;

namespace Warp.Provider.PostgreSql;

/// <summary>
/// PostgreSQL-specific SQL queries that use <c>FOR UPDATE [SKIP LOCKED]</c> directly in the
/// statement (not via a regex-rewriting interceptor). SQL templates are built once from the
/// EF model and cached on this singleton — per-call cost is just parameter binding.
/// </summary>
public sealed class PostgresWarpSqlQueries<TContext> : IWarpSqlQueries<TContext>
    where TContext : DbContext
{
    private readonly WarpJobTableNames _n;

    private readonly string _claimEnqueuedJobsSql;
    private readonly string _lockNextEnqueuedMessageSql;
    private readonly string _lockStaleProcessingJobsSql;
    private readonly string _lockJobByIdWaitSql;
    private readonly string _lockAllServersSql;

    public PostgresWarpSqlQueries(WarpJobTableNames names)
    {
        _n = names;

        var table = QualifiedTable();
        var serverTable = QualifiedServerTable();

        // Atomic claim: subquery with FOR UPDATE SKIP LOCKED picks up to N eligible rows
        // (State=Enqueued) in queue-priority/schedule order, outer UPDATE flips to Processing
        // and RETURNING pipes the full row back. Pause is enforced in C# via PauseStateHolder
        // before the worker calls into this query — pause has heartbeat-cadence latency, see
        // PauseStateHolder for details.
        _claimEnqueuedJobsSql = $@"
            UPDATE {table} AS t
            SET ""{_n.CurrentState}"" = {(int)State.Processing},
                ""{_n.CurrentWorkerId}"" = {{0}},
                ""{_n.LastKeepAlive}"" = {{1}}
            FROM (
                SELECT ""{_n.Id}"" AS id
                FROM {table}
                WHERE ""{_n.Kind}"" = {(int)JobKind.Job}
                  AND ""{_n.CurrentState}"" = {(int)State.Enqueued}
                  AND ""{_n.Queue}"" = ANY({{2}})
                ORDER BY ""{_n.Queue}"", ""{_n.ScheduleTime}""
                LIMIT {{3}}
                FOR UPDATE SKIP LOCKED
            ) AS c
            WHERE t.""{_n.Id}"" = c.id
            RETURNING t.*";

        _lockNextEnqueuedMessageSql = $@"
            SELECT * FROM {table}
            WHERE ""{_n.Kind}"" = {(int)JobKind.Message}
              AND ""{_n.CurrentState}"" = {(int)State.Enqueued}
            ORDER BY ""{_n.Queue}"", ""{_n.ScheduleTime}""
            LIMIT 1
            FOR NO KEY UPDATE SKIP LOCKED";

        _lockStaleProcessingJobsSql = $@"
            SELECT * FROM {table}
            WHERE ""{_n.Kind}"" = {(int)JobKind.Job}
              AND ""{_n.CurrentState}"" = {(int)State.Processing}
              AND ""{_n.LastKeepAlive}"" IS NOT NULL
              AND ""{_n.LastKeepAlive}"" < {{0}}
            FOR NO KEY UPDATE SKIP LOCKED";

        _lockJobByIdWaitSql = $@"
            SELECT * FROM {table}
            WHERE ""{_n.Id}"" = {{0}}
            FOR NO KEY UPDATE";

        _lockAllServersSql = $@"
            SELECT * FROM {serverTable}
            FOR NO KEY UPDATE";
    }

    public async Task<List<Job>> ClaimEnqueuedJobsAsync(
        TContext context,
        string[] queues,
        Guid workerId,
        DateTime now,
        int limit,
        CancellationToken ct)
    {
        return await context.Set<Job>()
            .FromSqlRaw(_claimEnqueuedJobsSql, workerId, now, queues, limit)
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
            ? $"\"{_n.Table}\""
            : $"\"{_n.Schema}\".\"{_n.Table}\"";
    }

    private string QualifiedServerTable()
    {
        return string.IsNullOrEmpty(_n.ServerSchema)
            ? $"\"{_n.ServerTable}\""
            : $"\"{_n.ServerSchema}\".\"{_n.ServerTable}\"";
    }
}
