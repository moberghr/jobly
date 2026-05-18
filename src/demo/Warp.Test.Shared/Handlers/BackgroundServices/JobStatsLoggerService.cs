using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Warp.Core;
using Warp.Core.BackgroundServices;
using Warp.Core.Entities;
using Warp.Core.Enums;

namespace Warp.Test.Shared.Handlers.BackgroundServices;

/// <summary>
/// Demo cluster-singleton background service. Every 10 seconds, queries the Job table and
/// logs a state-count summary. Demonstrates <see cref="ServiceScope.Singleton"/> — start a
/// second host and only ONE of the two will run this service at a time; the other waits.
/// Watch the dashboard's lease panel — it will show which server currently holds the lease.
/// </summary>
/// <remarks>
/// Also demonstrates the recommended dependency-injection pattern: the service is a
/// singleton, so we inject <see cref="IServiceScopeFactory"/> and create a fresh scope per
/// work cycle. Injecting <c>TestContext</c> (or any other scoped service) directly into the
/// constructor would be a captive-dependency bug — the same DbContext would be held for the
/// life of the process.
/// </remarks>
public sealed class JobStatsLoggerService : WarpBackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<JobStatsLoggerService> _logger;

    public JobStatsLoggerService(IServiceScopeFactory scopes, ILogger<JobStatsLoggerService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public override ServiceScope Scope => ServiceScope.Singleton;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("JobStatsLoggerService acquired the lease; reporting job stats every 10s");

        while (!ct.IsCancellationRequested)
        {
            await ReportStatsAsync(ct);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("JobStatsLoggerService stopping (cancellation observed)");

                return;
            }
        }
    }

    private async Task ReportStatsAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestContext>();

        var counts = await context.Set<Job>()
            .GroupBy(x => x.CurrentState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var enqueued = counts.FirstOrDefault(x => x.State == State.Enqueued)?.Count ?? 0;
        var processing = counts.FirstOrDefault(x => x.State == State.Processing)?.Count ?? 0;
        var completed = counts.FirstOrDefault(x => x.State == State.Completed)?.Count ?? 0;
        var failed = counts.FirstOrDefault(x => x.State == State.Failed)?.Count ?? 0;

        _logger.LogInformation(
            "Job stats — Enqueued: {Enqueued}, Processing: {Processing}, Completed: {Completed}, Failed: {Failed}",
            enqueued,
            processing,
            completed,
            failed);
    }
}
