using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Services;

/// <summary>
/// DI-resolved replacement for <c>MessageRoutingTask&lt;TContext&gt;._instances</c>. See
/// <see cref="OrchestrationSignalRegistry{TContext}"/> for the rationale — this mirrors it for
/// message-routing wake-ups.
/// </summary>
public sealed class MessageRoutingSignalRegistry<TContext>
    where TContext : DbContext
{
    private readonly List<Action> _wakers = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Register a waker. Dispose the returned handle to unregister.
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
        private readonly MessageRoutingSignalRegistry<TContext> _registry;
        private Action? _wake;

        public Registration(MessageRoutingSignalRegistry<TContext> registry, Action wake)
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
