using Jobly.Core.Data.Entities;
using Jobly.Core.Data.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Removes Server rows (and their Worker / WorkerGroup children) whose last heartbeat is
/// older than <see cref="JoblyWorkerConfiguration.HealthCheckTimeout"/>. This is the
/// ungraceful-shutdown cleanup path — <see cref="JoblyServerRegistration{TContext}.StopAsync"/>
/// handles the graceful case.
/// </summary>
public sealed class ServerCleanup<TContext> : IServerTask
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _time;
    private readonly IJoblySqlQueries<TContext> _sqlQueries;
    private readonly JoblyWorkerConfiguration _configuration;

    public ServerCleanup(
        TContext context,
        TimeProvider time,
        IJoblySqlQueries<TContext> sqlQueries,
        IOptions<JoblyWorkerConfiguration> configuration)
    {
        _context = context;
        _time = time;
        _sqlQueries = sqlQueries;
        _configuration = configuration.Value;
    }

    public string Name => "ServerCleanup";

    public string? LockKey => "jobly:server-cleanup";

    public TimeSpan? DefaultInterval => _configuration.ServerCleanupInterval;

    public bool RerunImmediately => false;

    public async Task<string?> ExecuteAsync(CancellationToken ct)
    {
        var count = await CleanUpServersAsync(ct);

        return count > 0 ? $"Removed {count} stale servers" : null;
    }

    internal async Task<int> CleanUpServersAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var removedCount = 0;

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        var servers = await _sqlQueries.LockAllServersAsync(_context, ct);
        foreach (var server in servers)
        {
            if (now - server.LastHeartbeatTime <= _configuration.HealthCheckTimeout)
            {
                continue;
            }

            var workers = await _context.Set<Jobly.Core.Data.Entities.Worker>()
                .Where(x => x.ServerId == server.Id)
                .ToListAsync(ct);
            _context.Set<Jobly.Core.Data.Entities.Worker>().RemoveRange(workers);

            // WorkerGroup rows are FK-linked to Server without OnDelete(Cascade) — crash
            // recovery has to clean them up explicitly. JoblyServerRegistration.StopAsync
            // handles the graceful-shutdown case; this handles the ungraceful one.
            var workerGroups = await _context.Set<WorkerGroup>()
                .Where(x => x.ServerId == server.Id)
                .ToListAsync(ct);
            _context.Set<WorkerGroup>().RemoveRange(workerGroups);

            _context.Set<Server>().Remove(server);

            removedCount++;
        }

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return removedCount;
    }
}
