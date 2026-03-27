using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Jobly health manager will be responsible for managing the health of the Jobly worker.
/// 
/// </summary>
public class JoblyHealthManager<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly ILogger<JoblyHealthManager<TContext>> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly JoblyWorkerConfiguration _configuration;

    public JoblyHealthManager(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<JoblyHealthManager<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _configuration = configuration.Value;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await UpdateHeartbeat(context);
            await CleanUpServers(context);
            await CleanupExpiredJobs(context);

            await Task.Delay(_configuration.HealthCheckInterval, stoppingToken);
        }

        await RemoveServer();
    }

    private async Task UpdateHeartbeat(TContext context)
    {
        var server = await context.Set<Server>()
            .FindAsync(_configuration.ServerId);
        if (server == null)
        {
            // This should only happen if this server has stalled and other server has deleted it.
            // All its jobs may have been failed.
            // TODO: should we throw an exception here?
            throw new InvalidOperationException("Server not found in the database.");
        }

        server.LastHeartbeatTime = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }
    
    private async Task CleanUpServers(TContext context)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var servers = await context.Set<Server>()
            .TagWith(InterceptorConstants.RowLockTableBatch)
            .ToListAsync();
        foreach (var server in servers)
        {
            if (DateTime.UtcNow - server.LastHeartbeatTime > _configuration.HealthCheckTimeout)
            {
                _logger.LogWarning("Server {ServerId} has not sent a heartbeat in {Timeout}. Removing it from the database.",
                    server.Id, _configuration.HealthCheckTimeout);
                var workerIds = await context.Set<Jobly.Core.Data.Entities.Worker>()
                    .Where(w => w.ServerId == server.Id)
                    .Select(w => w.Id)
                    .ToListAsync();

                context.Set<Server>().Remove(server);

                // Remove workers for this server
                var workers = await context.Set<Jobly.Core.Data.Entities.Worker>()
                    .Where(w => w.ServerId == server.Id)
                    .ToListAsync();
                context.Set<Jobly.Core.Data.Entities.Worker>().RemoveRange(workers);

                var jobs = await context.Set<Job>()
                    .Where(x => x.CurrentState == State.Processing)
                    .Where(x => x.CurrentWorkerId != null && workerIds.Contains(x.CurrentWorkerId.Value))
                    .ToListAsync();
                foreach (var job in jobs)
                {
                    job.CurrentState = State.Failed;
                    context.Set<JobState>().Add(new JobState
                    {
                        JobId = job.Id,
                        State = State.Failed,
                        DateTime = DateTime.UtcNow,
                        Message = $"The job {job.Id} failed because of timeout."
                    });
                }
            }
        }
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    
    private async Task CleanupExpiredJobs(TContext context)
    {
        // Get expired job IDs
        var expiredJobIds = await context.Set<Job>()
            .Where(x => x.ExpireAt != null && x.ExpireAt < DateTime.UtcNow)
            .Select(x => x.Id)
            .Take(_configuration.ExpirationBatchSize)
            .ToListAsync();

        if (expiredJobIds.Count == 0) return;

        // Delete logs, states, then jobs (respecting FK order)
        await context.Set<JobLog>()
            .Where(x => expiredJobIds.Contains(x.JobId))
            .ExecuteDeleteAsync();

        await context.Set<JobState>()
            .Where(x => expiredJobIds.Contains(x.JobId))
            .ExecuteDeleteAsync();

        await context.Set<Job>()
            .Where(x => expiredJobIds.Contains(x.Id))
            .ExecuteDeleteAsync();

        // Cleanup expired messages
        await context.Set<Message>()
            .Where(x => x.ExpireAt != null && x.ExpireAt < DateTime.UtcNow)
            .Take(_configuration.ExpirationBatchSize)
            .ExecuteDeleteAsync();

        _logger.LogInformation("Cleaned up {count} expired jobs", expiredJobIds.Count);
    }

    private async Task RemoveServer()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        
        var server = await context.Set<Server>()
            .FindAsync(_configuration.ServerId);;
        
        if (server == null)
        {
            // This should only happen if this server has stalled and other server has deleted it.
            // All its jobs may have been failed.
            _logger.LogWarning("Server {ServerId} not found in the database. Skipping removal.", _configuration.ServerId);
            return;
        }
        // Remove workers for this server
        var workers = await context.Set<Jobly.Core.Data.Entities.Worker>()
            .Where(w => w.ServerId == server.Id)
            .ToListAsync();
        context.Set<Jobly.Core.Data.Entities.Worker>().RemoveRange(workers);

        context.Set<Server>().Remove(server);
        await context.SaveChangesAsync();
    }
}