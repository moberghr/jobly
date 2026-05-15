using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Warp.Core.Data.Entities;
using Warp.Core.Data.Queries;

namespace Warp.Worker.Services;

/// <summary>
/// Server heartbeat: refreshes <c>LastHeartbeatTime</c>, CPU/memory metrics, and the
/// pause-state snapshot workers consult before each poll. No distributed lock — every
/// server must heartbeat independently.
/// </summary>
public sealed class Heartbeat<TContext> : IServerTask
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _time;
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly ProcessCpuTracker _cpuTracker;
    private readonly WarpWorkerConfiguration _configuration;
    private readonly IWarpSqlQueries<TContext> _sqlQueries;

    public Heartbeat(
        TContext context,
        TimeProvider time,
        PauseStateHolder pauseStateHolder,
        ProcessCpuTracker cpuTracker,
        IOptions<WarpWorkerConfiguration> configuration,
        IWarpSqlQueries<TContext> sqlQueries)
    {
        _context = context;
        _time = time;
        _pauseStateHolder = pauseStateHolder;
        _cpuTracker = cpuTracker;
        _configuration = configuration.Value;
        _sqlQueries = sqlQueries;
    }

    public string Name => "Heartbeat";

    public string? LockKey => null;

    public TimeSpan? DefaultInterval => _configuration.HealthCheckInterval;

    public bool RerunImmediately => false;

    public bool LogOnSuccess => false;

    public async Task<string?> ExecuteAsync(CancellationToken ct)
    {
        // One round-trip via CTE+JOIN (PG) / table-variable+chained-SELECT (MSSQL) — folds
        // the heartbeat UPDATE, the server paused_at read, AND the worker_group pause-state
        // read into a single query. Saves two DB hops per tick (every 3s by default).
        var now = _time.GetUtcNow().UtcDateTime;
        var snapshot = _cpuTracker.Sample(now);
        var memoryBytes = snapshot?.WorkingSet;
        var cpuPercent = snapshot?.CpuPercent;

        var result = await _sqlQueries.HeartbeatAsync(
            _context,
            _configuration.ServerId,
            now,
            memoryBytes,
            cpuPercent,
            ct);

        if (result == null)
        {
            throw new InvalidOperationException("Server not found in the database.");
        }

        _pauseStateHolder.Update(result.ServerPausedAt != null, new Dictionary<Guid, bool>(result.GroupPaused));

        return null;
    }
}
