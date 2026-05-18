using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core.BackgroundServices;
using Warp.Core.Events;

namespace Warp.Worker.BackgroundServices;

/// <summary>
/// Strategy for <see cref="ServiceScope.Singleton"/> services. Acquires a cluster-wide lease
/// via <see cref="IBackgroundServiceLeaseCoordinator"/>; returns <c>null</c> when another
/// server holds a live lease. Subscribes to the <c>BackgroundServiceLeaseLost</c> signal
/// channel — on signal for this service's name, cancels the per-execution CTS so user code
/// observes cancellation without waiting for the next poll.
/// </summary>
internal sealed class SingletonServiceStrategy<TContext> : IBackgroundServiceStrategy
    where TContext : DbContext
{
    private readonly string _serviceName;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeSpan _leaseTtl;
    private readonly ServerTaskSignals<TContext> _signals;

    public SingletonServiceStrategy(
        string serviceName,
        IServiceScopeFactory scopes,
        TimeSpan leaseTtl,
        ServerTaskSignals<TContext> signals)
    {
        _serviceName = serviceName;
        _scopes = scopes;
        _leaseTtl = leaseTtl;
        _signals = signals;
    }

    public ServiceScope Scope => ServiceScope.Singleton;

    /// <inheritdoc/>
    public async Task<BackgroundServiceExecutionScope?> AcquireAsync(CancellationToken hostStoppingToken)
    {
        using var scope = _scopes.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IBackgroundServiceLeaseCoordinator>();
        var acquired = await coordinator.TryAcquireAsync(_serviceName, _leaseTtl, hostStoppingToken);

        if (!acquired)
        {
            return null;
        }

        // Build a linked CTS: cancelled when the host stops OR when the BackgroundServiceLeaseLost
        // signal fires for this service's name. User code receives the linked token so it observes
        // cancellation immediately on lease loss without waiting for a poll cycle.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(hostStoppingToken);
        var signalSubscription = _signals.SubscribeBackgroundServiceLeaseLost(
            name =>
            {
                if (string.Equals(name, _serviceName, StringComparison.Ordinal))
                {
                    linkedCts.Cancel();
                }
            });

        var release = new SingletonRelease(_serviceName, _scopes, linkedCts, signalSubscription);

        return new BackgroundServiceExecutionScope(release, linkedCts.Token);
    }

    private sealed class SingletonRelease : IAsyncDisposable
    {
        private readonly string _serviceName;
        private readonly IServiceScopeFactory _scopes;
        private readonly CancellationTokenSource _linkedCts;
        private readonly IDisposable _signalSubscription;

        public SingletonRelease(
            string serviceName,
            IServiceScopeFactory scopes,
            CancellationTokenSource linkedCts,
            IDisposable signalSubscription)
        {
            _serviceName = serviceName;
            _scopes = scopes;
            _linkedCts = linkedCts;
            _signalSubscription = signalSubscription;
        }

        public async ValueTask DisposeAsync()
        {
            // Unsubscribe first so the signal doesn't fire into a disposed CTS.
            _signalSubscription.Dispose();
            _linkedCts.Dispose();

            // Release the lease using a fresh short-budget CTS because the host token
            // may already be cancelled at this point (graceful shutdown path).
            using var releaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                using var scope = _scopes.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IBackgroundServiceLeaseCoordinator>();
                await coordinator.ReleaseAsync(_serviceName, releaseCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort. If the release fails (DB down, already expired), the TTL
                // covers it — another server will take over when the lease expires.
            }
        }
    }
}
