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
    private readonly IInterceptorService _interceptorService;

    public JoblyHealthManager(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<JoblyHealthManager<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration, IInterceptorService interceptorService)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _interceptorService = interceptorService;
        _configuration = configuration.Value;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RegisterServer();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await UpdateHeartbeat(context);
            await CleanUpServers(context, scope, stoppingToken);
            
            await Task.Delay(_configuration.HealthCheckInterval, stoppingToken);
        }
        
        await RemoveServer();
    }

    private async Task RegisterServer()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        
        // Register the server in the database.
        var now = DateTime.UtcNow;
        var server = new Server
        {
            Id = _configuration.ServerId,
            StartedTime = now,
            LastHeartbeatTime = now,
            ServiceCount = 0 // Cant set this yet.
        };
        await context.Set<Server>().AddAsync(server);
        await context.SaveChangesAsync();
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
    
    private async Task CleanUpServers(TContext context, IServiceScope scope, CancellationToken cancellationToken)
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
                context.Set<Server>().Remove(server);
                var jobs = await context.Set<Job>()
                    .Where(x => x.CurrentState == State.Processing)
                    .Where(x => x.CurrentServerId == server.Id)
                    .ToListAsync(cancellationToken: cancellationToken);
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

                    var interceptorPipeline = _interceptorService.CreateInterceptorPipeline(context, job, scope);
                    await _interceptorService.RunJobExecutionFailedInterceptors(interceptorPipeline, cancellationToken);
                }
            }
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
        context.Set<Server>().Remove(server);
        await context.SaveChangesAsync();
    }
}