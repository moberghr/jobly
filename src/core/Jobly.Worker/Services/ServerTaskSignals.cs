using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Services;

/// <summary>
/// In-process push → wake plumbing for server tasks. Producers (workers, the notification
/// listener, scheduled-job activation) call the named <c>SignalXxx</c> methods. Consumers
/// — the <see cref="ServerTaskLoop{TContext}"/>s for <see cref="Orchestrator{TContext}"/>
/// and <see cref="MessageRouter{TContext}"/> — subscribe at host construction and tear
/// down on shutdown via the returned <see cref="IDisposable"/>.
/// </summary>
/// <remarks>
/// A dedicated method per signal (instead of a keyed registry) matches how Jobly's signal
/// surface is actually used: a small, domain-fixed set that changes rarely. Adding a
/// signal is a one-line addition here plus whatever task code needs it — no keys, no
/// stringly typed channels.
/// </remarks>
public sealed class ServerTaskSignals<TContext>
    where TContext : DbContext
{
    private readonly List<Action> _jobFinalizedWakers = [];
    private readonly List<Action> _messageEnqueuedWakers = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Called by workers after finalising a job (or by the notification listener on a
    /// <c>JobFinalized</c> push) — wakes the Orchestrator loop.
    /// </summary>
    public void SignalJobFinalized() => Fire(_jobFinalizedWakers);

    /// <summary>
    /// Called by the notification listener on a <c>MessageEnqueued</c> push — wakes the
    /// MessageRouter loop.
    /// </summary>
    public void SignalMessageEnqueued() => Fire(_messageEnqueuedWakers);

    internal Subscription SubscribeJobFinalized(Action wake) =>
        Subscribe(_jobFinalizedWakers, wake);

    internal Subscription SubscribeMessageEnqueued(Action wake) =>
        Subscribe(_messageEnqueuedWakers, wake);

    private Subscription Subscribe(List<Action> list, Action wake)
    {
        lock (_gate)
        {
            list.Add(wake);
        }

        return new Subscription(list, wake, _gate);
    }

    private void Fire(List<Action> list)
    {
        Action[] snapshot;
        lock (_gate)
        {
            snapshot = [.. list];
        }

        foreach (var waker in snapshot)
        {
            waker();
        }
    }

    internal sealed class Subscription : IDisposable
    {
        private readonly List<Action> _list;
        private readonly Lock _gate;
        private Action? _wake;

        public Subscription(List<Action> list, Action wake, Lock gate)
        {
            _list = list;
            _gate = gate;
            _wake = wake;
        }

        public void Dispose()
        {
            if (_wake is null)
            {
                return;
            }

            lock (_gate)
            {
                _list.Remove(_wake);
            }

            _wake = null;
        }
    }
}
