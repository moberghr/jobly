namespace Warp.Worker.BackgroundServices;

/// <summary>
/// Carries the <see cref="CancellationToken"/> that should be passed to
/// <c>WarpBackgroundService.ExecuteAsync</c>, plus an optional <see cref="IAsyncDisposable"/>
/// that the supervisor calls when the execution scope ends (lease release, linked-CTS teardown).
/// </summary>
internal sealed class BackgroundServiceExecutionScope
{
    public BackgroundServiceExecutionScope(IAsyncDisposable? release, CancellationToken token)
    {
        Token = token;
        Release = release;
    }

    /// <summary>
    /// The token to pass to user code. For <see cref="Warp.Core.BackgroundServices.ServiceScope.PerServer"/>
    /// services this is the host's stopping token; for
    /// <see cref="Warp.Core.BackgroundServices.ServiceScope.Singleton"/> services it is a linked
    /// token that fires when the host stops OR when the lease is lost.
    /// </summary>
    public CancellationToken Token { get; }

    /// <summary>
    /// Releases the resources acquired during <c>AcquireAsync</c> — e.g. disposing the linked
    /// <see cref="CancellationTokenSource"/> and releasing the singleton lease.
    /// <c>null</c> for per-server services (nothing to release).
    /// </summary>
    public IAsyncDisposable? Release { get; }
}
