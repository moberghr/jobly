using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    private readonly string _heartbeatSql;
    private readonly string _activateScheduledJobsSql;

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

        // CTE+LEFT JOIN folds three queries (UPDATE server, SELECT server.paused_at,
        // SELECT worker_group.id+paused_at) into ONE round-trip. The CTE runs the UPDATE and
        // returns the post-update server row; the outer SELECT joins worker_group for this
        // server. Cardinality: 0 rows if the server doesn't exist; max(1, # groups) rows
        // otherwise (LEFT JOIN keeps the server row even with zero groups, with group_id = NULL).
        _heartbeatSql = $@"
            WITH heartbeat AS (
                UPDATE {serverTable}
                SET ""{_n.ServerLastHeartbeatTime}"" = {{1}},
                    ""{_n.ServerMemoryWorkingSetBytes}"" = COALESCE({{2}}, ""{_n.ServerMemoryWorkingSetBytes}""),
                    ""{_n.ServerCpuUsagePercent}"" = COALESCE({{3}}, ""{_n.ServerCpuUsagePercent}"")
                WHERE ""{_n.ServerId}"" = {{0}}
                RETURNING ""{_n.ServerId}"" AS id, ""{_n.ServerPausedAt}"" AS paused_at
            )
            SELECT h.paused_at AS server_paused_at,
                   w.""{_n.WorkerGroupId}"" AS group_id,
                   w.""{_n.WorkerGroupPausedAt}"" AS group_paused_at
            FROM heartbeat h
            LEFT JOIN {QualifiedWorkerGroupTable()} w ON w.""{_n.WorkerGroupServerId}"" = h.id";

        // Atomic activation: UPDATE ... RETURNING queue flips due rows AND streams back the
        // queue names so the caller can fire one JobEnqueued notification per distinct queue.
        // Replaces the previous SELECT-DISTINCT-then-UPDATE pattern (2 round-trips).
        _activateScheduledJobsSql = $@"
            UPDATE {table}
            SET ""{_n.CurrentState}"" = {(int)State.Enqueued}
            WHERE ""{_n.CurrentState}"" = {(int)State.Scheduled}
              AND ""{_n.ScheduleTime}"" <= {{0}}
            RETURNING ""{_n.Queue}""";
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

    public async Task<HeartbeatResult?> HeartbeatAsync(
        TContext context,
        Guid serverId,
        DateTime now,
        long? memoryBytes,
        double? cpuPercent,
        CancellationToken ct)
    {
        // Raw ADO.NET: the CTE+JOIN returns a flat (server_paused_at, group_id, group_paused_at)
        // shape that doesn't match any single EF entity. Issuing the command directly avoids
        // contorting EF Core into materializing this composite result.
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using var cmd = conn.CreateCommand();

            cmd.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = _heartbeatSql
                .Replace("{0}", "@p0", StringComparison.Ordinal)
                .Replace("{1}", "@p1", StringComparison.Ordinal)
                .Replace("{2}", "@p2", StringComparison.Ordinal)
                .Replace("{3}", "@p3", StringComparison.Ordinal);
            AddParameter(cmd, "@p0", serverId);
            AddParameter(cmd, "@p1", now);
            AddParameter(cmd, "@p2", (object?)memoryBytes ?? DBNull.Value);
            AddParameter(cmd, "@p3", (object?)cpuPercent ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            DateTime? serverPausedAt = null;
            var groupPaused = new Dictionary<Guid, bool>();
            var anyRow = false;
            while (await reader.ReadAsync(ct))
            {
                anyRow = true;
                if (!await reader.IsDBNullAsync(0, ct))
                {
                    serverPausedAt = reader.GetDateTime(0);
                }

                // group_id is NULL for the LEFT JOIN row when the server has no worker groups.
                if (!await reader.IsDBNullAsync(1, ct))
                {
                    var groupId = reader.GetGuid(1);
                    var groupIsPaused = !await reader.IsDBNullAsync(2, ct);
                    groupPaused[groupId] = groupIsPaused;
                }
            }

            return anyRow ? new HeartbeatResult(serverPausedAt, groupPaused) : null;
        }
        finally
        {
            if (openedHere)
            {
                await conn.CloseAsync();
            }
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    public async Task<List<string>> ActivateScheduledJobsAsync(
        TContext context,
        DateTime now,
        CancellationToken ct)
    {
        // Raw ADO.NET: EF Core's FromSqlRaw<Job> would try to materialize full Job entities,
        // but the SQL only RETURNS the queue column. Issuing the command directly is cleaner
        // and matches how the notification transport opens connections.
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using var cmd = conn.CreateCommand();

            // Bind to ambient transaction if any — when called from the xact-lock path the
            // connection is in a pending local transaction. Npgsql tolerates a null
            // Transaction here, but SQL Server doesn't, and we use the same shape both
            // places for consistency.
            cmd.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = _activateScheduledJobsSql.Replace("{0}", "@p0", StringComparison.Ordinal);
            var param = cmd.CreateParameter();
            param.ParameterName = "@p0";
            param.Value = now;
            cmd.Parameters.Add(param);

            var queues = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                queues.Add(reader.GetString(0));
            }

            return queues;
        }
        finally
        {
            if (openedHere)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<(bool LockHeld, T? Result)> RunUnderTransactionLockAsync<T>(
        TContext context,
        string lockKey,
        Func<TContext, CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);

        var key = AdvisoryLockKey.Compute(lockKey);

        await using var tx = await context.Database.BeginTransactionAsync(ct);

        // pg_try_advisory_xact_lock is bound to the current transaction — auto-released on
        // COMMIT/ROLLBACK so we never need a separate pg_advisory_unlock round-trip.
        var conn = context.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        cmd.CommandText = "SELECT pg_try_advisory_xact_lock(@p0)";
        var p = cmd.CreateParameter();
        p.ParameterName = "@p0";
        p.Value = key;
        cmd.Parameters.Add(p);
        var raw = await cmd.ExecuteScalarAsync(ct);
        var held = raw is bool b && b;

        if (!held)
        {
            await tx.RollbackAsync(ct);

            return (false, default);
        }

        try
        {
            var result = await work(context, ct);
            await tx.CommitAsync(ct);

            return (true, result);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
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

    private string QualifiedWorkerGroupTable()
    {
        return string.IsNullOrEmpty(_n.WorkerGroupSchema)
            ? $"\"{_n.WorkerGroupTable}\""
            : $"\"{_n.WorkerGroupSchema}\".\"{_n.WorkerGroupTable}\"";
    }
}
