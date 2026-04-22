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
    private readonly ServerTaskSignal[] _signals;

    private readonly IServiceScopeFactory _scopes;
    private readonly IJoblyLockProvider _lockProvider;
    private readonly TimeProvider _time;
    private readonly Guid _serverId;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Lock _signalLock = new();
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
        _signals = [.. template.Signals];
        _scopes = scopes;
        _lockProvider = lockProvider;
        _time = time;
        _serverId = serverId;
        _logger = logger;
    }

    public string Name => _name;

    public Type TaskType => _taskType;

    public IReadOnlyList<ServerTaskSignal> Signals => _signals;

    /// <summary>
    /// Wake the loop's <see cref="WaitForNextRunAsync"/> — next iteration starts immediately.
    /// No-op if the semaphore already has a pending signal. The lock serializes concurrent
    /// callers so that two threads can't both observe <c>CurrentCount == 0</c> and both call
    /// <c>Release()</c> — which would exceed the max count of 1 and throw.
    /// (Same pattern as <c>JoblyDispatcher.SignalAll</c>.)
    /// </summary>
    public void Signal()
    {
        lock (_signalLock)
        {
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }
    }

    /// <summary>
    /// Scheduling loop. Registers the ServerTask row once, then alternates between running
    /// one iteration (see <see cref="RunOneIterationAsync"/>) and waiting for the next tick
    /// or wake signal. All bookkeeping + exception handling lives in the inner method; this
    /// one is pure control flow.
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

            var didWork = await RunOneIterationAsync(ct);
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (didWork && _rerunImmediately)
            {
                continue;
            }

            await WaitForNextRunAsync(interval.Value, ct);
        }
    }

    /// <summary>
    /// Runs the task once under its lock, writes the ServerTask / ServerLog bookkeeping, and
    /// swallows exceptions after logging them. Returns <c>true</c> when work was done (so the
    /// outer loop can re-run immediately if <see cref="IServerTask.RerunImmediately"/> is set),
    /// <c>false</c> otherwise.
    /// </summary>
    private async Task<bool> RunOneIterationAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var message = await ExecuteInLockedScopeAsync(ct);
            var elapsed = sw.Elapsed.TotalMilliseconds;

            if (message == null)
            {
                await TryUpdateServerTaskAsync("Skipped", null, elapsed);
                return false;
            }

            await TryUpdateServerTaskAsync("Completed", message, elapsed);
            if (_logOnSuccess)
            {
                await TryWriteServerLogAsync("Completed", message, elapsed);
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Server task {Name} failed", _name);
            var elapsed = sw.Elapsed.TotalMilliseconds;
            await TryUpdateServerTaskAsync("Failed", ex.Message, elapsed);
            await TryWriteServerLogAsync("Failed", ex.Message, elapsed);

            return false;
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
