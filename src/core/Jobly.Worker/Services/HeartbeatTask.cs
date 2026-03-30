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
    private TimeSpan _previousCpuTime;
    private DateTime _previousWallTime;

    public HeartbeatTask(
        IServiceScopeFactory scopeFactory,
        ILogger<HeartbeatTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration)
        : base(scopeFactory, logger, configuration)
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        _previousCpuTime = process.TotalProcessorTime;
        _previousWallTime = DateTime.UtcNow;
    }

    protected override string TaskName => "Heartbeat";

    protected override TimeSpan DefaultInterval => Configuration.HealthCheckInterval;

    protected override bool LogOnSuccess => false;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var server = await context.Set<Server>()
            .FindAsync([ServerId], ct) ?? throw new InvalidOperationException("Server not found in the database.");
        server.LastHeartbeatTime = DateTime.UtcNow;

        var process = System.Diagnostics.Process.GetCurrentProcess();
        server.MemoryWorkingSetBytes = process.WorkingSet64;

        var currentCpuTime = process.TotalProcessorTime;
        var currentWallTime = DateTime.UtcNow;
        var wallElapsed = (currentWallTime - _previousWallTime).TotalMilliseconds;

        if (wallElapsed > 0)
        {
            var cpuElapsed = (currentCpuTime - _previousCpuTime).TotalMilliseconds;
            server.CpuUsagePercent = Math.Round(cpuElapsed / wallElapsed / Environment.ProcessorCount * 100, 1);
        }

        _previousCpuTime = currentCpuTime;
        _previousWallTime = currentWallTime;

        // Heartbeat saves its own changes (no ServerLog write from base — too noisy)
        await context.SaveChangesAsync(ct);
        return null;
    }
}
