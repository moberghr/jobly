using Warp.Core.BackgroundServices;

namespace Warp.Worker.BackgroundServices;

/// <summary>
/// Strategy for <see cref="ServiceScope.PerServer"/> services. Acquires immediately and
/// unconditionally — every server runs its own independent copy, no coordination required.
/// </summary>
internal sealed class PerServerServiceStrategy : IBackgroundServiceStrategy
{
    public ServiceScope Scope => ServiceScope.PerServer;

    /// <inheritdoc/>
    public Task<BackgroundServiceExecutionScope?> AcquireAsync(CancellationToken hostStoppingToken)
    {
        var scope = new BackgroundServiceExecutionScope(release: null, hostStoppingToken);

        return Task.FromResult<BackgroundServiceExecutionScope?>(scope);
    }
}
