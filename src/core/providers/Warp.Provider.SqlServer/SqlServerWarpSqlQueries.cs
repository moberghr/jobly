using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Warp.Core.Data.Entities;
using Warp.Core.Data.Queries;
using Warp.Core.Entities;
using Warp.Core.Enums;

namespace Warp.Provider.SqlServer;

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
public sealed class SqlServerWarpSqlQueries<TContext> : IWarpSqlQueries<TContext>
    where TContext : DbContext
{
    private const string Separator = "\x1F"; // ASCII "unit separator" — safe: queue names / GUIDs won't contain it

    private readonly WarpJobTableNames _n;

    private readonly string _claimEnqueuedJobsSql;
    private readonly string _lockNextEnqueuedMessageSql;
    private readonly string _lockStaleProcessingJobsSql;
    private readonly string _lockJobByIdWaitSql;
    private readonly string _lockAllServersSql;
    private readonly string _heartbeatSql;
    private readonly string _activateScheduledJobsSql;

    public SqlServerWarpSqlQueries(WarpJobTableNames names)
    {
        _n = names;

        var table = QualifiedTable();
        var serverTable = QualifiedServerTable();

        // Atomic claim via UPDATE with OUTPUT. The candidates CTE picks up to N rows in
        // queue/schedule order with ROWLOCK+UPDLOCK+READPAST so concurrent workers skip each
        // other's locked rows. The UPDATE mutates the same rows and OUTPUT INSERTED.* streams
        // them back — caller gets tracked Job entities and sees post-update state. Pause is
        // enforced in C# via PauseStateHolder before the worker calls into this query — pause
        // has heartbeat-cadence latency, see PauseStateHolder for details.
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

        _lockJobByIdWaitSql = $@"
            SELECT *
            FROM {table} WITH (ROWLOCK, UPDLOCK)
            WHERE [{_n.Id}] = {{0}}";

        _lockAllServersSql = $@"
            SELECT *
            FROM {serverTable} WITH (ROWLOCK, UPDLOCK)";

        // Table variable + chained SELECT folds three queries (UPDATE server, SELECT
        // server.paused_at, SELECT worker_group.id+paused_at) into ONE round-trip. The UPDATE
        // OUTPUTs the post-update server row into @h; the chained SELECT joins worker_group
        // for this server. Cardinality: 0 rows if the server doesn't exist; max(1, # groups)
        // rows otherwise (LEFT JOIN keeps the server row even with zero groups, group_id NULL).
        var groupTable = QualifiedWorkerGroupTable();
        _heartbeatSql = $@"
            DECLARE @h TABLE (server_id uniqueidentifier, paused_at datetime2);
            UPDATE {serverTable}
            SET [{_n.ServerLastHeartbeatTime}] = {{1}},
                [{_n.ServerMemoryWorkingSetBytes}] = ISNULL({{2}}, [{_n.ServerMemoryWorkingSetBytes}]),
                [{_n.ServerCpuUsagePercent}] = ISNULL({{3}}, [{_n.ServerCpuUsagePercent}])
            OUTPUT INSERTED.[{_n.ServerId}], INSERTED.[{_n.ServerPausedAt}] INTO @h
            WHERE [{_n.ServerId}] = {{0}};
            SELECT h.paused_at AS server_paused_at,
                   w.[{_n.WorkerGroupId}] AS group_id,
                   w.[{_n.WorkerGroupPausedAt}] AS group_paused_at
            FROM @h h
            LEFT JOIN {groupTable} w ON w.[{_n.WorkerGroupServerId}] = h.server_id;";

        // Atomic activation: UPDATE ... OUTPUT INSERTED.queue flips due rows AND streams back
        // the queue names so the caller can fire one JobEnqueued notification per distinct
        // queue. Replaces the previous SELECT-DISTINCT-then-UPDATE pattern (2 round-trips).
        _activateScheduledJobsSql = $@"
            UPDATE {table}
            SET [{_n.CurrentState}] = {(int)State.Enqueued}
            OUTPUT INSERTED.[{_n.Queue}]
            WHERE [{_n.CurrentState}] = {(int)State.Scheduled}
              AND [{_n.ScheduleTime}] <= {{0}}";
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
        // Raw ADO.NET: multi-statement batch (DECLARE @h + UPDATE OUTPUT INTO + SELECT JOIN)
        // produces a composite result that doesn't match any single EF entity.
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
        // but OUTPUT INSERTED.queue only streams back one column.
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using var cmd = conn.CreateCommand();

            // SQL Server requires explicit Transaction binding when the connection is in a
            // pending local transaction. Npgsql is lenient but we set it for both providers
            // for consistency.
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

        await using var tx = await context.Database.BeginTransactionAsync(ct);

        // sp_getapplock with LockOwner Transaction auto-releases on COMMIT/ROLLBACK; LockTimeout
        // 0 means try-then-fail rather than block (matches PG's try_advisory). Return value
        // semantics: 0 granted; 1 granted-after-wait; negative codes mean not acquired
        // (-1 timeout, -2 cancelled, -3 deadlock, -999 error).
        var conn = context.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        cmd.CommandText = @"
            DECLARE @rc int;
            EXEC @rc = sp_getapplock @Resource = @p0,
                                      @LockMode = N'Exclusive',
                                      @LockOwner = N'Transaction',
                                      @LockTimeout = 0;
            SELECT @rc;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@p0";
        p.Value = lockKey;
        cmd.Parameters.Add(p);
        var raw = await cmd.ExecuteScalarAsync(ct);
        var rc = raw is int i ? i : Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);

        if (rc < 0)
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
            ? $"[{_n.Table}]"
            : $"[{_n.Schema}].[{_n.Table}]";
    }

    private string QualifiedServerTable()
    {
        return string.IsNullOrEmpty(_n.ServerSchema)
            ? $"[{_n.ServerTable}]"
            : $"[{_n.ServerSchema}].[{_n.ServerTable}]";
    }

    private string QualifiedWorkerGroupTable()
    {
        return string.IsNullOrEmpty(_n.WorkerGroupSchema)
            ? $"[{_n.WorkerGroupTable}]"
            : $"[{_n.WorkerGroupSchema}].[{_n.WorkerGroupTable}]";
    }
}
