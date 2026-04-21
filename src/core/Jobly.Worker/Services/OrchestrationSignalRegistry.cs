using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Services;

/// <summary>
/// DI-resolved replacement for <c>OrchestrationTask&lt;TContext&gt;._instances</c>. Anything
/// that wants to wake the orchestration loop (e.g. <see cref="NotificationListenerTask{TContext}"/>
/// on a JobFinalized push) resolves this registry and calls <see cref="Signal"/>. The
/// per-server <see cref="ServerTaskLoop{TContext}"/> for the orchestrator registers a waker at
/// startup and disposes it on shutdown.
/// </summary>
public sealed class OrchestrationSignalRegistry<TContext>
    where TContext : DbContext
{
    private readonly List<Action> _wakers = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Register a waker (typically <c>() =&gt; semaphore.Release()</c>). Dispose the returned
    /// handle to unregister — leaking it would leak a closure across the host lifetime.
    /// </summary>
    internal IDisposable Register(Action wake)
    {
        lock (_gate)
        {
            _wakers.Add(wake);
        }

        return new Registration(this, wake);
    }

    /// <summary>
    /// Wake every registered waker. Snapshot under the lock so handler callbacks can register
    /// or unregister without re-entrancy deadlocks.
    /// </summary>
    public void Signal()
    {
        Action[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _wakers];
        }

        foreach (var waker in snapshot)
        {
            waker();
        }
    }

    private void Unregister(Action wake)
    {
        lock (_gate)
        {
            _wakers.Remove(wake);
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly OrchestrationSignalRegistry<TContext> _registry;
        private Action? _wake;

        public Registration(OrchestrationSignalRegistry<TContext> registry, Action wake)
        {
            _registry = registry;
            _wake = wake;
        }

        public void Dispose()
        {
            if (_wake is null)
            {
                return;
            }

            _registry.Unregister(_wake);
            _wake = null;
        }
    }
}
