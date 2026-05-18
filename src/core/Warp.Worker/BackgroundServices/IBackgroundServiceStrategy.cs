using Warp.Core.BackgroundServices;

namespace Warp.Worker.BackgroundServices;

/// <summary>
/// Acquisition strategy for a <c>WarpBackgroundService</c>. Encapsulates how the supervisor
/// decides whether to invoke user code now or wait:
/// <list type="bullet">
/// <item><see cref="PerServerServiceStrategy"/> — always acquires immediately (no coordination).</item>
/// <item><see cref="SingletonServiceStrategy{TContext}"/> — acquires the cluster lease; returns
/// <c>null</c> when another server holds it.</item>
/// </list>
/// Open/closed for future strategies without changing the supervisor.
/// </summary>
internal interface IBackgroundServiceStrategy
{
    /// <summary>
    /// Attempt to acquire the right to run. Returns a populated
    /// <see cref="BackgroundServiceExecutionScope"/> when acquired, or <c>null</c> when
    /// acquisition was not possible (e.g., singleton lease held by another server).
    /// </summary>
    Task<BackgroundServiceExecutionScope?> AcquireAsync(CancellationToken hostStoppingToken);

    /// <summary>
    /// The scope kind this strategy implements. Used by the supervisor for lifecycle log
    /// messages (Started vs LeaseAcquired) and for the status it sets on the instance row
    /// while waiting.
    /// </summary>
    ServiceScope Scope { get; }
}
