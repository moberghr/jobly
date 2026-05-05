using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Warp.Core.Data.Entities;

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

    public Heartbeat(
        TContext context,
        TimeProvider time,
        PauseStateHolder pauseStateHolder,
        ProcessCpuTracker cpuTracker,
        IOptions<WarpWorkerConfiguration> configuration)
    {
        _context = context;
        _time = time;
        _pauseStateHolder = pauseStateHolder;
        _cpuTracker = cpuTracker;
        _configuration = configuration.Value;
    }

    public string Name => "Heartbeat";

    public string? LockKey => null;

    public TimeSpan? DefaultInterval => _configuration.HealthCheckInterval;

    public bool RerunImmediately => false;

    public bool LogOnSuccess => false;

    public async Task<string?> ExecuteAsync(CancellationToken ct)
    {
        // Use ExecuteUpdate (no entity load, no change tracker) so the heartbeat write doesn't
        // pull a Server snapshot whose PausedAt could go stale before we read it back. Then
        // read PausedAt fresh below — every iteration of this task sees the latest committed
        // pause state, never a load-time snapshot.
        var now = _time.GetUtcNow().UtcDateTime;
        var snapshot = _cpuTracker.Sample(now);
        var memoryBytes = snapshot?.WorkingSet;
        var cpuPercent = snapshot?.CpuPercent;

        var affected = await _context.Set<Server>()
            .Where(s => s.Id == _configuration.ServerId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(p => p.LastHeartbeatTime, now)
                    .SetProperty(p => p.MemoryWorkingSetBytes, p => memoryBytes ?? p.MemoryWorkingSetBytes)
                    .SetProperty(p => p.CpuUsagePercent, p => cpuPercent ?? p.CpuUsagePercent),
                ct);

        if (affected == 0)
        {
            throw new InvalidOperationException("Server not found in the database.");
        }

        // Pause-state snapshot. Both reads are fresh DB queries — no change tracker, no
        // load-time staleness. AsNoTracking() makes the projections explicit.
        var serverPausedAt = await _context.Set<Server>()
            .AsNoTracking()
            .Where(s => s.Id == _configuration.ServerId)
            .Select(s => s.PausedAt)
            .FirstOrDefaultAsync(ct);

        var groupPauseStates = await _context.Set<WorkerGroup>()
            .AsNoTracking()
            .Where(g => g.ServerId == _configuration.ServerId)
            .ToDictionaryAsync(g => g.Id, g => g.PausedAt != null, ct);
        _pauseStateHolder.Update(serverPausedAt != null, groupPauseStates);

        return null;
    }
}
