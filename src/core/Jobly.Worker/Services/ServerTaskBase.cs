using System.Diagnostics;
using Jobly.Core.Data.Entities;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Base class for server background tasks. Handles the loop, try/catch, ServerTask updates,
/// ServerLog writes, and optional distributed locking.
/// </summary>
public abstract class ServerTaskBase<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IDistributedLock? _distributedLock;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _signal = new(0);
    private int? _serverTaskId;

    protected ServerTaskBase(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        TimeProvider timeProvider,
        string? lockName = null,
        IDistributedLockProvider? lockProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration.Value;
        _timeProvider = timeProvider;
        _distributedLock = lockName != null && lockProvider != null
            ? lockProvider.CreateLock(lockName)
            : null;
    }

    protected abstract string TaskName { get; }

    protected abstract TimeSpan DefaultInterval { get; }

    protected abstract Task<string?> RunServerTask(TContext context, CancellationToken ct);

    /// <summary>
    /// Whether to write a ServerLog entry on each successful run. Default true.
    /// Override to false for high-frequency tasks like heartbeat.
    /// </summary>
    protected virtual bool LogOnSuccess => true;

    /// <summary>
    /// Whether to re-run immediately when work was found. Default true.
    /// Override to false for tasks that should always wait for their interval.
    /// </summary>
    protected virtual bool RerunImmediately => true;

    protected Guid ServerId => _configuration.ServerId;

    protected JoblyWorkerConfiguration Configuration => _configuration;

    protected TimeProvider TimeProvider => _timeProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Register the ServerTask row on first run
        await EnsureServerTaskRegistered(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Read interval from DB (allows runtime changes + disable via null)
            var interval = await GetInterval(stoppingToken);
            if (interval == null)
            {
                // Task is disabled — check again in 10s
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            var sw = Stopwatch.StartNew();
            var didWork = false;
            try
            {
                if (_distributedLock != null)
                {
                    await using var handle = await _distributedLock.TryAcquireAsync(timeout: TimeSpan.Zero, stoppingToken);
                    if (handle == null)
                    {
                        await UpdateServerTask("Skipped", "Lock held by another server", sw.Elapsed.TotalMilliseconds);
                        await WaitForNextRun(interval.Value, stoppingToken);
                        continue;
                    }

                    didWork = await RunAndLog(sw, stoppingToken);
                }
                else
                {
                    didWork = await RunAndLog(sw, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Server task {TaskName} failed", TaskName);
                await TryUpdateServerTask("Failed", ex.Message, sw.Elapsed.TotalMilliseconds);
                await TryWriteServerLog("Failed", ex.Message, sw.Elapsed.TotalMilliseconds);
            }

            // If work was done and task supports re-run, run again immediately
            if (didWork && RerunImmediately)
            {
                continue;
            }

            await WaitForNextRun(interval.Value, stoppingToken);
        }
    }

    /// <summary>
    /// Signal the task to wake up and check for work immediately.
    /// </summary>
    public void Signal()
    {
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    private async Task WaitForNextRun(TimeSpan interval, CancellationToken stoppingToken)
    {
        await _signal.WaitAsync(interval, stoppingToken);
    }

    private async Task<bool> RunAndLog(Stopwatch sw, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var message = await RunServerTask(context, ct);

        // Always update the ServerTask row (last run info)
        await UpdateServerTask("Completed", message, sw.Elapsed.TotalMilliseconds);

        if (LogOnSuccess)
        {
            await WriteServerLog("Completed", message, sw.Elapsed.TotalMilliseconds);
        }

        return message != null; // non-null message = work was done
    }

    private async Task EnsureServerTaskRegistered(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var existing = await context.Set<ServerTask>()
            .Where(x => x.ServerId == _configuration.ServerId && x.TaskName == TaskName)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            existing.IntervalSeconds = DefaultInterval.TotalSeconds;
            _serverTaskId = existing.Id;
        }
        else
        {
            var task = new ServerTask
            {
                ServerId = _configuration.ServerId,
                TaskName = TaskName,
                IntervalSeconds = DefaultInterval.TotalSeconds,
            };
            context.Set<ServerTask>().Add(task);
            await context.SaveChangesAsync(ct);
            _serverTaskId = task.Id;
        }

        await context.SaveChangesAsync(ct);
    }

    private async Task<TimeSpan?> GetInterval(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var seconds = await context.Set<ServerTask>()
            .Where(x => x.Id == _serverTaskId)
            .Select(x => x.IntervalSeconds)
            .FirstOrDefaultAsync(ct);

        return seconds.HasValue ? TimeSpan.FromSeconds(seconds.Value) : null;
    }

    private async Task UpdateServerTask(string status, string? message, double durationMs)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var task = await context.Set<ServerTask>().FindAsync(_serverTaskId);
        if (task != null)
        {
            task.LastStatus = status;
            task.LastMessage = message;
            task.LastRun = _timeProvider.GetUtcNow().UtcDateTime;
            task.LastDurationMs = durationMs;
            await context.SaveChangesAsync();
        }
    }

    private async Task TryUpdateServerTask(string status, string? message, double durationMs)
    {
        try
        {
            await UpdateServerTask(status, message, durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update ServerTask for {TaskName}", TaskName);
        }
    }

    private async Task WriteServerLog(string status, string? message, double durationMs)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        context.Set<ServerLog>().Add(new ServerLog
        {
            ServerId = _configuration.ServerId,
            ServerTaskId = _serverTaskId,
            Status = status,
            Message = message,
            DurationMs = durationMs,
            Timestamp = _timeProvider.GetUtcNow().UtcDateTime,
        });
        await context.SaveChangesAsync();
    }

    private async Task TryWriteServerLog(string status, string? message, double durationMs)
    {
        try
        {
            await WriteServerLog(status, message, durationMs);
        }
        catch (Exception logEx)
        {
            _logger.LogWarning(logEx, "Failed to write ServerLog for {TaskName}", TaskName);
        }
    }
}
