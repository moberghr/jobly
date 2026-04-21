using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

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
    private readonly JoblyWorkerConfiguration _configuration;

    public Heartbeat(
        TContext context,
        TimeProvider time,
        PauseStateHolder pauseStateHolder,
        ProcessCpuTracker cpuTracker,
        IOptions<JoblyWorkerConfiguration> configuration)
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
        var server = await _context.Set<Server>()
            .FindAsync([_configuration.ServerId], ct)
            ?? throw new InvalidOperationException("Server not found in the database.");

        server.LastHeartbeatTime = _time.GetUtcNow().UtcDateTime;

        var snapshot = _cpuTracker.Sample(_time.GetUtcNow().UtcDateTime);
        if (snapshot != null)
        {
            server.MemoryWorkingSetBytes = snapshot.Value.WorkingSet;
            if (snapshot.Value.CpuPercent.HasValue)
            {
                server.CpuUsagePercent = snapshot.Value.CpuPercent.Value;
            }
        }

        await _context.SaveChangesAsync(ct);

        var groupPauseStates = await _context.Set<WorkerGroup>()
            .Where(g => g.ServerId == _configuration.ServerId)
            .ToDictionaryAsync(g => g.Id, g => g.PausedAt != null, ct);
        _pauseStateHolder.Update(server.PausedAt != null, groupPauseStates);

        return null;
    }
}
