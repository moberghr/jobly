using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Updates server heartbeat and CPU/memory metrics.
/// No distributed lock — each server must heartbeat independently.
/// </summary>
public class HeartbeatTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    private readonly PauseStateHolder _pauseStateHolder;
    private TimeSpan? _previousCpuTime;
    private DateTime _previousWallTime;

    public HeartbeatTask(
        IServiceScopeFactory scopeFactory,
        ILogger<HeartbeatTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        TimeProvider timeProvider,
        PauseStateHolder pauseStateHolder)
        : base(scopeFactory, logger, configuration, timeProvider)
    {
        _pauseStateHolder = pauseStateHolder;

        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            _previousCpuTime = process.TotalProcessorTime;
        }
        catch
        {
            // Process metrics not available in this environment
        }

        _previousWallTime = timeProvider.GetUtcNow().UtcDateTime;
    }

    protected override string TaskName => "Heartbeat";

    protected override TimeSpan DefaultInterval => Configuration.HealthCheckInterval;

    protected override bool RerunImmediately => false;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var server = await context.Set<Server>()
            .FindAsync([ServerId], ct) ?? throw new InvalidOperationException("Server not found in the database.");
        server.LastHeartbeatTime = TimeProvider.GetUtcNow().UtcDateTime;

        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            server.MemoryWorkingSetBytes = process.WorkingSet64;

            if (_previousCpuTime.HasValue)
            {
                var currentCpuTime = process.TotalProcessorTime;
                var currentWallTime = TimeProvider.GetUtcNow().UtcDateTime;
                var wallElapsed = (currentWallTime - _previousWallTime).TotalMilliseconds;

                if (wallElapsed > 0)
                {
                    var cpuElapsed = (currentCpuTime - _previousCpuTime.Value).TotalMilliseconds;
                    server.CpuUsagePercent = Math.Round(cpuElapsed / wallElapsed / Environment.ProcessorCount * 100, 1);
                }

                _previousCpuTime = currentCpuTime;
                _previousWallTime = currentWallTime;
            }
        }
        catch
        {
            // Process metrics not available — heartbeat still updates LastHeartbeatTime
        }

        await context.SaveChangesAsync(ct);

        // Update pause state for workers to read
        var groupPauseStates = await context.Set<WorkerGroup>()
            .Where(g => g.ServerId == ServerId)
            .ToDictionaryAsync(g => g.Id, g => g.PausedAt != null, ct);
        _pauseStateHolder.Update(server.PausedAt != null, groupPauseStates);

        return null;
    }
}
