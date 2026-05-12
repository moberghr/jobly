using Warp.Core.Events;

namespace Warp.Worker.Services;

/// <summary>
/// Contract for a background server task. A task is a plain DI-registered unit of work;
/// <see cref="ServerTaskHost{TContext}"/> drives it: takes the distributed lock (when
/// <see cref="LockKey"/> is set), opens a fresh scope per iteration, calls
/// <see cref="ExecuteAsync"/>, and writes the resulting <c>ServerTask</c> / <c>ServerLog</c>
/// rows.
/// </summary>
/// <remarks>
/// Implementers MUST call <c>SaveChangesAsync</c> before returning from
/// <see cref="ExecuteAsync"/>. The host opens a new scope for bookkeeping, so any tracker
/// state left behind inside the task's own scope is discarded — but the task still has to
/// commit its own work.
/// </remarks>
public interface IServerTask
{
    /// <summary>
    /// Display name shown on the dashboard and used as the <c>ServerTask</c> row key for
    /// this server.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Distributed-lock key, or <c>null</c> if this task may run on every server
    /// independently (e.g. heartbeat).
    /// </summary>
    string? LockKey { get; }

    /// <summary>
    /// Auto-run interval. Returning <c>null</c> disables the auto-run loop for this task;
    /// the host will not schedule it. The task stays resolvable via DI for manual triggers.
    /// </summary>
    TimeSpan? DefaultInterval { get; }

    /// <summary>
    /// Do the work. Return a non-null status message when work was performed (drives the
    /// re-run and log-on-success decisions in the host loop); return <c>null</c> when
    /// there was nothing to do. Must call <c>SaveChangesAsync</c> before returning.
    /// </summary>
    Task<string?> ExecuteAsync(CancellationToken ct);

    /// <summary>
    /// When <c>true</c> (default), the host re-runs the task immediately if the last call
    /// returned non-null. Override to <c>false</c> for tasks that should always wait for
    /// their configured interval.
    /// </summary>
    bool RerunImmediately => true;

    /// <summary>
    /// When <c>true</c> (default), the host writes a <c>ServerLog</c> row on each
    /// successful run. Override to <c>false</c> for high-frequency tasks like heartbeat.
    /// </summary>
    bool LogOnSuccess => true;

    /// <summary>
    /// Push-event channels that should wake this task's loop. Default: none (pure polling).
    /// The host subscribes the loop's <c>Signal</c> method to each declared channel on
    /// <see cref="ServerTaskSignals{TContext}"/> at startup and unsubscribes on shutdown.
    /// </summary>
    IEnumerable<ServerTaskSignal> Signals => [];
}
