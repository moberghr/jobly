using System.Diagnostics;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobly.Worker.Services;

/// <summary>
/// Per-task loop driven by <see cref="ServerTaskHost{TContext}"/>. Owns the task's lifecycle
/// bookkeeping (ServerTask row registration, ServerLog writes), the signal semaphore, and
/// the lock + scope primitive that calls into <see cref="IServerTask.ExecuteAsync"/>.
/// </summary>
internal sealed class ServerTaskLoop<TContext> : IDisposable
    where TContext : DbContext
{
    private readonly Type _taskType;
    private readonly string _name;
    private readonly string? _lockKey;
    private readonly TimeSpan _defaultInterval;
    private readonly bool _rerunImmediately;
    private readonly bool _logOnSuccess;

    private readonly IServiceScopeFactory _scopes;
    private readonly IJoblyLockProvider _lockProvider;
    private readonly TimeProvider _time;
    private readonly Guid _serverId;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _signal = new(0, 1);
    private int? _serverTaskId;

    public ServerTaskLoop(
        IServerTask template,
        IServiceScopeFactory scopes,
        IJoblyLockProvider lockProvider,
        TimeProvider time,
        Guid serverId,
        ILogger logger)
    {
        _taskType = template.GetType();
        _name = template.Name;
        _lockKey = template.LockKey;
        _defaultInterval = template.DefaultInterval
            ?? throw new ArgumentException(
                $"Cannot build a loop for {template.GetType().Name}: DefaultInterval is null (auto-run disabled).",
                nameof(template));
        _rerunImmediately = template.RerunImmediately;
        _logOnSuccess = template.LogOnSuccess;
        _scopes = scopes;
        _lockProvider = lockProvider;
        _time = time;
        _serverId = serverId;
        _logger = logger;
    }

    public string Name => _name;

    public Type TaskType => _taskType;

    /// <summary>
    /// Wake the loop's <see cref="WaitForNextRunAsync"/> — next iteration starts immediately.
    /// No-op if the semaphore already has a pending signal.
    /// </summary>
    public void Signal()
    {
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    /// <summary>
    /// Main auto-run loop. Registers the ServerTask row, then repeatedly: reads interval,
    /// runs the task under its lock, writes bookkeeping, waits for the next tick.
    /// </summary>
    internal async Task RunAsync(CancellationToken ct)
    {
        await EnsureRegisteredAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var interval = await GetIntervalAsync(ct);
            if (interval == null)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var sw = Stopwatch.StartNew();
            var didWork = false;
            try
            {
                var message = await ExecuteInLockedScopeAsync(ct);
                if (message == null)
                {
                    await TryUpdateServerTaskAsync("Skipped", null, sw.Elapsed.TotalMilliseconds);
                }
                else
                {
                    didWork = true;
                    await TryUpdateServerTaskAsync("Completed", message, sw.Elapsed.TotalMilliseconds);
                    if (_logOnSuccess)
                    {
                        await TryWriteServerLogAsync("Completed", message, sw.Elapsed.TotalMilliseconds);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Server task {Name} failed", _name);
                await TryUpdateServerTaskAsync("Failed", ex.Message, sw.Elapsed.TotalMilliseconds);
                await TryWriteServerLogAsync("Failed", ex.Message, sw.Elapsed.TotalMilliseconds);
            }

            if (didWork && _rerunImmediately)
            {
                continue;
            }

            await WaitForNextRunAsync(interval.Value, ct);
        }
    }

    /// <summary>
    /// Test-only entry point. Runs the task once inside its lock + scope with no bookkeeping
    /// and no swallowed exceptions. Test-triggered runs never write ServerTask/ServerLog rows,
    /// so the dashboard history reflects only auto-scheduled runs.
    /// </summary>
    internal Task<string?> RunOnceAsync(CancellationToken ct) => ExecuteInLockedScopeAsync(ct);

    private async Task<string?> ExecuteInLockedScopeAsync(CancellationToken ct)
    {
        IAsyncDisposable? handle = null;
        if (_lockKey != null)
        {
            handle = await _lockProvider.TryAcquireAsync(_lockKey, TimeSpan.Zero, ct);
            if (handle == null)
            {
                return null;
            }
        }

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var task = scope.ServiceProvider
                .GetServices<IServerTask>()
                .First(x => x.GetType() == _taskType);

            return await task.ExecuteAsync(ct);
        }
        finally
        {
            if (handle != null)
            {
                await handle.DisposeAsync();
            }
        }
    }

    private async Task EnsureRegisteredAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var existing = await context.Set<ServerTask>()
            .Where(x => x.ServerId == _serverId)
            .Where(x => x.TaskName == _name)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            existing.IntervalSeconds = _defaultInterval.TotalSeconds;
            await context.SaveChangesAsync(ct);
            _serverTaskId = existing.Id;

            return;
        }

        var entity = new ServerTask
        {
            ServerId = _serverId,
            TaskName = _name,
            IntervalSeconds = _defaultInterval.TotalSeconds,
        };
        context.Set<ServerTask>().Add(entity);
        await context.SaveChangesAsync(ct);
        _serverTaskId = entity.Id;
    }

    private async Task<TimeSpan?> GetIntervalAsync(CancellationToken ct)
    {
        if (_serverTaskId == null)
        {
            return null;
        }

        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var seconds = await context.Set<ServerTask>()
            .Where(x => x.Id == _serverTaskId)
            .Select(x => x.IntervalSeconds)
            .FirstOrDefaultAsync(ct);

        return seconds.HasValue ? TimeSpan.FromSeconds(seconds.Value) : null;
    }

    private async Task WaitForNextRunAsync(TimeSpan interval, CancellationToken ct)
    {
        try
        {
            await _signal.WaitAsync(interval, ct);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down; let the outer loop's token check exit cleanly.
        }
    }

    private async Task UpdateServerTaskAsync(string status, string? message, double durationMs)
    {
        if (_serverTaskId == null)
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var now = _time.GetUtcNow().UtcDateTime;

        await context.Set<ServerTask>()
            .Where(x => x.Id == _serverTaskId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LastStatus, status)
                .SetProperty(x => x.LastMessage, message)
                .SetProperty(x => x.LastRun, now)
                .SetProperty(x => x.LastDurationMs, durationMs));
    }

    private async Task TryUpdateServerTaskAsync(string status, string? message, double durationMs)
    {
        try
        {
            await UpdateServerTaskAsync(status, message, durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update ServerTask for {Name}", _name);
        }
    }

    private async Task WriteServerLogAsync(string status, string? message, double durationMs)
    {
        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        context.Set<ServerLog>().Add(new ServerLog
        {
            ServerId = _serverId,
            ServerTaskId = _serverTaskId,
            Status = status,
            Message = message,
            DurationMs = durationMs,
            Timestamp = _time.GetUtcNow().UtcDateTime,
        });
        await context.SaveChangesAsync();
    }

    private async Task TryWriteServerLogAsync(string status, string? message, double durationMs)
    {
        try
        {
            await WriteServerLogAsync(status, message, durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write ServerLog for {Name}", _name);
        }
    }

    public void Dispose() => _signal.Dispose();
}
